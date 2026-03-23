using System.Text.Json;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;
using SentinelMap.Domain.Messages;
using StackExchange.Redis;

namespace SentinelMap.Infrastructure.Pipeline;

public class RedisObservationPublisher : IObservationPublisher
{
    private readonly ISubscriber _subscriber;

    public RedisObservationPublisher(IConnectionMultiplexer redis)
    {
        _subscriber = redis.GetSubscriber();
    }

    public async Task PublishAsync(Observation observation, CancellationToken ct = default)
    {
        string? displayName = null;
        string? vesselType = null;
        string? aircraftType = null;
        if (!string.IsNullOrEmpty(observation.RawData))
        {
            try
            {
                var rawNode = System.Text.Json.Nodes.JsonNode.Parse(observation.RawData);
                displayName = rawNode?["displayName"]?.GetValue<string>();
                vesselType = rawNode?["vesselType"]?.GetValue<string>();
                aircraftType = rawNode?["aircraftType"]?.GetValue<string>();
            }
            catch { }
        }

        var message = new ObservationPublishedMessage(
            ObservationId: observation.Id,
            ObservedAt: observation.ObservedAt,
            SourceType: observation.SourceType,
            ExternalId: observation.ExternalId,
            Longitude: observation.Position?.X ?? 0,
            Latitude: observation.Position?.Y ?? 0,
            Heading: observation.Heading,
            SpeedMps: observation.SpeedMps,
            DisplayName: displayName,
            VesselType: vesselType,
            AircraftType: aircraftType);

        var json = JsonSerializer.Serialize(message);
        var channel = RedisChannel.Literal($"observations:{observation.SourceType}");
        await _subscriber.PublishAsync(channel, json);
    }
}
