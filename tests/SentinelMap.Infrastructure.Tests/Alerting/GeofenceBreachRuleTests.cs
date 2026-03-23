using FluentAssertions;
using Moq;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;
using SentinelMap.Infrastructure.Alerting;
using SentinelMap.SharedKernel.Enums;
using StackExchange.Redis;

namespace SentinelMap.Infrastructure.Tests.Alerting;

public class GeofenceBreachRuleTests
{
    private readonly Mock<IGeofenceRepository> _geofenceRepo = new();
    private readonly Mock<IDatabase> _redisDb = new();
    private readonly GeofenceBreachRule _sut;

    private static readonly Point TestPosition = new(0.0, 51.0) { SRID = 4326 };

    public GeofenceBreachRuleTests()
    {
        _sut = new GeofenceBreachRule(_geofenceRepo.Object, _redisDb.Object);

        // Default: KeyDeleteAsync and SetAddAsync succeed
        _redisDb.Setup(db => db.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        _redisDb.Setup(db => db.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1L);
    }

    private static TrackedEntity MakeEntity(string displayName = "TestEntity") => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = displayName,
        Type = EntityType.Vessel,
        LastKnownPosition = TestPosition
    };

    private void SetupPreviousMembership(Guid entityId, params Guid[] geofenceIds)
    {
        var key = $"geofence:membership:{entityId}";
        var values = geofenceIds.Select(id => (RedisValue)id.ToString()).ToArray();
        _redisDb.Setup(db => db.SetMembersAsync(key, It.IsAny<CommandFlags>()))
            .ReturnsAsync(values);
    }

    private void SetupNoPreviousMembership(Guid entityId)
    {
        var key = $"geofence:membership:{entityId}";
        _redisDb.Setup(db => db.SetMembersAsync(key, It.IsAny<CommandFlags>()))
            .ReturnsAsync([]);
    }

    [Fact]
    public async Task EntityEntersGeofence_NoPreviousMembership_ReturnsEntryTrigger()
    {
        var entity = MakeEntity("Vessel Alpha");
        var geofenceId = Guid.NewGuid();

        SetupNoPreviousMembership(entity.Id);
        _geofenceRepo.Setup(r => r.FindContainingAsync(TestPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync([geofenceId]);

        var triggers = await _sut.EvaluateAsync(entity);

        triggers.Should().HaveCount(1);
        triggers[0].Type.Should().Be(AlertType.GeofenceBreach);
        triggers[0].Severity.Should().Be(AlertSeverity.High);
        triggers[0].Summary.Should().Contain("entered geofence");
        triggers[0].Summary.Should().Contain("Vessel Alpha");
        triggers[0].GeofenceId.Should().Be(geofenceId);
    }

    [Fact]
    public async Task EntityExitsGeofence_WasPreviouslyIn_ReturnsExitTrigger()
    {
        var entity = MakeEntity("Vessel Bravo");
        var geofenceId = Guid.NewGuid();

        SetupPreviousMembership(entity.Id, geofenceId);
        _geofenceRepo.Setup(r => r.FindContainingAsync(TestPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var triggers = await _sut.EvaluateAsync(entity);

        triggers.Should().HaveCount(1);
        triggers[0].Type.Should().Be(AlertType.GeofenceBreach);
        triggers[0].Severity.Should().Be(AlertSeverity.High);
        triggers[0].Summary.Should().Contain("exited geofence");
        triggers[0].Summary.Should().Contain("Vessel Bravo");
        triggers[0].GeofenceId.Should().Be(geofenceId);
    }

    [Fact]
    public async Task EntityStaysInSameGeofence_ReturnsEmptyList()
    {
        var entity = MakeEntity();
        var geofenceId = Guid.NewGuid();

        SetupPreviousMembership(entity.Id, geofenceId);
        _geofenceRepo.Setup(r => r.FindContainingAsync(TestPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync([geofenceId]);

        var triggers = await _sut.EvaluateAsync(entity);

        triggers.Should().BeEmpty();
    }

    [Fact]
    public async Task EntityWithNoPosition_ReturnsEmptyList()
    {
        var entity = new TrackedEntity
        {
            Id = Guid.NewGuid(),
            DisplayName = "Vessel Charlie",
            Type = EntityType.Vessel,
            LastKnownPosition = null
        };

        var triggers = await _sut.EvaluateAsync(entity);

        triggers.Should().BeEmpty();
        _geofenceRepo.Verify(r => r.FindContainingAsync(It.IsAny<Point>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MultipleTransitions_EnterOneExitAnother_ReturnsBothTriggers()
    {
        var entity = MakeEntity("Vessel Delta");
        var enteringGeofenceId = Guid.NewGuid();
        var exitingGeofenceId = Guid.NewGuid();

        SetupPreviousMembership(entity.Id, exitingGeofenceId);
        _geofenceRepo.Setup(r => r.FindContainingAsync(TestPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync([enteringGeofenceId]);

        var triggers = await _sut.EvaluateAsync(entity);

        triggers.Should().HaveCount(2);
        triggers.Should().ContainSingle(t => t.Summary.Contains("entered geofence") && t.GeofenceId == enteringGeofenceId);
        triggers.Should().ContainSingle(t => t.Summary.Contains("exited geofence") && t.GeofenceId == exitingGeofenceId);
    }

    [Fact]
    public async Task AfterEvaluation_RedisIsUpdatedWithCurrentMembership()
    {
        var entity = MakeEntity();
        var geofenceId = Guid.NewGuid();

        SetupNoPreviousMembership(entity.Id);
        _geofenceRepo.Setup(r => r.FindContainingAsync(TestPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync([geofenceId]);

        await _sut.EvaluateAsync(entity);

        var expectedKey = $"geofence:membership:{entity.Id}";
        _redisDb.Verify(db => db.KeyDeleteAsync(expectedKey, It.IsAny<CommandFlags>()), Times.Once);
        _redisDb.Verify(db => db.SetAddAsync(
            expectedKey,
            It.Is<RedisValue[]>(v => v.Length == 1 && v[0].ToString() == geofenceId.ToString()),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task AfterEvaluation_WhenNoCurrentMembership_KeyIsDeletedButNotRecreated()
    {
        var entity = MakeEntity();

        SetupNoPreviousMembership(entity.Id);
        _geofenceRepo.Setup(r => r.FindContainingAsync(TestPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await _sut.EvaluateAsync(entity);

        var expectedKey = $"geofence:membership:{entity.Id}";
        _redisDb.Verify(db => db.KeyDeleteAsync(expectedKey, It.IsAny<CommandFlags>()), Times.Once);
        _redisDb.Verify(db => db.SetAddAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()), Times.Never);
    }
}
