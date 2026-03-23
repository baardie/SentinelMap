using FluentAssertions;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Infrastructure.Correlation;
using SentinelMap.SharedKernel.Enums;

namespace SentinelMap.Infrastructure.Tests.Correlation;

public class DirectIdMatchRuleTests
{
    private readonly DirectIdMatchRule _sut = new();
    private readonly Point _position = new(0, 0) { SRID = 4326 };

    private static TrackedEntity MakeEntity(params (string type, string value)[] identifiers)
    {
        var entity = new TrackedEntity
        {
            Type = EntityType.Vessel,
            DisplayName = "Test Vessel",
        };
        foreach (var (type, value) in identifiers)
        {
            entity.Identifiers.Add(new EntityIdentifier
            {
                EntityId = entity.Id,
                IdentifierType = type,
                IdentifierValue = value,
                Source = "AIS",
            });
        }
        return entity;
    }

    [Fact]
    public async Task MatchingIdentifier_ReturnsHighConfidence()
    {
        var entity = MakeEntity(("MMSI", "123456789"));

        var score = await _sut.EvaluateAsync("AIS", "123456789", "Vessel X", _position, entity);

        score.Should().NotBeNull();
        score!.Confidence.Should().Be(0.95);
        score.RuleId.Should().Be("DirectIdMatch");
    }

    [Fact]
    public async Task NoMatchingIdentifier_ReturnsNull()
    {
        var entity = MakeEntity(("MMSI", "999999999"));

        var score = await _sut.EvaluateAsync("AIS", "123456789", "Vessel X", _position, entity);

        score.Should().BeNull();
    }

    [Fact]
    public async Task CaseInsensitiveMatch_ReturnsScore()
    {
        var entity = MakeEntity(("ICAO", "abc123"));

        var score = await _sut.EvaluateAsync("ADSB", "ABC123", "Aircraft Y", _position, entity);

        score.Should().NotBeNull();
        score!.Confidence.Should().Be(0.95);
    }

    [Fact]
    public async Task EntityWithNoIdentifiers_ReturnsNull()
    {
        var entity = MakeEntity();

        var score = await _sut.EvaluateAsync("AIS", "123456789", "Vessel X", _position, entity);

        score.Should().BeNull();
    }
}
