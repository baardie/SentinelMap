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
/// Evaluates alert rules against entity updates received via Redis.
/// Also runs an AIS Dark timer that marks vessels as Dark when they go silent.
/// </summary>
public class AlertingWorker : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AlertingWorker> _logger;

    public AlertingWorker(
        IConnectionMultiplexer redis,
        IServiceScopeFactory scopeFactory,
        ILogger<AlertingWorker> logger)
    {
        _redis = redis;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var entityUpdateTask = RunEntityUpdateHandlerAsync(stoppingToken);
        var aisDarkTask = RunAisDarkTimerAsync(stoppingToken);

        await Task.WhenAll(entityUpdateTask, aisDarkTask);
    }

    // -------------------------------------------------------------------------
    // Entity update handler — subscribes to Redis "entities:updated"
    // -------------------------------------------------------------------------

    private async Task RunEntityUpdateHandlerAsync(CancellationToken stoppingToken)
    {
        var subscriber = _redis.GetSubscriber();
        var channel = RedisChannel.Literal("entities:updated");

        await subscriber.SubscribeAsync(channel, async (_, message) =>
        {
            if (message.IsNull) return;

            EntityUpdatedMessage? msg;
            try { msg = JsonSerializer.Deserialize<EntityUpdatedMessage>(message!); }
            catch { return; }
            if (msg is null) return;

            try
            {
                await EvaluateRulesForEntityAsync(msg, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error evaluating alert rules for entity {EntityId}", msg.EntityId);
            }
        });

        _logger.LogInformation("AlertingWorker subscribed to entities:updated");
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task EvaluateRulesForEntityAsync(EntityUpdatedMessage msg, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var entityRepo = scope.ServiceProvider.GetRequiredService<IEntityRepository>();
        var alertRepo = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
        var rules = scope.ServiceProvider.GetRequiredService<IEnumerable<IAlertRule>>();

        var entity = await entityRepo.GetByIdAsync(msg.EntityId, ct);
        if (entity is null)
        {
            _logger.LogWarning("AlertingWorker: entity {EntityId} not found, skipping rule evaluation", msg.EntityId);
            return;
        }

        // Patch entity with latest message values in case the DB is momentarily stale
        if (entity.LastKnownPosition is null || entity.LastSeen < msg.Timestamp)
        {
            entity.LastKnownPosition = new Point(msg.Longitude, msg.Latitude) { SRID = 4326 };
            entity.LastSpeedMps = msg.Speed;
            entity.LastHeading = msg.Heading;
            entity.LastSeen = msg.Timestamp;
        }

        foreach (var rule in rules)
        {
            IReadOnlyList<AlertTrigger> triggers;
            try { triggers = await rule.EvaluateAsync(entity, ct); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rule {RuleId} threw during evaluation for entity {EntityId}", rule.RuleId, entity.Id);
                continue;
            }

            foreach (var trigger in triggers)
            {
                await PersistAndPublishAlertAsync(alertRepo, trigger, entity.Id, ct);
            }
        }
    }

    // -------------------------------------------------------------------------
    // AIS Dark timer — runs every 60 s after a 2-minute startup grace period
    // -------------------------------------------------------------------------

    private async Task RunAisDarkTimerAsync(CancellationToken stoppingToken)
    {
        // Grace period: wait 2 minutes before the first dark check
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
        catch (OperationCanceledException) { return; }

        _logger.LogInformation("AlertingWorker AIS Dark timer started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAisDarkAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during AIS Dark check");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task CheckAisDarkAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var entityRepo = scope.ServiceProvider.GetRequiredService<IEntityRepository>();
        var alertRepo = scope.ServiceProvider.GetRequiredService<IAlertRepository>();

        var darkTimeoutSeconds = int.TryParse(
            Environment.GetEnvironmentVariable("AIS_DARK_TIMEOUT_SECONDS"), out var parsed)
            ? parsed
            : 900;

        var darkTimeout = TimeSpan.FromSeconds(darkTimeoutSeconds);
        var staleVessels = await entityRepo.FindStaleVesselsAsync(darkTimeout, ct);

        if (staleVessels.Count > 0)
            _logger.LogInformation("AIS Dark check: {Count} vessel(s) going dark", staleVessels.Count);

        var publisher = _redis.GetSubscriber();

        foreach (var vessel in staleVessels)
        {
            // Mark vessel as Dark
            vessel.Status = EntityStatus.Dark;
            vessel.UpdatedAt = DateTimeOffset.UtcNow;
            await entityRepo.UpdateAsync(vessel, ct);

            // Create and persist AIS Dark alert
            var trigger = new AlertTrigger(
                Type: AlertType.AisDark,
                Severity: AlertSeverity.High,
                Summary: $"Vessel {vessel.DisplayName} has gone AIS dark",
                Details: $"Vessel {vessel.Id} last seen {vessel.LastSeen:u}, exceeded dark timeout of {darkTimeoutSeconds}s");

            var alert = await PersistAndPublishAlertAsync(alertRepo, trigger, vessel.Id, ct);

            // Publish entity update so the UI reflects the status change
            var entityUpdate = new EntityUpdatedMessage(
                EntityId: vessel.Id,
                Longitude: vessel.LastKnownPosition?.X ?? 0,
                Latitude: vessel.LastKnownPosition?.Y ?? 0,
                Heading: vessel.LastHeading,
                Speed: vessel.LastSpeedMps,
                EntityType: EntityType.Vessel.ToString(),
                Status: EntityStatus.Dark.ToString(),
                Timestamp: vessel.LastSeen ?? DateTimeOffset.UtcNow,
                DisplayName: vessel.DisplayName);

            var entityJson = JsonSerializer.Serialize(entityUpdate);
            await publisher.PublishAsync(RedisChannel.Literal("entities:updated"), entityJson);

            _logger.LogInformation("Vessel {EntityId} ({DisplayName}) marked AIS Dark", vessel.Id, vessel.DisplayName);
        }
    }

    // -------------------------------------------------------------------------
    // Shared helper — persist Alert and publish AlertTriggeredMessage
    // -------------------------------------------------------------------------

    private async Task<Alert> PersistAndPublishAlertAsync(
        IAlertRepository alertRepo,
        AlertTrigger trigger,
        Guid entityId,
        CancellationToken ct)
    {
        var alert = new Alert
        {
            Type = trigger.Type,
            Severity = trigger.Severity,
            EntityId = entityId,
            GeofenceId = trigger.GeofenceId,
            Summary = trigger.Summary,
            Details = trigger.Details,
            Status = AlertStatus.Triggered,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await alertRepo.AddAsync(alert, ct);

        var alertMsg = new AlertTriggeredMessage(
            AlertId: alert.Id,
            Type: alert.Type.ToString(),
            Severity: alert.Severity.ToString(),
            EntityId: alert.EntityId,
            Summary: alert.Summary,
            CreatedAt: alert.CreatedAt);

        var json = JsonSerializer.Serialize(alertMsg);
        await _redis.GetSubscriber().PublishAsync(RedisChannel.Literal("alerts:triggered"), json);

        _logger.LogInformation("Alert {AlertId} ({Type}/{Severity}) triggered for entity {EntityId}: {Summary}",
            alert.Id, alert.Type, alert.Severity, entityId, alert.Summary);

        return alert;
    }
}
