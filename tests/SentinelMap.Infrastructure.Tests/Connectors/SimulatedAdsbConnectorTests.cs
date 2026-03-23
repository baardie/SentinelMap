using System.Text.Json;
using FluentAssertions;
using SentinelMap.Infrastructure.Connectors;

namespace SentinelMap.Infrastructure.Tests.Connectors;

public class SimulatedAdsbConnectorTests
{
    [Fact]
    public void SourceType_Should_Be_ADSB()
    {
        var connector = new SimulatedAdsbConnector(updateIntervalMs: 50);
        connector.SourceType.Should().Be("ADSB");
    }

    [Fact]
    public void SourceId_Should_Be_SimulatedAdsb()
    {
        var connector = new SimulatedAdsbConnector(updateIntervalMs: 50);
        connector.SourceId.Should().Be("simulated-adsb");
    }

    [Fact]
    public async Task StreamAsync_Should_Yield_Observations_With_Correct_Fields()
    {
        var connector = new SimulatedAdsbConnector(updateIntervalMs: 10);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var observations = new List<SentinelMap.Domain.Entities.Observation>();
        await foreach (var obs in connector.StreamAsync(cts.Token))
        {
            observations.Add(obs);
            if (observations.Count >= 8) break;
        }

        observations.Should().HaveCountGreaterThanOrEqualTo(8);

        foreach (var obs in observations)
        {
            obs.SourceType.Should().Be("ADSB");
            obs.ExternalId.Should().NotBeNullOrEmpty();
            obs.Position.Should().NotBeNull();
            obs.Position!.SRID.Should().Be(4326);

            // Latitude: Liverpool/Mersey area ~ 53-54, but allow broader test range
            obs.Position.Y.Should().BeInRange(51, 55, "latitude should be in UK/Liverpool area");
            // Longitude: Liverpool area ~ -3, allow broader test range
            obs.Position.X.Should().BeInRange(-5, 0, "longitude should be in UK/Liverpool area");

            obs.Heading.Should().NotBeNull();
            obs.SpeedMps.Should().BeGreaterThan(0);

            obs.RawData.Should().NotBeNullOrEmpty();
            var doc = JsonDocument.Parse(obs.RawData!);
            doc.RootElement.TryGetProperty("aircraftType", out _).Should().BeTrue("RawData must contain aircraftType");
            doc.RootElement.TryGetProperty("altitude", out _).Should().BeTrue("RawData must contain altitude");
        }
    }

    [Fact]
    public async Task StreamAsync_Should_Produce_Multiple_Unique_Aircraft()
    {
        var connector = new SimulatedAdsbConnector(updateIntervalMs: 10);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1000));

        var externalIds = new HashSet<string>();
        await foreach (var obs in connector.StreamAsync(cts.Token))
        {
            externalIds.Add(obs.ExternalId);
            if (externalIds.Count >= 6) break;
        }

        externalIds.Should().HaveCountGreaterThanOrEqualTo(6, "at least 6 distinct aircraft should be observed");
    }

    [Fact]
    public async Task StreamAsync_Should_Cancel_Gracefully()
    {
        var connector = new SimulatedAdsbConnector(updateIntervalMs: 50);
        using var cts = new CancellationTokenSource();

        var count = 0;
        await foreach (var _ in connector.StreamAsync(cts.Token))
        {
            count++;
            if (count == 2) cts.Cancel();
        }

        // After cancellation, iteration should stop promptly (well within one full cycle of all aircraft)
        count.Should().BeLessThanOrEqualTo(20, "cancellation should stop iteration promptly");
    }

    [Fact]
    public async Task StreamAsync_RawData_Should_Contain_DisplayName()
    {
        var connector = new SimulatedAdsbConnector(updateIntervalMs: 10);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var observations = new List<SentinelMap.Domain.Entities.Observation>();
        await foreach (var obs in connector.StreamAsync(cts.Token))
        {
            observations.Add(obs);
            if (observations.Count >= 4) break;
        }

        foreach (var obs in observations)
        {
            obs.RawData.Should().NotBeNullOrEmpty();
            var doc = JsonDocument.Parse(obs.RawData!);
            doc.RootElement.TryGetProperty("displayName", out var displayName).Should().BeTrue("RawData must contain displayName");
            displayName.GetString().Should().NotBeNullOrEmpty();
        }
    }
}
