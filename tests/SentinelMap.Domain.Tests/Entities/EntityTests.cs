using FluentAssertions;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.SharedKernel.Enums;

namespace SentinelMap.Domain.Tests.Entities;

public class EntityTests
{
    [Fact]
    public void NewEntity_HasActiveStatus()
    {
        var entity = new TrackedEntity
        {
            DisplayName = "EVER GIVEN",
            Type = EntityType.Vessel
        };

        entity.Status.Should().Be(EntityStatus.Active);
        entity.Id.Should().NotBeEmpty();
        entity.Classification.Should().Be(Classification.Official);
    }

    [Fact]
    public void Entity_UpdatePosition_SetsLastSeenAndPosition()
    {
        var entity = new TrackedEntity
        {
            DisplayName = "TEST VESSEL",
            Type = EntityType.Vessel
        };

        var point = new Point(1.5, 51.0) { SRID = 4326 };
        var now = DateTimeOffset.UtcNow;

        entity.UpdatePosition(point, speedMps: 5.0, heading: 180.0, now);

        entity.LastKnownPosition.Should().Be(point);
        entity.LastSpeedMps.Should().Be(5.0);
        entity.LastHeading.Should().Be(180.0);
        entity.LastSeen.Should().Be(now);
    }
}
