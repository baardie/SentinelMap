using FluentAssertions;
using SentinelMap.Domain.Entities;
using SentinelMap.Infrastructure.Alerting;
using SentinelMap.SharedKernel.Enums;

namespace SentinelMap.Infrastructure.Tests.Alerting;

public class SpeedAnomalyRuleTests
{
    private readonly SpeedAnomalyRule _sut = new();

    private static TrackedEntity MakeVessel(double? speedMps, string name = "MV Test") => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = name,
        Type = EntityType.Vessel,
        LastSpeedMps = speedMps
    };

    private static TrackedEntity MakeAircraft(double? speedMps, string name = "Flight Test") => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = name,
        Type = EntityType.Aircraft,
        LastSpeedMps = speedMps
    };

    private static TrackedEntity MakeUnknown(double? speedMps) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = "Unknown Entity",
        Type = EntityType.Unknown,
        LastSpeedMps = speedMps
    };

    [Fact]
    public async Task Vessel_At30Knots_ReturnsEmpty()
    {
        // 30 knots = ~15.4 m/s, below 50-knot threshold
        var entity = MakeVessel(15.4);

        var triggers = await _sut.EvaluateAsync(entity);

        triggers.Should().BeEmpty();
    }

    [Fact]
    public async Task Vessel_At60Knots_ReturnsMediumTrigger()
    {
        // 60 knots = ~30.9 m/s, above 50-knot threshold
        var entity = MakeVessel(30.9, "Fast Vessel");

        var triggers = await _sut.EvaluateAsync(entity);

        triggers.Should().HaveCount(1);
        triggers[0].Type.Should().Be(AlertType.SpeedAnomaly);
        triggers[0].Severity.Should().Be(AlertSeverity.Medium);
        triggers[0].Summary.Should().Contain("Fast Vessel");
        triggers[0].Summary.Should().Contain("50 knots");
        triggers[0].Summary.Should().Contain("Vessel");
    }

    [Fact]
    public async Task Aircraft_At500Knots_ReturnsEmpty()
    {
        // 500 knots = ~257 m/s, below 600-knot threshold
        var entity = MakeAircraft(257.0);

        var triggers = await _sut.EvaluateAsync(entity);

        triggers.Should().BeEmpty();
    }

    [Fact]
    public async Task Aircraft_At700Knots_ReturnsMediumTrigger()
    {
        // 700 knots = ~360 m/s, above 600-knot threshold
        var entity = MakeAircraft(360.0, "Fast Aircraft");

        var triggers = await _sut.EvaluateAsync(entity);

        triggers.Should().HaveCount(1);
        triggers[0].Type.Should().Be(AlertType.SpeedAnomaly);
        triggers[0].Severity.Should().Be(AlertSeverity.Medium);
        triggers[0].Summary.Should().Contain("Fast Aircraft");
        triggers[0].Summary.Should().Contain("600 knots");
        triggers[0].Summary.Should().Contain("Aircraft");
    }

    [Fact]
    public async Task NullSpeed_ReturnsEmpty()
    {
        var entity = MakeVessel(null);

        var triggers = await _sut.EvaluateAsync(entity);

        triggers.Should().BeEmpty();
    }

    [Fact]
    public async Task UnknownEntityType_HighSpeed_ReturnsEmpty()
    {
        // No threshold defined for Unknown entity type — should be skipped
        var entity = MakeUnknown(9999.0);

        var triggers = await _sut.EvaluateAsync(entity);

        triggers.Should().BeEmpty();
    }

    [Fact]
    public async Task Vessel_AtExactThreshold_ReturnsEmpty()
    {
        // Exactly at 50 knots = 25.72 m/s — should NOT trigger (> not >=)
        var entity = MakeVessel(25.72);

        var triggers = await _sut.EvaluateAsync(entity);

        triggers.Should().BeEmpty();
    }

    [Fact]
    public async Task Aircraft_AtExactThreshold_ReturnsEmpty()
    {
        // Exactly at 600 knots = 308.67 m/s — should NOT trigger
        var entity = MakeAircraft(308.67);

        var triggers = await _sut.EvaluateAsync(entity);

        triggers.Should().BeEmpty();
    }
}
