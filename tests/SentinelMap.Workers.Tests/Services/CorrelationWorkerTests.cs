using FluentAssertions;
using Moq;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;
using SentinelMap.Domain.Messages;
using SentinelMap.SharedKernel.Enums;
using SentinelMap.Workers.Services;
using StackExchange.Redis;

namespace SentinelMap.Workers.Tests.Services;

public class CorrelationWorkerTests
{
    private readonly Mock<IEntityRepository> _entityRepo = new();
    private readonly Mock<IDatabase> _redisDb = new();

    [Fact]
    public async Task CacheHit_UpdatesEntityPositionWithoutCreatingNew()
    {
        var entityId = Guid.NewGuid();
        _redisDb.Setup(db => db.StringGetAsync("correlation:link:AIS:235009888", It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)entityId.ToString());

        var processor = new CorrelationProcessor(_entityRepo.Object, _redisDb.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CorrelationProcessor>.Instance);

        var msg = new ObservationPublishedMessage(1, DateTimeOffset.UtcNow, "AIS", "235009888", -1.5, 51.0, 90.0, 6.17);

        await processor.ProcessAsync(msg);

        _entityRepo.Verify(r => r.UpdatePositionAsync(entityId, It.IsAny<Point>(), It.IsAny<double?>(), It.IsAny<double?>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        _entityRepo.Verify(r => r.AddAsync(It.IsAny<TrackedEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CacheMiss_CreatesNewEntityAndCachesLink()
    {
        _redisDb.Setup(db => db.StringGetAsync("correlation:link:AIS:999999999", It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        _entityRepo.Setup(r => r.AddAsync(It.IsAny<TrackedEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrackedEntity e, CancellationToken _) => e);

        var processor = new CorrelationProcessor(_entityRepo.Object, _redisDb.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CorrelationProcessor>.Instance);

        var msg = new ObservationPublishedMessage(2, DateTimeOffset.UtcNow, "AIS", "999999999", 1.0, 51.0, null, null);

        await processor.ProcessAsync(msg);

        _entityRepo.Verify(r => r.AddAsync(It.IsAny<TrackedEntity>(), It.IsAny<CancellationToken>()), Times.Once);
        // Match the 5-arg overload: StringSetAsync(key, value, TimeSpan?, When, CommandFlags)
        _redisDb.Verify(db => db.StringSetAsync(
            "correlation:link:AIS:999999999",
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }
}
