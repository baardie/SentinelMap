using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;

namespace SentinelMap.Infrastructure.Connectors;

/// <summary>
/// Yields ADS-B observations by replaying real recorded aircraft tracks loaded from
/// an embedded JSON resource. Each aircraft cycles through its waypoints in order,
/// looping back to the start when the track is exhausted.
/// Requires no API keys — enables demo and offline development.
/// </summary>
public class SimulatedAdsbConnector : ISourceConnector
{
    private readonly int _baseIntervalMs;
    private readonly Random _rng = new();
    private readonly List<SampleAircraft> _aircraft;

    public SimulatedAdsbConnector(int updateIntervalMs = 2000)
    {
        _baseIntervalMs = updateIntervalMs;
        _aircraft = LoadSampleData();
    }

    public string SourceId => "simulated-adsb";
    public string SourceType => "ADSB";

    // ── Sample data model ────────────────────────────────────────────────

    private sealed class SampleAircraft
    {
        [JsonPropertyName("icao")] public string Icao { get; set; } = "";
        [JsonPropertyName("callsign")] public string Callsign { get; set; } = "";
        [JsonPropertyName("aircraftType")] public string AircraftType { get; set; } = "";
        [JsonPropertyName("registration")] public string Registration { get; set; } = "";
        [JsonPropertyName("category")] public string Category { get; set; } = "";
        [JsonPropertyName("military")] public bool Military { get; set; }
        [JsonPropertyName("waypoints")] public List<AdsbWaypoint> Waypoints { get; set; } = [];
    }

    private sealed class AdsbWaypoint
    {
        [JsonPropertyName("lon")] public double Lon { get; set; }
        [JsonPropertyName("lat")] public double Lat { get; set; }
        [JsonPropertyName("heading")] public double Heading { get; set; }
        [JsonPropertyName("speedMps")] public double SpeedMps { get; set; }
        [JsonPropertyName("altitude")] public double Altitude { get; set; }
    }

    private class AircraftState
    {
        public int WaypointIndex;
    }

    // ── Load embedded resource ───────────────────────────────────────────

    private static List<SampleAircraft> LoadSampleData()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith("adsb-tracks.json", StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        return JsonSerializer.Deserialize<List<SampleAircraft>>(stream) ?? [];
    }

    // ── Streaming ────────────────────────────────────────────────────────

    public async IAsyncEnumerable<Observation> StreamAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (_aircraft.Count == 0) yield break;

        // Initialise state — stagger start positions
        var state = new AircraftState[_aircraft.Count];
        for (int i = 0; i < _aircraft.Count; i++)
        {
            state[i] = new AircraftState
            {
                WaypointIndex = _rng.Next(_aircraft[i].Waypoints.Count),
            };
        }

        while (!ct.IsCancellationRequested)
        {
            for (int i = 0; i < _aircraft.Count; i++)
            {
                if (ct.IsCancellationRequested) yield break;

                var aircraft = _aircraft[i];
                var s = state[i];

                if (aircraft.Waypoints.Count == 0) continue;

                // Current and next waypoints (for vertical rate computation)
                var wp = aircraft.Waypoints[s.WaypointIndex];
                var nextIdx = (s.WaypointIndex + 1) % aircraft.Waypoints.Count;
                var wpNext = aircraft.Waypoints[nextIdx];

                // Apply small jitter to position (~5.5m)
                var (lon, lat) = ApplyJitter(wp.Lon, wp.Lat, 0.00005);
                var heading = wp.Heading;
                var speedMps = wp.SpeedMps * (0.9 + _rng.NextDouble() * 0.2);
                var altitudeFt = (int)Math.Max(0, wp.Altitude);

                // Compute vertical rate from altitude difference between waypoints (ft/min estimate)
                var verticalRate = (int)(wpNext.Altitude - wp.Altitude);

                yield return new Observation
                {
                    SourceType = "ADSB",
                    ExternalId = aircraft.Icao,
                    Position = new Point(lon, lat) { SRID = 4326 },
                    Heading = heading,
                    SpeedMps = speedMps,
                    ObservedAt = DateTimeOffset.UtcNow,
                    RawData = JsonSerializer.Serialize(new
                    {
                        displayName = aircraft.Callsign,
                        aircraftType = aircraft.AircraftType,
                        registration = aircraft.Registration,
                        category = aircraft.Category,
                        military = aircraft.Military,
                        altitude = altitudeFt,
                        squawk = "1000",
                        emergency = "none",
                        verticalRate,
                    }),
                };

                // Advance to next waypoint (loop)
                s.WaypointIndex = nextIdx;

                // Stagger emissions: small random delay between each aircraft
                if (i < _aircraft.Count - 1)
                {
                    try { await Task.Delay(50 + _rng.Next(150), ct); }
                    catch (OperationCanceledException) { yield break; }
                }
            }

            // Main interval with slight jitter (±20%)
            var interval = (int)(_baseIntervalMs * (0.8 + _rng.NextDouble() * 0.4));
            try { await Task.Delay(interval, ct); }
            catch (OperationCanceledException) { yield break; }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private (double lon, double lat) ApplyJitter(double lon, double lat, double magnitude)
    {
        lon += (_rng.NextDouble() - 0.5) * magnitude;
        lat += (_rng.NextDouble() - 0.5) * magnitude;
        return (lon, lat);
    }
}
