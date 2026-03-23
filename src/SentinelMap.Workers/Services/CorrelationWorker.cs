using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;
using SentinelMap.Domain.Messages;
using SentinelMap.Infrastructure.Correlation;
using SentinelMap.SharedKernel.Enums;
using StackExchange.Redis;

namespace SentinelMap.Workers.Services;

/// <summary>
/// Extracted processing logic — testable without a BackgroundService host.
/// </summary>
public class CorrelationProcessor
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(7);
    private const double MinConfidenceThreshold = 0.6;

    private readonly IEntityRepository _entityRepo;
    private readonly IDatabase _db;
    private readonly IEnumerable<ICorrelationRule> _correlationRules;
    private readonly ILogger<CorrelationProcessor> _logger;

    public CorrelationProcessor(
        IEntityRepository entityRepo,
        IDatabase db,
        IEnumerable<ICorrelationRule> correlationRules,
        ILogger<CorrelationProcessor> logger)
    {
        _entityRepo = entityRepo;
        _db = db;
        _correlationRules = correlationRules;
        _logger = logger;
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

            _logger.LogDebug("Hot-path hit for {Source}:{ExternalId} → entity {EntityId}", msg.SourceType, msg.ExternalId, entityId);

            return new EntityUpdatedMessage(entityId, msg.Longitude, msg.Latitude, msg.Heading, msg.SpeedMps,
                entityType.ToString(), EntityStatus.Active.ToString(), msg.ObservedAt, msg.DisplayName,
                msg.VesselType, msg.AircraftType);
        }

        // Cold path: check for existing entity match via correlation rules
        var searchRadius = Math.Max(5000, (msg.SpeedMps ?? 5) * 900 * 1.2); // 15min window
        var candidates = await _entityRepo.FindCandidatesAsync(position, searchRadius, TimeSpan.FromHours(24), ct);

        TrackedEntity? matchedEntity = null;
        double bestConfidence = 0;
        string? bestRuleId = null;

        foreach (var candidate in candidates)
        {
            foreach (var rule in _correlationRules)
            {
                var score = await rule.EvaluateAsync(msg.SourceType, msg.ExternalId, msg.DisplayName, position, candidate, ct);
                if (score != null && score.Confidence > bestConfidence)
                {
                    bestConfidence = score.Confidence;
                    matchedEntity = candidate;
                    bestRuleId = score.RuleId;
                }
            }
        }

        if (matchedEntity != null && bestConfidence > MinConfidenceThreshold)
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
            }

            matchedEntity.LastKnownPosition = position;
            matchedEntity.LastSpeedMps = msg.SpeedMps;
            matchedEntity.LastHeading = msg.Heading;
            matchedEntity.LastSeen = msg.ObservedAt;
            matchedEntity.UpdatedAt = DateTimeOffset.UtcNow;

            await _entityRepo.UpdateAsync(matchedEntity, ct);
            await _db.StringSetAsync(cacheKey, matchedEntity.Id.ToString(), CacheTtl, When.Always, CommandFlags.None);

            _logger.LogInformation(
                "Correlated {Source}:{ExternalId} → entity {EntityId} via {Rule} (confidence {Confidence:F2})",
                msg.SourceType, msg.ExternalId, matchedEntity.Id, bestRuleId, bestConfidence);

            return new EntityUpdatedMessage(matchedEntity.Id, msg.Longitude, msg.Latitude, msg.Heading, msg.SpeedMps,
                entityType.ToString(), EntityStatus.Active.ToString(), msg.ObservedAt, msg.DisplayName,
                msg.VesselType, msg.AircraftType);
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

        _logger.LogInformation("Created entity {EntityId} for {Source}:{ExternalId}", entity.Id, msg.SourceType, msg.ExternalId);

        return new EntityUpdatedMessage(entity.Id, msg.Longitude, msg.Latitude, msg.Heading, msg.SpeedMps,
            entityType.ToString(), EntityStatus.Active.ToString(), msg.ObservedAt, msg.DisplayName,
            msg.VesselType, msg.AircraftType);
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
                var processor = new CorrelationProcessor(entityRepo, db, correlationRules,
                    scope.ServiceProvider.GetRequiredService<ILogger<CorrelationProcessor>>());

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
