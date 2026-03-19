using FluentAssertions;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Infrastructure.Pipeline;

namespace SentinelMap.Infrastructure.Tests.Pipeline;

public class ObservationValidatorTests
{
    private readonly ObservationValidator _validator = new();

    private static Observation ValidObservation() => new()
    {
        SourceType = "AIS",
        ExternalId = "235009888",
        Position = new Point(-1.5, 51.0) { SRID = 4326 },
        ObservedAt = DateTimeOffset.UtcNow.AddSeconds(-10),
    };

    [Fact]
    public async Task ValidObservation_PassesValidation()
    {
        var result = await _validator.ValidateAsync(ValidObservation());
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task MissingExternalId_FailsValidation()
    {
        var obs = ValidObservation();
        obs.ExternalId = "";
        var result = await _validator.ValidateAsync(obs);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(Observation.ExternalId));
    }

    [Fact]
    public async Task MissingSourceType_FailsValidation()
    {
        var obs = ValidObservation();
        obs.SourceType = "";
        var result = await _validator.ValidateAsync(obs);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task NullPosition_FailsValidation()
    {
        var obs = ValidObservation();
        obs.Position = null;
        var result = await _validator.ValidateAsync(obs);
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(-91.0, 0.0)]
    [InlineData(91.0, 0.0)]
    [InlineData(0.0, -181.0)]
    [InlineData(0.0, 181.0)]
    public async Task OutOfBoundsPosition_FailsValidation(double lat, double lon)
    {
        var obs = ValidObservation();
        obs.Position = new Point(lon, lat) { SRID = 4326 };
        var result = await _validator.ValidateAsync(obs);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task FutureTimestamp_FailsValidation()
    {
        var obs = ValidObservation();
        obs.ObservedAt = DateTimeOffset.UtcNow.AddMinutes(2);
        var result = await _validator.ValidateAsync(obs);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task StaleTimestamp_FailsValidation()
    {
        var obs = ValidObservation();
        obs.ObservedAt = DateTimeOffset.UtcNow.AddHours(-25);
        var result = await _validator.ValidateAsync(obs);
        result.IsValid.Should().BeFalse();
    }
}
