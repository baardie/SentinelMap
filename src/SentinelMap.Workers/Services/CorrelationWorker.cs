using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;
using SentinelMap.Domain.Messages;
using SentinelMap.Infrastructure.Correlation;
using SentinelMap.Infrastructure.Data;
using SentinelMap.SharedKernel.Enums;
using StackExchange.Redis;

namespace SentinelMap.Workers.Services;

/// <summary>
/// Extracted processing logic — testable without a BackgroundService host.
/// </summary>
public class CorrelationProcessor
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(7);
    private const double AutoMergeThreshold = 0.6;
    private const double ReviewThreshold = 0.3;

    private readonly IEntityRepository _entityRepo;
    private readonly IDatabase _db;
    private readonly IEnumerable<ICorrelationRule> _correlationRules;
    private readonly IAlertRepository _alertRepo;
    private readonly SystemDbContext? _systemDb;
    private readonly ILogger<CorrelationProcessor> _logger;

    public CorrelationProcessor(
        IEntityRepository entityRepo,
        IDatabase db,
        IEnumerable<ICorrelationRule> correlationRules,
        IAlertRepository alertRepo,
        ILogger<CorrelationProcessor> logger,
        SystemDbContext? systemDb = null)
    {
        _entityRepo = entityRepo;
        _db = db;
        _correlationRules = correlationRules;
        _alertRepo = alertRepo;
        _logger = logger;
        _systemDb = systemDb;
    }

    public async Task<EntityUpdatedMessage?> ProcessAsync(ObservationPublishedMessage msg, CancellationToken ct = default)
    {
        // Skip infrastructure and safety messages — they're not tracked entities
        if (msg.SourceType != "AIS" && msg.SourceType != "ADSB")
            return null;

        var entityType = msg.SourceType == "ADSB" ? EntityType.Aircraft : EntityType.Vessel;
        var cacheKey = $"correlation:link:{msg.SourceType}:{msg.ExternalId}";
        var cached = await _db.StringGetAsync(cacheKey);
        var position = new Point(msg.Longitude, msg.Latitude) { SRID = 4326 };

        if (!cached.IsNull && Guid.TryParse(cached.ToString(), out var entityId))
        {
            // Hot path: known entity — update position only
            await _entityRepo.UpdatePositionAsync(entityId, position, msg.SpeedMps, msg.Heading, msg.ObservedAt, ct);

            // Link observation to entity for historical track queries
            await LinkObservationToEntityAsync(msg.ObservationId, entityId, msg.ObservedAt, ct);

            _logger.LogDebug("Hot-path hit for {Source}:{ExternalId} → entity {EntityId}", msg.SourceType, msg.ExternalId, entityId);

            // Check for emergency squawk
            await CheckEmergencyAsync(msg, entityId, ct);

            return new EntityUpdatedMessage(entityId, msg.Longitude, msg.Latitude, msg.Heading, msg.SpeedMps,
                entityType.ToString(), EntityStatus.Active.ToString(), msg.ObservedAt, msg.DisplayName,
                msg.VesselType, msg.AircraftType, msg.Emergency, msg.IsMilitary);
        }

        // Cold path: check for existing entity match via correlation rules
        // For ADS-B, only use DirectIdMatch — aircraft don't share identifiers and
        // fuzzy/spatial matching causes false merges between different aircraft
        var searchRadius = Math.Max(5000, (msg.SpeedMps ?? 5) * 900 * 1.2); // 15min window
        var candidates = await _entityRepo.FindCandidatesAsync(position, searchRadius, TimeSpan.FromHours(24), ct);

        TrackedEntity? matchedEntity = null;
        double bestConfidence = 0;
        string? bestRuleId = null;
        var allScores = new List<CorrelationScore>();

        foreach (var candidate in candidates)
        {
            foreach (var rule in _correlationRules)
            {
                // Skip fuzzy and spatial rules for aircraft — only direct ID match
                if (msg.SourceType == "ADSB" && rule is not DirectIdMatchRule)
                    continue;

                var score = await rule.EvaluateAsync(msg.SourceType, msg.ExternalId, msg.DisplayName, position, candidate, ct);
                if (score != null)
                {
                    allScores.Add(score);
                    if (score.Confidence > bestConfidence)
                    {
                        bestConfidence = score.Confidence;
                        matchedEntity = candidate;
                        bestRuleId = score.RuleId;
                    }
                }
            }
        }

        if (matchedEntity != null && bestConfidence > AutoMergeThreshold)
        {
            // Link to existing entity — add new identifier and update position
            var identifierType = msg.SourceType == "ADSB" ? "ICAO" : "MMSI";
            var alreadyHasId = matchedEntity.Identifiers
                .Any(id => string.Equals(id.IdentifierValue, msg.ExternalId, StringComparison.OrdinalIgnoreCase));

            if (!alreadyHasId)
            {
                matchedEntity.Identifiers.Add(new EntityIdentifier
                {
                    EntityId = matchedEntity.Id,
                    IdentifierType = identifierType,
                    IdentifierValue = msg.ExternalId,
                    Source = msg.SourceType,
                });

                // Publish a CorrelationLink alert — a new data source has been linked to this entity
                var linkAlert = new Alert
                {
                    Type = AlertType.CorrelationLink,
                    Severity = AlertSeverity.Low,
                    EntityId = matchedEntity.Id,
                    Summary = $"New identifier {msg.ExternalId} ({msg.SourceType}) linked to entity {matchedEntity.DisplayName ?? matchedEntity.Id.ToString()}",
                    Details = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        message = $"Correlated via rule '{bestRuleId}' with confidence {bestConfidence:F2}",
                        externalId = msg.ExternalId,
                        source = msg.SourceType,
                        ruleId = bestRuleId,
                        confidence = bestConfidence
                    }),
                    Status = AlertStatus.Triggered,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                await _alertRepo.AddAsync(linkAlert, ct);

                var linkAlertMsg = new AlertTriggeredMessage(
                    AlertId: linkAlert.Id,
                    Type: linkAlert.Type.ToString(),
                    Severity: linkAlert.Severity.ToString(),
                    EntityId: linkAlert.EntityId,
                    Summary: linkAlert.Summary,
                    CreatedAt: linkAlert.CreatedAt);

                await _db.Multiplexer.GetSubscriber().PublishAsync(
                    RedisChannel.Literal("alerts:triggered"),
                    System.Text.Json.JsonSerializer.Serialize(linkAlertMsg));

                _logger.LogInformation(
                    "CorrelationLink alert {AlertId} published for entity {EntityId} new identifier {ExternalId}",
                    linkAlert.Id, matchedEntity.Id, msg.ExternalId);
            }

            // Use raw SQL to avoid concurrency exceptions from tracked entity races
            await _entityRepo.UpdatePositionAsync(matchedEntity.Id, position, msg.SpeedMps, msg.Heading, msg.ObservedAt, ct);
            await _db.StringSetAsync(cacheKey, matchedEntity.Id.ToString(), CacheTtl, When.Always, CommandFlags.None);
            await LinkObservationToEntityAsync(msg.ObservationId, matchedEntity.Id, msg.ObservedAt, ct);

            _logger.LogInformation(
                "Correlated {Source}:{ExternalId} → entity {EntityId} via {Rule} (confidence {Confidence:F2})",
                msg.SourceType, msg.ExternalId, matchedEntity.Id, bestRuleId, bestConfidence);

            await CheckEmergencyAsync(msg, matchedEntity.Id, ct);

            return new EntityUpdatedMessage(matchedEntity.Id, msg.Longitude, msg.Latitude, msg.Heading, msg.SpeedMps,
                entityType.ToString(), EntityStatus.Active.ToString(), msg.ObservedAt, msg.DisplayName,
                msg.VesselType, msg.AircraftType, msg.Emergency, msg.IsMilitary);
        }

        if (matchedEntity != null && bestConfidence >= ReviewThreshold && _systemDb != null)
        {
            // Mid-confidence match — create review for analyst and still create new entity
            var newEntity = new TrackedEntity
            {
                Type = entityType,
                DisplayName = msg.DisplayName,
                LastKnownPosition = position,
                LastSpeedMps = msg.SpeedMps,
                LastHeading = msg.Heading,
                LastSeen = msg.ObservedAt,
                Status = EntityStatus.Active,
            };

            var newIdentifierTypeReview = msg.SourceType == "ADSB" ? "ICAO" : "MMSI";
            newEntity.Identifiers.Add(new EntityIdentifier
            {
                EntityId = newEntity.Id,
                IdentifierType = newIdentifierTypeReview,
                IdentifierValue = msg.ExternalId,
                Source = msg.SourceType,
            });

            await _entityRepo.AddAsync(newEntity, ct);
            await _db.StringSetAsync(cacheKey, newEntity.Id.ToString(), CacheTtl, When.Always, CommandFlags.None);
            await LinkObservationToEntityAsync(msg.ObservationId, newEntity.Id, msg.ObservedAt, ct);

            var review = new CorrelationReview
            {
                SourceEntityId = newEntity.Id,
                TargetEntityId = matchedEntity.Id,
                Confidence = bestConfidence,
                RuleScores = JsonSerializer.Serialize(allScores),
            };
            _systemDb.CorrelationReviews.Add(review);
            await _systemDb.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Created review {ReviewId} for potential correlation: {Source}:{ExternalId} → entity {EntityId} (confidence {Confidence:F2})",
                review.Id, msg.SourceType, msg.ExternalId, matchedEntity.Id, bestConfidence);

            await CheckEmergencyAsync(msg, newEntity.Id, ct);

            return new EntityUpdatedMessage(newEntity.Id, msg.Longitude, msg.Latitude, msg.Heading, msg.SpeedMps,
                entityType.ToString(), EntityStatus.Active.ToString(), msg.ObservedAt, msg.DisplayName,
                msg.VesselType, msg.AircraftType, msg.Emergency, msg.IsMilitary);
        }

        // No match found — create new entity
        var entity = new TrackedEntity
        {
            Type = entityType,
            DisplayName = msg.DisplayName,
            LastKnownPosition = position,
            LastSpeedMps = msg.SpeedMps,
            LastHeading = msg.Heading,
            LastSeen = msg.ObservedAt,
            Status = EntityStatus.Active,
        };

        // Add identifier linking external ID to entity
        var newIdentifierType = msg.SourceType == "ADSB" ? "ICAO" : "MMSI";
        entity.Identifiers.Add(new EntityIdentifier
        {
            EntityId = entity.Id,
            IdentifierType = newIdentifierType,
            IdentifierValue = msg.ExternalId,
            Source = msg.SourceType,
        });

        await _entityRepo.AddAsync(entity, ct);

        // Cache the link for future observations
        await _db.StringSetAsync(cacheKey, entity.Id.ToString(), CacheTtl, When.Always, CommandFlags.None);

        // Link observation to entity for historical track queries
        await LinkObservationToEntityAsync(msg.ObservationId, entity.Id, msg.ObservedAt, ct);

        _logger.LogInformation("Created entity {EntityId} for {Source}:{ExternalId}", entity.Id, msg.SourceType, msg.ExternalId);

        await CheckEmergencyAsync(msg, entity.Id, ct);

        return new EntityUpdatedMessage(entity.Id, msg.Longitude, msg.Latitude, msg.Heading, msg.SpeedMps,
            entityType.ToString(), EntityStatus.Active.ToString(), msg.ObservedAt, msg.DisplayName,
            msg.VesselType, msg.AircraftType, msg.Emergency, msg.IsMilitary);
    }

    /// <summary>
    /// Creates an EmergencySquawk alert if the observation carries a non-"none" emergency status.
    /// Uses a Redis debounce key to avoid duplicate alerts for the same aircraft.
    /// </summary>
    private async Task CheckEmergencyAsync(ObservationPublishedMessage msg, Guid entityId, CancellationToken ct)
    {
        if (msg.SourceType != "ADSB") return;
        if (string.IsNullOrEmpty(msg.Emergency) || string.Equals(msg.Emergency, "none", StringComparison.OrdinalIgnoreCase))
            return;

        // Debounce: one alert per aircraft per 10 minutes
        var debounceKey = $"alert:emergency:debounce:{msg.ExternalId}";
        var alreadyAlerted = await _db.KeyExistsAsync(debounceKey);
        if (alreadyAlerted) return;

        await _db.StringSetAsync(debounceKey, "1", TimeSpan.FromMinutes(10));

        var alert = new Alert
        {
            Type = AlertType.EmergencySquawk,
            Severity = AlertSeverity.Critical,
            EntityId = entityId,
            Summary = $"Aircraft {msg.DisplayName ?? msg.ExternalId} emergency: {msg.Emergency}",
            Details = JsonSerializer.Serialize(new
            {
                message = $"Emergency squawk detected for aircraft {msg.DisplayName ?? msg.ExternalId} (ICAO: {msg.ExternalId})",
                emergency = msg.Emergency,
                externalId = msg.ExternalId,
                latitude = msg.Latitude,
                longitude = msg.Longitude,
            }),
            Status = AlertStatus.Triggered,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await _alertRepo.AddAsync(alert, ct);

        var alertMsg = new AlertTriggeredMessage(
            AlertId: alert.Id,
            Type: alert.Type.ToString(),
            Severity: alert.Severity.ToString(),
            EntityId: alert.EntityId,
            Summary: alert.Summary,
            CreatedAt: alert.CreatedAt);

        await _db.Multiplexer.GetSubscriber().PublishAsync(
            RedisChannel.Literal("alerts:triggered"),
            JsonSerializer.Serialize(alertMsg));

        _logger.LogWarning(
            "EmergencySquawk alert {AlertId} for entity {EntityId} ({DisplayName}): {Emergency}",
            alert.Id, entityId, msg.DisplayName ?? msg.ExternalId, msg.Emergency);
    }

    private async Task LinkObservationToEntityAsync(long observationId, Guid entityId, DateTimeOffset observedAt, CancellationToken ct)
    {
        if (_systemDb == null) return;
        try
        {
            // Use a date range for partition routing (exact timestamp may have microsecond drift)
            var dayStart = observedAt.UtcDateTime.Date;
            var dayEnd = dayStart.AddDays(1);
            object[] parameters = [entityId, observationId, DateTime.SpecifyKind(dayStart, DateTimeKind.Utc), DateTime.SpecifyKind(dayEnd, DateTimeKind.Utc)];
            await _systemDb.Database.ExecuteSqlRawAsync(
                "UPDATE observations SET entity_id = {0} WHERE id = {1} AND observed_at >= {2} AND observed_at < {3}",
                parameters,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to link observation {ObsId} to entity {EntityId}", observationId, entityId);
        }
    }
}

/// <summary>
/// BackgroundService that subscribes to Redis "observations:*" and runs CorrelationProcessor per message.
/// Publishes EntityUpdatedMessage to "entities:updated" for consumption by TrackHubService.
/// </summary>
public class CorrelationWorker : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CorrelationWorker> _logger;

    public CorrelationWorker(IConnectionMultiplexer redis, IServiceScopeFactory scopeFactory, ILogger<CorrelationWorker> logger)
    {
        _redis = redis;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = _redis.GetSubscriber();
        var channel = RedisChannel.Pattern("observations:*");

        await subscriber.SubscribeAsync(channel, async (_, message) =>
        {
            if (message.IsNull) return;

            ObservationPublishedMessage? msg;
            try { msg = JsonSerializer.Deserialize<ObservationPublishedMessage>(message!); }
            catch { return; }
            if (msg is null) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var entityRepo = scope.ServiceProvider.GetRequiredService<IEntityRepository>();
                var db = _redis.GetDatabase();
                var correlationRules = scope.ServiceProvider.GetServices<ICorrelationRule>();
                var alertRepo = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
                var systemDb = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
                var processor = new CorrelationProcessor(entityRepo, db, correlationRules, alertRepo,
                    scope.ServiceProvider.GetRequiredService<ILogger<CorrelationProcessor>>(), systemDb);

                var entityUpdate = await processor.ProcessAsync(msg, stoppingToken);
                if (entityUpdate is null) return;

                // Publish entity update for TrackHubService
                var json = JsonSerializer.Serialize(entityUpdate);
                await _redis.GetSubscriber().PublishAsync(RedisChannel.Literal("entities:updated"), json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing observation for {Source}:{ExternalId}", msg.SourceType, msg.ExternalId);
            }
        });

        _logger.LogInformation("CorrelationWorker subscribed to observations:*");
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
