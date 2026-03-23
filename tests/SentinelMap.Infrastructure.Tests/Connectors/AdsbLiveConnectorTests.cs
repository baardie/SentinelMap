using FluentAssertions;
using SentinelMap.Infrastructure.Connectors;

namespace SentinelMap.Infrastructure.Tests.Connectors;

public class AdsbLiveConnectorTests
{
    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SourceType_Should_Be_ADSB()
    {
        var connector = new AdsbLiveConnector(
            new System.Net.Http.HttpClient(),
            centreLat: 51.5,
            centreLon: -0.1);

        connector.SourceType.Should().Be("ADSB");
    }

    [Fact]
    public void SourceId_Should_Be_AdsbLive()
    {
        var connector = new AdsbLiveConnector(
            new System.Net.Http.HttpClient(),
            centreLat: 51.5,
            centreLon: -0.1);

        connector.SourceId.Should().Be("adsb-airplaneslive");
    }

    // -----------------------------------------------------------------------
    // ParseAircraft — happy path
    // -----------------------------------------------------------------------

    [Fact]
    public void ParseAircraft_Should_Parse_Valid_Aircraft()
    {
        var json = """
        {
            "hex": "400734",
            "flight": "BAW456  ",
            "lat": 51.4775,
            "lon": -0.4614,
            "alt_baro": 35000,
            "gs": 420.5,
            "track": 92.3,
            "t": "A320",
            "r": "G-EUYA",
            "squawk": "7421"
        }
        """;

        var obs = AdsbLiveConnector.ParseAircraft(json);

        obs.Should().NotBeNull();
        obs!.SourceType.Should().Be("ADSB");
        obs.ExternalId.Should().Be("400734");
        obs.Position.Should().NotBeNull();
        obs.Position!.Y.Should().BeApproximately(51.4775, 0.0001);
        obs.Position!.X.Should().BeApproximately(-0.4614, 0.0001);
        obs.SpeedMps.Should().BeApproximately(420.5 * 0.514444, 0.01);
        obs.Heading.Should().BeApproximately(92.3, 0.01);

        obs.RawData.Should().NotBeNullOrEmpty();
        obs.RawData.Should().Contain("BAW456");
        obs.RawData.Should().Contain("A320");
        obs.RawData.Should().Contain("35000");
        obs.RawData.Should().Contain("G-EUYA");
        obs.RawData.Should().Contain("7421");
    }

    // -----------------------------------------------------------------------
    // ParseAircraft — null cases
    // -----------------------------------------------------------------------

    [Fact]
    public void ParseAircraft_Should_Return_Null_For_Missing_Position()
    {
        var json = """
        {
            "hex": "400734",
            "flight": "BAW456",
            "alt_baro": 35000,
            "gs": 420.5,
            "track": 92.3
        }
        """;

        var obs = AdsbLiveConnector.ParseAircraft(json);

        obs.Should().BeNull("lat and lon are absent");
    }

    [Fact]
    public void ParseAircraft_Should_Return_Null_For_Missing_Hex()
    {
        var json = """
        {
            "flight": "BAW456",
            "lat": 51.4775,
            "lon": -0.4614,
            "alt_baro": 35000
        }
        """;

        var obs = AdsbLiveConnector.ParseAircraft(json);

        obs.Should().BeNull("hex identifier is absent");
    }

    // -----------------------------------------------------------------------
    // ParseAircraft — edge cases
    // -----------------------------------------------------------------------

    [Fact]
    public void ParseAircraft_Should_Handle_Ground_Aircraft()
    {
        var json = """
        {
            "hex": "3c4525",
            "lat": 48.3537,
            "lon": 11.7750,
            "alt_baro": "ground"
        }
        """;

        var obs = AdsbLiveConnector.ParseAircraft(json);

        obs.Should().NotBeNull();
        obs!.RawData.Should().Contain("\"altitude\":0", "ground aircraft should map to altitude 0");
    }

    [Fact]
    public void ParseAircraft_Should_Trim_Callsign_Whitespace()
    {
        var json = """
        {
            "hex": "400734",
            "flight": "  EZY123   ",
            "lat": 51.0,
            "lon": -1.0
        }
        """;

        var obs = AdsbLiveConnector.ParseAircraft(json);

        obs.Should().NotBeNull();
        obs!.RawData.Should().Contain("EZY123");
        obs.RawData.Should().NotContain("  EZY123", "leading spaces should be trimmed");
        obs.RawData.Should().NotContain("EZY123   ", "trailing spaces should be trimmed");
    }
}
