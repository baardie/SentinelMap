using FluentAssertions;
using Moq;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;
using SentinelMap.Domain.Messages;
using SentinelMap.Infrastructure.Correlation;
using SentinelMap.SharedKernel.Enums;
using SentinelMap.Workers.Services;
using StackExchange.Redis;

namespace SentinelMap.Workers.Tests.Services;

public class CorrelationWorkerTests
{
    private readonly Mock<IEntityRepository> _entityRepo = new();
    private readonly Mock<IDatabase> _redisDb = new();
    private readonly IEnumerable<ICorrelationRule> _noRules = [];

    public CorrelationWorkerTests()
    {
        // Default: FindCandidatesAsync returns empty so cold path creates new entities
        _entityRepo.Setup(r => r.FindCandidatesAsync(
                It.IsAny<Point>(), It.IsAny<double>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TrackedEntity>());
    }

    [Fact]
    public async Task CacheHit_UpdatesEntityPositionWithoutCreatingNew()
    {
        var entityId = Guid.NewGuid();
        _redisDb.Setup(db => db.StringGetAsync("correlation:link:AIS:235009888", It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)entityId.ToString());

        var processor = new CorrelationProcessor(_entityRepo.Object, _redisDb.Object, _noRules,
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

        var processor = new CorrelationProcessor(_entityRepo.Object, _redisDb.Object, _noRules,
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

    [Fact]
    public async Task AdsbObservation_CreatesAircraftEntity()
    {
        _redisDb.Setup(db => db.StringGetAsync("correlation:link:ADSB:A1B2C3", It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        TrackedEntity? createdEntity = null;
        _entityRepo.Setup(r => r.AddAsync(It.IsAny<TrackedEntity>(), It.IsAny<CancellationToken>()))
            .Callback<TrackedEntity, CancellationToken>((e, _) => createdEntity = e)
            .ReturnsAsync((TrackedEntity e, CancellationToken _) => e);

        var processor = new CorrelationProcessor(_entityRepo.Object, _redisDb.Object, _noRules,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CorrelationProcessor>.Instance);

        var msg = new ObservationPublishedMessage(3, DateTimeOffset.UtcNow, "ADSB", "A1B2C3", -0.5, 51.5, 270.0, 250.0);

        var result = await processor.ProcessAsync(msg);

        createdEntity.Should().NotBeNull();
        createdEntity!.Type.Should().Be(EntityType.Aircraft);
        result.Should().NotBeNull();
        result!.EntityType.Should().Be(EntityType.Aircraft.ToString());
    }

    [Fact]
    public async Task DisplayName_FlowsThroughPipeline()
    {
        _redisDb.Setup(db => db.StringGetAsync("correlation:link:AIS:112233445", It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        TrackedEntity? createdEntity = null;
        _entityRepo.Setup(r => r.AddAsync(It.IsAny<TrackedEntity>(), It.IsAny<CancellationToken>()))
            .Callback<TrackedEntity, CancellationToken>((e, _) => createdEntity = e)
            .ReturnsAsync((TrackedEntity e, CancellationToken _) => e);

        var processor = new CorrelationProcessor(_entityRepo.Object, _redisDb.Object, _noRules,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CorrelationProcessor>.Instance);

        var msg = new ObservationPublishedMessage(4, DateTimeOffset.UtcNow, "AIS", "112233445", 2.0, 52.0, null, null, "MV Sentinel");

        var result = await processor.ProcessAsync(msg);

        createdEntity.Should().NotBeNull();
        createdEntity!.DisplayName.Should().Be("MV Sentinel");
        result.Should().NotBeNull();
        result!.DisplayName.Should().Be("MV Sentinel");
    }

    [Fact]
    public async Task CacheHit_DisplayName_FlowsThroughPipeline()
    {
        var entityId = Guid.NewGuid();
        _redisDb.Setup(db => db.StringGetAsync("correlation:link:AIS:777888999", It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)entityId.ToString());

        var processor = new CorrelationProcessor(_entityRepo.Object, _redisDb.Object, _noRules,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CorrelationProcessor>.Instance);

        var msg = new ObservationPublishedMessage(5, DateTimeOffset.UtcNow, "AIS", "777888999", -1.0, 50.0, 180.0, 5.0, "HMS Example");

        var result = await processor.ProcessAsync(msg);

        result.Should().NotBeNull();
        result!.DisplayName.Should().Be("HMS Example");
    }
}
