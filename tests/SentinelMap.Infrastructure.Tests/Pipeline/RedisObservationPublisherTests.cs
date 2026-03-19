using FluentAssertions;
using Moq;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Infrastructure.Pipeline;
using StackExchange.Redis;

namespace SentinelMap.Infrastructure.Tests.Pipeline;

public class RedisObservationPublisherTests
{
    [Fact]
    public async Task Publish_SendsToCorrectChannel()
    {
        var mockSubscriber = new Mock<ISubscriber>();
        var mockMultiplexer = new Mock<IConnectionMultiplexer>();
        mockMultiplexer.Setup(m => m.GetSubscriber(It.IsAny<object>())).Returns(mockSubscriber.Object);

        var publisher = new RedisObservationPublisher(mockMultiplexer.Object);

        var observation = new Observation
        {
            Id = 1,
            ObservedAt = DateTimeOffset.UtcNow,
            SourceType = "AIS",
            ExternalId = "235009888",
            Position = new Point(-1.5, 51.0) { SRID = 4326 },
            Heading = 90.0,
            SpeedMps = 6.0,
        };

        await publisher.PublishAsync(observation);

        mockSubscriber.Verify(
            s => s.PublishAsync(
                It.Is<RedisChannel>(c => c.ToString() == "observations:AIS"),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task Publish_MessageContainsExternalId()
    {
        RedisValue capturedMessage = default;
        var mockSubscriber = new Mock<ISubscriber>();
        mockSubscriber
            .Setup(s => s.PublishAsync(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((_, msg, _) => capturedMessage = msg)
            .ReturnsAsync(1L);

        var mockMultiplexer = new Mock<IConnectionMultiplexer>();
        mockMultiplexer.Setup(m => m.GetSubscriber(It.IsAny<object>())).Returns(mockSubscriber.Object);

        var publisher = new RedisObservationPublisher(mockMultiplexer.Object);
        var observation = new Observation
        {
            Id = 42,
            ObservedAt = DateTimeOffset.UtcNow,
            SourceType = "AIS",
            ExternalId = "235009888",
            Position = new Point(-1.5, 51.0) { SRID = 4326 },
        };

        await publisher.PublishAsync(observation);

        capturedMessage.ToString().Should().Contain("235009888");
        capturedMessage.ToString().Should().Contain("42");
    }
}
