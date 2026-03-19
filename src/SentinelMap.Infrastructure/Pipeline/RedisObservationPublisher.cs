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
        var message = new ObservationPublishedMessage(
            ObservationId: observation.Id,
            ObservedAt: observation.ObservedAt,
            SourceType: observation.SourceType,
            ExternalId: observation.ExternalId,
            Longitude: observation.Position?.X ?? 0,
            Latitude: observation.Position?.Y ?? 0,
            Heading: observation.Heading,
            SpeedMps: observation.SpeedMps);

        var json = JsonSerializer.Serialize(message);
        var channel = RedisChannel.Literal($"observations:{observation.SourceType}");
        await _subscriber.PublishAsync(channel, json);
    }
}
