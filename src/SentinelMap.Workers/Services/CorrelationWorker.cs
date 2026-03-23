using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;
using SentinelMap.Domain.Messages;
using SentinelMap.SharedKernel.Enums;
using StackExchange.Redis;

namespace SentinelMap.Workers.Services;

/// <summary>
/// Extracted processing logic — testable without a BackgroundService host.
/// </summary>
public class CorrelationProcessor
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(7);
    private readonly IEntityRepository _entityRepo;
    private readonly IDatabase _db;
    private readonly ILogger<CorrelationProcessor> _logger;

    public CorrelationProcessor(IEntityRepository entityRepo, IDatabase db, ILogger<CorrelationProcessor> logger)
    {
        _entityRepo = entityRepo;
        _db = db;
        _logger = logger;
    }

    public async Task<EntityUpdatedMessage?> ProcessAsync(ObservationPublishedMessage msg, CancellationToken ct = default)
    {
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

        // Cold path: new identifier — create entity
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
        var identifierType = msg.SourceType == "ADSB" ? "ICAO" : "MMSI";
        entity.Identifiers.Add(new EntityIdentifier
        {
            EntityId = entity.Id,
            IdentifierType = identifierType,
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
                var processor = new CorrelationProcessor(entityRepo, db,
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
