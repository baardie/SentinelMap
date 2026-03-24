using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;

namespace SentinelMap.Infrastructure.Connectors;

/// <summary>
/// Live ADS-B connector via Airplanes.live REST API.
/// Polls GET https://api.airplanes.live/v2/point/{lat}/{lon}/{radius} on a configurable interval.
/// No authentication required.
/// </summary>
public class AdsbLiveConnector : ISourceConnector
{
    private const string BaseUrl = "https://api.airplanes.live/v2/point";
    private const double KnotsToMps = 0.514444;

    private readonly HttpClient _http;
    private readonly double _centreLat;
    private readonly double _centreLon;
    private readonly int _radiusNm;
    private readonly ILogger<AdsbLiveConnector> _logger;
    private readonly int _pollIntervalMs;

    public AdsbLiveConnector(
        HttpClient http,
        double centreLat,
        double centreLon,
        int radiusNm = 50,
        ILogger<AdsbLiveConnector>? logger = null,
        int pollIntervalMs = 5000)
    {
        _http = http;
        _centreLat = centreLat;
        _centreLon = centreLon;
        _radiusNm = radiusNm;
        _logger = logger ?? NullLogger<AdsbLiveConnector>.Instance;
        _pollIntervalMs = pollIntervalMs;
    }

    public string SourceId => "adsb-airplaneslive";
    public string SourceType => "ADSB";

    public async IAsyncEnumerable<Observation> StreamAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var url = $"{BaseUrl}/{_centreLat}/{_centreLon}/{_radiusNm}";

        while (!ct.IsCancellationRequested)
        {
            List<string> aircraftJsonList;

            try
            {
                var response = await _http.GetAsync(url, ct);
                response.EnsureSuccessStatusCode();

                var raw = await response.Content.ReadAsStringAsync(ct);
                aircraftJsonList = ExtractAircraftJsonObjects(raw);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("429"))
            {
                _logger.LogWarning("ADS-B rate limited (429), backing off 30s");
                aircraftJsonList = new List<string>();
                try { await Task.Delay(30000, ct); } catch (OperationCanceledException) { yield break; }
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ADS-B poll failed for {Url}; will retry in {Interval}ms", url, _pollIntervalMs);
                aircraftJsonList = new List<string>();
            }

            foreach (var acJson in aircraftJsonList)
            {
                var obs = ParseAircraft(acJson);
                if (obs is not null)
                    yield return obs;
            }

            try
            {
                await Task.Delay(_pollIntervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }

    /// <summary>
    /// Parses a single aircraft JSON object from the Airplanes.live API into an Observation.
    /// Public static for direct unit testing without HTTP.
    /// Returns null if hex, lat, or lon are missing.
    /// </summary>
    public static Observation? ParseAircraft(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is null) return null;

            var hex = node["hex"]?.GetValue<string>();
            if (string.IsNullOrEmpty(hex)) return null;

            // lat/lon may be absent — treat missing as null
            var latNode = node["lat"];
            var lonNode = node["lon"];
            if (latNode is null || lonNode is null) return null;

            var lat = latNode.GetValue<double>();
            var lon = lonNode.GetValue<double>();

            var flight = node["flight"]?.GetValue<string>()?.Trim();
            var aircraftType = node["t"]?.GetValue<string>();
            var registration = node["r"]?.GetValue<string>();
            var squawk = node["squawk"]?.GetValue<string>();
            var emergency = node["emergency"]?.GetValue<string>();
            var category = node["category"]?.GetValue<string>();

            // dbFlags: bit 1 = military, bit 2 = interesting, bit 4 = PIA, bit 8 = LADD
            var dbFlagsNode = node["dbFlags"];
            int dbFlags = 0;
            if (dbFlagsNode is not null)
                dbFlags = dbFlagsNode.GetValue<int>();
            var isMilitary = (dbFlags & 1) != 0;

            // baro_rate — vertical rate in ft/min
            double? verticalRate = null;
            var baroRateNode = node["baro_rate"];
            if (baroRateNode is not null)
                verticalRate = baroRateNode.GetValue<double>();

            // alt_baro can be an integer or the string "ground"
            int altitude = 0;
            var altNode = node["alt_baro"];
            if (altNode is not null)
            {
                if (altNode.GetValueKind() == JsonValueKind.String)
                {
                    var altStr = altNode.GetValue<string>();
                    if (!string.Equals(altStr, "ground", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(altStr, out altitude);
                    // "ground" → 0 (already the default)
                }
                else
                {
                    altitude = altNode.GetValue<int>();
                }
            }

            double? speedMps = null;
            var gsNode = node["gs"];
            if (gsNode is not null)
                speedMps = gsNode.GetValue<double>() * KnotsToMps;

            double? heading = null;
            var trackNode = node["track"];
            if (trackNode is not null)
                heading = trackNode.GetValue<double>();

            // Detect emergency status: explicit emergency field or emergency squawk codes
            var isEmergency = !string.IsNullOrEmpty(emergency) && !string.Equals(emergency, "none", StringComparison.OrdinalIgnoreCase);
            if (!isEmergency && (squawk == "7500" || squawk == "7600" || squawk == "7700"))
            {
                isEmergency = true;
                emergency = squawk switch
                {
                    "7500" => "unlawful",   // hijack
                    "7600" => "nordo",       // radio failure
                    "7700" => "general",     // general emergency
                    _ => emergency,
                };
            }

            return new Observation
            {
                SourceType = "ADSB",
                ExternalId = hex.ToUpperInvariant(),
                Position = new Point(lon, lat) { SRID = 4326 },
                SpeedMps = speedMps,
                Heading = heading,
                ObservedAt = DateTimeOffset.UtcNow,
                RawData = JsonSerializer.Serialize(new
                {
                    displayName = flight,
                    aircraftType,
                    altitude,
                    registration,
                    squawk,
                    verticalRate,
                    emergency = emergency ?? "none",
                    military = isMilitary,
                    category,
                }),
            };
        }
        catch
        {
            return null;
        }
    }

    // Extracts each element of the "ac" array as a raw JSON string.
    private static List<string> ExtractAircraftJsonObjects(string responseJson)
    {
        var results = new List<string>();
        try
        {
            var root = JsonNode.Parse(responseJson);
            var acArray = root?["ac"]?.AsArray();
            if (acArray is null) return results;

            foreach (var item in acArray)
            {
                if (item is not null)
                    results.Add(item.ToJsonString());
            }
        }
        catch
        {
            // malformed response — return empty list
        }
        return results;
    }
}
