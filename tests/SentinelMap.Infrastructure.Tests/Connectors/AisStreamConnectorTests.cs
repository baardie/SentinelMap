using FluentAssertions;
using SentinelMap.Infrastructure.Connectors;

namespace SentinelMap.Infrastructure.Tests.Connectors;

public class AisStreamConnectorTests
{
    [Fact]
    public void ParsePositionReport_ExtractsCorrectFields()
    {
        var json = """
        {
          "MessageType": "PositionReport",
          "MetaData": {
            "MMSI": 235009888,
            "ShipName": "BRITANNIA STAR",
            "latitude": 51.1234,
            "longitude": -1.5678,
            "time_utc": "2026-03-19 14:30:00.000000"
          },
          "Message": {
            "PositionReport": {
              "Cog": 45.2,
              "Sog": 12.4,
              "TrueHeading": 44,
              "NavigationalStatus": 0
            }
          }
        }
        """;

        var obs = AisStreamConnector.ParseMessage(json);

        obs.Should().NotBeNull();
        obs!.SourceType.Should().Be("AIS");
        obs.ExternalId.Should().Be("235009888");
        obs.Position.Should().NotBeNull();
        obs.Position!.Y.Should().BeApproximately(51.1234, 0.0001);
        obs.Position!.X.Should().BeApproximately(-1.5678, 0.0001);
        obs.Heading.Should().BeApproximately(44.0, 0.1);
        obs.SpeedMps.Should().BeApproximately(12.4 * 0.514444, 0.01);
    }

    [Fact]
    public void ParsePositionReport_HeadingUnavailable_FallsBackToCog()
    {
        var json = """
        {
          "MessageType": "PositionReport",
          "MetaData": { "MMSI": 123, "latitude": 51.0, "longitude": 1.0, "time_utc": "2026-03-19 12:00:00.000000" },
          "Message": { "PositionReport": { "Cog": 90.0, "Sog": 5.0, "TrueHeading": 511 } }
        }
        """;

        var obs = AisStreamConnector.ParseMessage(json);

        obs.Should().NotBeNull();
        obs!.Heading.Should().BeApproximately(90.0, 0.1, "511 means heading unavailable — fall back to COG");
    }

    [Fact]
    public void ParseShipStaticData_ExtractsDisplayName()
    {
        var json = """
        {
          "MessageType": "ShipStaticData",
          "MetaData": { "MMSI": 235009888, "latitude": 51.0, "longitude": -1.0, "time_utc": "2026-03-19 12:00:00.000000" },
          "Message": {
            "ShipStaticData": {
              "Name": "BRITANNIA STAR",
              "CallSign": "MBST",
              "Type": 70,
              "ImoNumber": 1234567
            }
          }
        }
        """;

        var obs = AisStreamConnector.ParseMessage(json);

        obs.Should().NotBeNull();
        obs!.RawData.Should().Contain("BRITANNIA STAR");
        obs.RawData.Should().Contain("MBST");
    }

    [Fact]
    public void ParseUnknownMessageType_ReturnsNull()
    {
        var json = """{"MessageType": "Unknown", "MetaData": {}, "Message": {}}""";
        var obs = AisStreamConnector.ParseMessage(json);
        obs.Should().BeNull();
    }

    [Fact]
    public void ParseMalformedJson_ReturnsNull()
    {
        var obs = AisStreamConnector.ParseMessage("not json at all");
        obs.Should().BeNull();
    }
}
