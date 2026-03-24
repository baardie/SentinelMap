using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;

namespace SentinelMap.Infrastructure.Connectors;

/// <summary>
/// Live AIS connector via AISStream.io WebSocket.
/// Requires AISSTREAM_API_KEY environment variable.
/// Parses message types: PositionReport (1-3), ShipStaticData (5), StandardClassBPositionReport (18-19),
/// BaseStationReport (4), AidsToNavigationReport (21), SafetyBroadcastMessage.
/// </summary>
public class AisStreamConnector : ISourceConnector
{
    private const string WssUrl = "wss://stream.aisstream.io/v0/stream";
    private const int HeadingUnavailable = 511;
    private const double KnotsToMps = 0.514444;

    private readonly string _apiKey;
    private readonly ILogger<AisStreamConnector> _logger;

    public AisStreamConnector(string apiKey, ILogger<AisStreamConnector> logger)
    {
        _apiKey = apiKey;
        _logger = logger;
    }

    public string SourceId => "aisstream";
    public string SourceType => "AIS";

    public async IAsyncEnumerable<Observation> StreamAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(WssUrl), ct);

        var subscription = JsonSerializer.Serialize(new
        {
            APIKey = _apiKey,
            // UK + NI + English Channel + Irish Sea bounding box
            // AISStream format: [[lat_min, lon_min], [lat_max, lon_max]]
            BoundingBoxes = new[] { new[] { new[] { 49.0, -11.0 }, new[] { 61.0, 2.5 } } },
            FilterMessageTypes = new[] { "PositionReport", "ShipStaticData", "StandardClassBPositionReport", "BaseStationReport", "AidsToNavigationReport", "SafetyBroadcastMessage" }
        });

        var subscriptionBytes = Encoding.UTF8.GetBytes(subscription);
        await ws.SendAsync(subscriptionBytes, WebSocketMessageType.Text, true, ct);

        _logger.LogInformation("AISStream WebSocket connected and subscribed");

        var buffer = new byte[64 * 1024];

        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            var messageBuilder = new StringBuilder();

            do
            {
                result = await ws.ReceiveAsync(buffer, ct);
                messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            } while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close) break;

            var message = messageBuilder.ToString();
            var observation = ParseMessage(message);
            if (observation is not null)
                yield return observation;
        }
    }

    /// <summary>
    /// Parses a raw AISStream JSON message into an Observation.
    /// Public static for direct unit testing without a WebSocket.
    /// Returns null for unsupported message types or parse failures.
    /// </summary>
    public static Observation? ParseMessage(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is null) return null;

            var messageType = node["MessageType"]?.GetValue<string>();
            var meta = node["MetaData"];

            if (meta is null || messageType is null) return null;

            var mmsi = meta["MMSI"]?.ToString() ?? meta["MMSI_String"]?.GetValue<string>();
            if (string.IsNullOrEmpty(mmsi)) return null;

            var timeStr = meta["time_utc"]?.GetValue<string>() ?? "";
            if (!DateTimeOffset.TryParse(timeStr, out var observedAt))
                observedAt = DateTimeOffset.UtcNow;

            return messageType switch
            {
                "PositionReport" => ParsePositionReport(node, mmsi, observedAt),
                "StandardClassBPositionReport" => ParsePositionReport(node, mmsi, observedAt),
                "ShipStaticData" => ParseShipStaticData(node, mmsi, observedAt),
                "BaseStationReport" => ParseBaseStationReport(node, mmsi, observedAt),
                "AidsToNavigationReport" => ParseAidsToNavigationReport(node, mmsi, observedAt),
                "SafetyBroadcastMessage" => ParseSafetyBroadcastMessage(node, mmsi, observedAt),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    private static Observation? ParsePositionReport(JsonNode node, string mmsi, DateTimeOffset observedAt)
    {
        var meta = node["MetaData"];
        var report = node["Message"]?["PositionReport"]
                  ?? node["Message"]?["StandardClassBPositionReport"];

        if (report is null) return null;

        var lat = meta?["latitude"]?.GetValue<double>() ?? 0;
        var lon = meta?["longitude"]?.GetValue<double>() ?? 0;
        var cog = report["Cog"]?.GetValue<double>() ?? 0;
        var sog = report["Sog"]?.GetValue<double>() ?? 0;
        var trueHeading = report["TrueHeading"]?.GetValue<int>() ?? HeadingUnavailable;

        var heading = trueHeading == HeadingUnavailable ? cog : (double)trueHeading;

        return new Observation
        {
            SourceType = "AIS",
            ExternalId = mmsi,
            Position = new Point(lon, lat) { SRID = 4326 },
            Heading = heading,
            SpeedMps = sog * KnotsToMps,
            ObservedAt = observedAt,
            RawData = JsonSerializer.Serialize(new
            {
                displayName = meta?["ShipName"]?.GetValue<string>(),
                vesselType = "Unknown",
            }),
        };
    }

    private static Observation? ParseShipStaticData(JsonNode node, string mmsi, DateTimeOffset observedAt)
    {
        var meta = node["MetaData"];
        var staticData = node["Message"]?["ShipStaticData"];
        if (staticData is null) return null;

        var lat = meta?["latitude"]?.GetValue<double>() ?? 0;
        var lon = meta?["longitude"]?.GetValue<double>() ?? 0;

        var name = staticData["Name"]?.GetValue<string>() ?? mmsi;
        var callsign = staticData["CallSign"]?.GetValue<string>();
        var imo = staticData["ImoNumber"]?.GetValue<int>();
        var shipType = staticData["Type"]?.GetValue<int>();

        return new Observation
        {
            SourceType = "AIS",
            ExternalId = mmsi,
            Position = new Point(lon, lat) { SRID = 4326 },
            ObservedAt = observedAt,
            RawData = JsonSerializer.Serialize(new
            {
                displayName = name,
                callsign,
                imoNumber = imo,
                aisShipType = shipType,
                vesselType = MapAisShipType(shipType),
            }),
        };
    }

    private static Observation? ParseBaseStationReport(JsonNode node, string mmsi, DateTimeOffset observedAt)
    {
        var report = node["Message"]?["BaseStationReport"];
        if (report is null) return null;

        var lat = report["Latitude"]?.GetValue<double>() ?? 0;
        var lon = report["Longitude"]?.GetValue<double>() ?? 0;

        if (lat == 0 && lon == 0) return null;

        return new Observation
        {
            SourceType = "AIS_INFRA",
            ExternalId = mmsi,
            Position = new Point(lon, lat) { SRID = 4326 },
            ObservedAt = observedAt,
            RawData = JsonSerializer.Serialize(new
            {
                featureType = "AisBaseStation",
                mmsi,
            }),
        };
    }

    private static Observation? ParseAidsToNavigationReport(JsonNode node, string mmsi, DateTimeOffset observedAt)
    {
        var report = node["Message"]?["AidsToNavigationReport"];
        if (report is null) return null;

        var lat = report["Latitude"]?.GetValue<double>() ?? 0;
        var lon = report["Longitude"]?.GetValue<double>() ?? 0;
        var name = report["Name"]?.GetValue<string>() ?? mmsi;
        var aidType = report["Type"]?.GetValue<int>() ?? 0;

        if (lat == 0 && lon == 0) return null;

        return new Observation
        {
            SourceType = "AIS_INFRA",
            ExternalId = mmsi,
            Position = new Point(lon, lat) { SRID = 4326 },
            ObservedAt = observedAt,
            RawData = JsonSerializer.Serialize(new
            {
                featureType = "AidToNavigation",
                name,
                aidType,
            }),
        };
    }

    private static Observation? ParseSafetyBroadcastMessage(JsonNode node, string mmsi, DateTimeOffset observedAt)
    {
        var report = node["Message"]?["SafetyBroadcastMessage"];
        if (report is null) return null;

        var text = report["Text"]?.GetValue<string>() ?? "";

        return new Observation
        {
            SourceType = "AIS_SAFETY",
            ExternalId = mmsi,
            ObservedAt = observedAt,
            RawData = JsonSerializer.Serialize(new
            {
                text,
                mmsi,
            }),
        };
    }

    private static string MapAisShipType(int? aisType) => aisType switch
    {
        >= 70 and <= 79 => "Cargo",
        >= 80 and <= 89 => "Tanker",
        >= 60 and <= 69 => "Passenger",
        30 => "Fishing",
        _ => "Unknown",
    };
}
