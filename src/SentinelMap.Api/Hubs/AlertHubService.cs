using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using SentinelMap.Domain.Messages;
using StackExchange.Redis;

namespace SentinelMap.Api.Hubs;

/// <summary>
/// Subscribes to Redis "alerts:triggered" channel.
/// Broadcasts AlertTriggered events to all connected SignalR clients.
/// </summary>
public class AlertHubService : BackgroundService
{
    private readonly IHubContext<TrackHub> _hub;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<AlertHubService> _logger;

    public AlertHubService(IHubContext<TrackHub> hub, IConnectionMultiplexer redis, ILogger<AlertHubService> logger)
    {
        _hub = hub;
        _redis = redis;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = _redis.GetSubscriber();

        await subscriber.SubscribeAsync(RedisChannel.Literal("alerts:triggered"), async (_, message) =>
        {
            if (message.IsNull) return;

            AlertTriggeredMessage? evt;
            try { evt = JsonSerializer.Deserialize<AlertTriggeredMessage>(message!); }
            catch { return; }
            if (evt is null) return;

            var alertPayload = new
            {
                alertId = evt.AlertId,
                type = evt.Type,
                severity = evt.Severity,
                entityId = evt.EntityId,
                summary = evt.Summary,
                createdAt = evt.CreatedAt
            };

            try
            {
                await _hub.Clients.All.SendAsync("AlertTriggered", alertPayload, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast AlertTriggered to SignalR clients");
            }
        });

        _logger.LogInformation("AlertHubService subscribed to alerts:triggered");
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
