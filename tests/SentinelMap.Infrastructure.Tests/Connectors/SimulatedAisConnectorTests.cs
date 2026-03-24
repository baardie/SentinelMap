using FluentAssertions;
using SentinelMap.Infrastructure.Connectors;

namespace SentinelMap.Infrastructure.Tests.Connectors;

public class SimulatedAisConnectorTests
{
    [Fact]
    public void SourceId_IsSimulatedAis()
    {
        var connector = new SimulatedAisConnector(updateIntervalMs: 50);
        connector.SourceId.Should().Be("simulated-ais");
        connector.SourceType.Should().Be("AIS");
    }

    [Fact]
    public async Task StreamAsync_YieldsObservations()
    {
        var connector = new SimulatedAisConnector(updateIntervalMs: 10);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var observations = new List<SentinelMap.Domain.Entities.Observation>();
        await foreach (var obs in connector.StreamAsync(cts.Token))
        {
            observations.Add(obs);
            if (observations.Count >= 5) break;
        }

        observations.Count.Should().BeGreaterThanOrEqualTo(5, "should yield multiple observations");
    }

    [Fact]
    public async Task StreamAsync_ObservationsHaveValidPositions()
    {
        var connector = new SimulatedAisConnector(updateIntervalMs: 10);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var observations = new List<SentinelMap.Domain.Entities.Observation>();
        await foreach (var obs in connector.StreamAsync(cts.Token))
        {
            observations.Add(obs);
            if (observations.Count >= 4) break;
        }

        foreach (var obs in observations)
        {
            obs.SourceType.Should().Be("AIS");
            obs.ExternalId.Should().NotBeNullOrEmpty();
            obs.Position.Should().NotBeNull();
            obs.Position!.Y.Should().BeInRange(-90, 90, "latitude");
            obs.Position!.X.Should().BeInRange(-180, 180, "longitude");
            obs.ObservedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
            obs.RawData.Should().NotBeNullOrEmpty("connector must embed displayName in RawData");
        }
    }

    [Fact]
    public async Task StreamAsync_CancellationStopsYielding()
    {
        var connector = new SimulatedAisConnector(updateIntervalMs: 50);
        using var cts = new CancellationTokenSource();

        var count = 0;
        await foreach (var _ in connector.StreamAsync(cts.Token))
        {
            count++;
            if (count == 2) cts.Cancel();
        }

        count.Should().BeLessThanOrEqualTo(16, "cancellation should stop iteration promptly");
    }

    [Fact]
    public async Task StreamAsync_ProducesMultipleUniqueVessels()
    {
        var connector = new SimulatedAisConnector(updateIntervalMs: 10);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var externalIds = new HashSet<string>();
        await foreach (var obs in connector.StreamAsync(cts.Token))
        {
            externalIds.Add(obs.ExternalId);
            if (externalIds.Count >= 6) break;
        }

        externalIds.Should().HaveCountGreaterThanOrEqualTo(6, "at least 6 distinct vessels should be observed");
    }
}
