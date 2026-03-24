using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;

namespace SentinelMap.Infrastructure.Connectors;

/// <summary>
/// Yields AIS observations by replaying real recorded vessel tracks loaded from
/// an embedded JSON resource. Each vessel cycles through its waypoints in order,
/// looping back to the start when the track is exhausted.
/// Requires no API keys — enables demo and offline development.
/// </summary>
public class SimulatedAisConnector : ISourceConnector
{
    private readonly int _baseIntervalMs;
    private readonly Random _rng = new();
    private readonly List<SampleVessel> _vessels;

    public SimulatedAisConnector(int updateIntervalMs = 2000)
    {
        _baseIntervalMs = updateIntervalMs;
        _vessels = LoadSampleData();
    }

    public string SourceId => "simulated-ais";
    public string SourceType => "AIS";

    // ── Sample data model ────────────────────────────────────────────────

    private sealed class SampleVessel
    {
        [JsonPropertyName("mmsi")] public string Mmsi { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("vesselType")] public string VesselType { get; set; } = "";
        [JsonPropertyName("destination")] public string Destination { get; set; } = "";
        [JsonPropertyName("imo")] public string Imo { get; set; } = "";
        [JsonPropertyName("callsign")] public string Callsign { get; set; } = "";
        [JsonPropertyName("shipTypeCode")] public int ShipTypeCode { get; set; }
        [JsonPropertyName("length")] public int Length { get; set; }
        [JsonPropertyName("beam")] public int Beam { get; set; }
        [JsonPropertyName("draught")] public double Draught { get; set; }
        [JsonPropertyName("waypoints")] public List<AisWaypoint> Waypoints { get; set; } = [];
    }

    private sealed class AisWaypoint
    {
        [JsonPropertyName("lon")] public double Lon { get; set; }
        [JsonPropertyName("lat")] public double Lat { get; set; }
        [JsonPropertyName("heading")] public double Heading { get; set; }
        [JsonPropertyName("speedMps")] public double SpeedMps { get; set; }
    }

    private class VesselState
    {
        public int WaypointIndex;
        public int DarkTicksRemaining;
        public bool HasGoneDark;
    }

    // ── Load embedded resource ───────────────────────────────────────────

    private static List<SampleVessel> LoadSampleData()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith("ais-tracks.json", StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        return JsonSerializer.Deserialize<List<SampleVessel>>(stream) ?? [];
    }

    // ── Streaming ────────────────────────────────────────────────────────

    public async IAsyncEnumerable<Observation> StreamAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (_vessels.Count == 0) yield break;

        // Initialise state — stagger start positions
        var state = new VesselState[_vessels.Count];
        for (int i = 0; i < _vessels.Count; i++)
        {
            state[i] = new VesselState
            {
                WaypointIndex = _rng.Next(_vessels[i].Waypoints.Count),
            };
        }

        while (!ct.IsCancellationRequested)
        {
            for (int i = 0; i < _vessels.Count; i++)
            {
                if (ct.IsCancellationRequested) yield break;

                var vessel = _vessels[i];
                var s = state[i];

                if (vessel.Waypoints.Count == 0) continue;

                // Current waypoint
                var wp = vessel.Waypoints[s.WaypointIndex];

                // Apply small jitter to position (~11m)
                var (lon, lat) = ApplyJitter(wp.Lon, wp.Lat, 0.0001);
                var heading = wp.Heading;
                var speedMps = wp.SpeedMps * (0.85 + _rng.NextDouble() * 0.3);

                // ── AIS dark period (first vessel, index 0) ──────────────
                bool suppressObservation = false;
                if (i == 0)
                {
                    if (s.DarkTicksRemaining > 0)
                    {
                        s.DarkTicksRemaining--;
                        suppressObservation = true;
                    }
                    else if (!s.HasGoneDark &&
                             s.WaypointIndex >= vessel.Waypoints.Count - 2)
                    {
                        s.DarkTicksRemaining = 17;
                        s.HasGoneDark = true;
                        suppressObservation = true;
                    }

                    // Reset when looping back near start
                    if (s.WaypointIndex < 2)
                    {
                        s.HasGoneDark = false;
                    }
                }

                if (!suppressObservation)
                {
                    yield return new Observation
                    {
                        SourceType = "AIS",
                        ExternalId = vessel.Mmsi,
                        Position = new Point(lon, lat) { SRID = 4326 },
                        Heading = heading,
                        SpeedMps = speedMps,
                        ObservedAt = DateTimeOffset.UtcNow,
                        RawData = JsonSerializer.Serialize(new
                        {
                            displayName = vessel.Name,
                            vesselType = vessel.VesselType,
                            destination = vessel.Destination,
                            imo = vessel.Imo,
                            callsign = vessel.Callsign,
                            shipTypeCode = vessel.ShipTypeCode,
                            length = vessel.Length,
                            beam = vessel.Beam,
                            draught = vessel.Draught,
                        }),
                    };
                }

                // Advance to next waypoint (loop)
                s.WaypointIndex = (s.WaypointIndex + 1) % vessel.Waypoints.Count;

                // Stagger emissions: small random delay between each vessel
                if (i < _vessels.Count - 1)
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
