using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using SentinelMap.Domain.Messages;
using StackExchange.Redis;

namespace SentinelMap.Api.Hubs;

/// <summary>
/// Subscribes to Redis "entities:updated" channel.
/// Broadcasts TrackUpdate events to all connected SignalR clients.
/// </summary>
public class TrackHubService : BackgroundService
{
    private readonly IHubContext<TrackHub> _hub;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<TrackHubService> _logger;

    public TrackHubService(IHubContext<TrackHub> hub, IConnectionMultiplexer redis, ILogger<TrackHubService> logger)
    {
        _hub = hub;
        _redis = redis;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = _redis.GetSubscriber();

        await subscriber.SubscribeAsync(RedisChannel.Literal("entities:updated"), async (_, message) =>
        {
            if (message.IsNull) return;

            EntityUpdatedMessage? evt;
            try { evt = JsonSerializer.Deserialize<EntityUpdatedMessage>(message!); }
            catch { return; }
            if (evt is null) return;

            var trackUpdate = new
            {
                entityId = evt.EntityId,
                position = new[] { evt.Longitude, evt.Latitude },
                heading = evt.Heading,
                speed = evt.Speed,
                entityType = evt.EntityType,
                status = evt.Status,
                timestamp = evt.Timestamp,
                displayName = evt.DisplayName,
                vesselType = evt.VesselType,
                aircraftType = evt.AircraftType,
                emergency = evt.Emergency,
                isMilitary = evt.IsMilitary,
            };

            try
            {
                await _hub.Clients.All.SendAsync("TrackUpdate", trackUpdate, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast TrackUpdate to SignalR clients");
            }
        });

        _logger.LogInformation("TrackHubService subscribed to entities:updated");
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
