using FluentAssertions;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Infrastructure.Correlation;
using SentinelMap.SharedKernel.Enums;

namespace SentinelMap.Infrastructure.Tests.Correlation;

public class NameFuzzyMatchRuleTests
{
    private readonly NameFuzzyMatchRule _sut = new();
    private readonly Point _position = new(0, 0) { SRID = 4326 };

    private static TrackedEntity MakeEntity(string? displayName) => new()
    {
        Type = EntityType.Vessel,
        DisplayName = displayName,
    };

    [Fact]
    public async Task HighlySimilarNames_ReturnsScore()
    {
        var entity = MakeEntity("MV EVER GIVEN");

        var score = await _sut.EvaluateAsync("AIS", "123", "EVERGIVEN", _position, entity);

        score.Should().NotBeNull();
        score!.Confidence.Should().BeGreaterThan(0.6);
        score.RuleId.Should().Be("NameFuzzyMatch");
    }

    [Fact]
    public async Task ExactNameMatch_ReturnsHighConfidence()
    {
        var entity = MakeEntity("QUEEN ELIZABETH");

        var score = await _sut.EvaluateAsync("AIS", "123", "HMS Queen Elizabeth", _position, entity);

        // Both normalise to "QUEEN ELIZABETH" → JW = 1.0 → confidence = 0.85
        score.Should().NotBeNull();
        score!.Confidence.Should().BeApproximately(0.85, 0.01);
    }

    [Fact]
    public async Task CompletelyDifferentNames_ReturnsNull()
    {
        var entity = MakeEntity("PACIFIC EXPLORER");

        var score = await _sut.EvaluateAsync("AIS", "123", "ATLANTIC WARRIOR", _position, entity);

        score.Should().BeNull();
    }

    [Fact]
    public async Task NullDisplayName_ReturnsNull()
    {
        var entity = MakeEntity("SOME VESSEL");

        var score = await _sut.EvaluateAsync("AIS", "123", null, _position, entity);

        score.Should().BeNull();
    }

    [Fact]
    public async Task NullCandidateDisplayName_ReturnsNull()
    {
        var entity = MakeEntity(null);

        var score = await _sut.EvaluateAsync("AIS", "123", "SOME VESSEL", _position, entity);

        score.Should().BeNull();
    }
}
