using System.Runtime.CompilerServices;
using System.Text.Json;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;

namespace SentinelMap.Infrastructure.Connectors;

/// <summary>
/// Yields synthetic AIS observations for vessels moving through the River Mersey,
/// Liverpool Bay, and surrounding waters. Uses realistic channel waypoints,
/// varied vessel types, speed profiles, and position jitter.
/// Requires no API keys — enables demo and offline development.
/// </summary>
public class SimulatedAisConnector : ISourceConnector
{
    private readonly int _baseIntervalMs;
    private readonly Random _rng = new();

    public SimulatedAisConnector(int updateIntervalMs = 2000)
    {
        _baseIntervalMs = updateIntervalMs;
    }

    public string SourceId => "simulated-ais";
    public string SourceType => "AIS";

    private enum VesselBehaviour { Transit, Looping, Anchored }

    private record SimVessel(
        string Mmsi,
        string Name,
        string VesselType,
        double SpeedKnots,
        VesselBehaviour Behaviour,
        (double Lon, double Lat)[] Waypoints);

    // ── Vessel definitions ─────────────────────────────────────────────
    // Waypoints follow real navigation channels: Queen's Channel, Crosby Channel,
    // The Narrows (New Brighton ↔ Seaforth), and upriver past Birkenhead.
    private static readonly SimVessel[] Vessels =
    [
        // ── Large transiting vessels ───────────────────────────────────
        // Container ship inbound: Liverpool Bay → Queen's Channel → Crosby Channel → Seaforth
        new("235009888", "MERSEY TRADER", "Cargo", 10, VesselBehaviour.Transit,
        [
            (-3.35, 53.53),   // Liverpool Bay approach
            (-3.28, 53.51),   // Bar Light buoy area
            (-3.22, 53.495),  // Queen's Channel entrance
            (-3.18, 53.485),  // Mid Queen's Channel
            (-3.14, 53.475),  // Formby Channel junction
            (-3.10, 53.465),  // Crosby Channel entrance
            (-3.07, 53.455),  // Mid Crosby Channel
            (-3.05, 53.448),  // C1 buoy area
            (-3.035, 53.442), // Approaching The Narrows
            (-3.02, 53.438),  // New Brighton / Perch Rock abeam
            (-3.01, 53.435),  // The Narrows
            (-3.005, 53.432), // Seaforth approach
            (-2.995, 53.432), // Seaforth container terminal berth
        ]),

        // Tanker outbound: Tranmere Oil Terminal → downriver → Liverpool Bay
        new("244670316", "NORDIC COURAGE", "Tanker", 8, VesselBehaviour.Transit,
        [
            (-2.975, 53.355), // Tranmere Oil Terminal
            (-2.985, 53.360), // Departing berth
            (-2.995, 53.370), // Mid-river off Rock Ferry
            (-3.005, 53.385), // Off Woodside
            (-3.015, 53.400), // Off Pier Head
            (-3.025, 53.420), // Alfred Dock passage
            (-3.02, 53.438),  // The Narrows
            (-3.035, 53.442), // Exiting Narrows
            (-3.05, 53.448),  // C1 buoy
            (-3.07, 53.455),  // Crosby Channel
            (-3.10, 53.465),  // Crosby Channel exit
            (-3.18, 53.485),  // Queen's Channel
            (-3.28, 53.51),   // Bar Light
            (-3.40, 53.54),   // Open sea
        ]),

        // Bulk carrier inbound from the north: approaching from off Formby Point
        new("235101234", "ATLANTIC BULKER", "Cargo", 9, VesselBehaviour.Transit,
        [
            (-3.15, 53.53),   // Off Formby Point
            (-3.12, 53.50),   // Approaching Crosby
            (-3.10, 53.485),  // Formby Channel
            (-3.08, 53.470),  // Mid-channel
            (-3.06, 53.455),  // Crosby Channel
            (-3.04, 53.445),  // Approaching Narrows
            (-3.025, 53.440), // New Brighton abeam
            (-3.015, 53.435), // Through Narrows
            (-3.005, 53.432), // Seaforth
            (-2.998, 53.430), // Royal Seaforth Dock entrance
        ]),

        // ── Ferries (higher speed, regular routes) ────────────────────
        // Isle of Man fast ferry: Liverpool landing stage → out to sea
        new("235085678", "MANANNAN", "HSC", 30, VesselBehaviour.Transit,
        [
            (-2.995, 53.402), // Liverpool Pier Head / landing stage
            (-3.005, 53.410), // Departing, heading north
            (-3.015, 53.425), // Off Princes Dock
            (-3.025, 53.438), // The Narrows
            (-3.04, 53.445),  // Exiting channel
            (-3.08, 53.460),  // Crosby Channel (accelerating)
            (-3.15, 53.490),  // Open water, full speed
            (-3.25, 53.520),  // Liverpool Bay
            (-3.45, 53.560),  // Heading NW to Isle of Man
            (-3.65, 53.600),  // Open Irish Sea
        ]),

        // Birkenhead / Woodside ferry: short cross-river shuttle
        new("235042100", "ROYAL IRIS", "Passenger", 8, VesselBehaviour.Looping,
        [
            (-2.995, 53.402), // Liverpool Pier Head
            (-2.998, 53.400), // Mid-river
            (-3.005, 53.396), // Mid-river
            (-3.012, 53.393), // Approaching Woodside
            (-3.015, 53.391), // Woodside terminal
            (-3.012, 53.393), // Departing Woodside
            (-3.005, 53.396), // Mid-river return
            (-2.998, 53.400), // Mid-river
        ]),

        // ── Working vessels (lower speed, tight patterns) ─────────────
        // Pilot launch operating at the Bar
        new("235055001", "DORADO", "Pilot", 12, VesselBehaviour.Looping,
        [
            (-3.22, 53.500),  // Station near Bar Light
            (-3.25, 53.505),  // Patrol north
            (-3.28, 53.510),  // Approaching inbound vessel
            (-3.25, 53.508),  // Running alongside
            (-3.22, 53.503),  // Returning to station
            (-3.20, 53.498),  // Patrol south
            (-3.22, 53.495),  // Loop back
        ]),

        // Tug manoeuvring near Cammell Laird shipyard
        new("235067890", "SVITZER BIDSTON", "Tug", 4, VesselBehaviour.Looping,
        [
            (-3.015, 53.375), // Off Cammell Laird dry dock
            (-3.010, 53.378), // Moving east
            (-3.005, 53.380), // Mid-river
            (-3.000, 53.378), // Crossing to Liverpool side
            (-3.005, 53.375), // Returning
            (-3.010, 53.372), // South of shipyard
            (-3.015, 53.370), // Off Monks Ferry
            (-3.018, 53.373), // Back towards shipyard
        ]),

        // Dredger working in the Crosby Channel
        new("235033456", "UMD DOLPHIN", "Dredger", 2, VesselBehaviour.Looping,
        [
            (-3.08, 53.460),  // Crosby Channel east
            (-3.085, 53.462), // Working pattern north
            (-3.09, 53.460),  // Working pattern west
            (-3.085, 53.458), // Working pattern south
            (-3.08, 53.456),  // Slight south shift
            (-3.085, 53.458), // Return
        ]),

        // ── Anchored / stationary vessels ─────────────────────────────
        // Cargo vessel at anchor in the Mersey anchorage (slight swing on anchor)
        new("636092345", "PACIFIC HARMONY", "Cargo", 0, VesselBehaviour.Anchored,
        [
            (-3.12, 53.475),  // Anchor position (centre of swing circle)
        ]),

        // Small tanker at anchor waiting for berth at Tranmere
        new("538006789", "STENA CONQUEST", "Tanker", 0, VesselBehaviour.Anchored,
        [
            (-3.04, 53.420),  // Anchor position off New Brighton
        ]),

        // Fishing vessel working off Formby
        new("235019876", "LADY ANNE", "Fishing", 4, VesselBehaviour.Looping,
        [
            (-3.14, 53.520),  // Off Formby Point
            (-3.16, 53.525),  // Trawl run NW
            (-3.19, 53.530),  // End of run
            (-3.17, 53.528),  // Turning
            (-3.15, 53.523),  // Return SE
            (-3.13, 53.518),  // South end
            (-3.12, 53.515),  // Loop
            (-3.13, 53.518),  // Back north
        ]),
    ];

    public async IAsyncEnumerable<Observation> StreamAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var state = new VesselState[Vessels.Length];
        for (int i = 0; i < Vessels.Length; i++)
        {
            // Stagger start positions so vessels aren't all at waypoint 0
            state[i] = new VesselState
            {
                Position = _rng.NextDouble() * (Vessels[i].Waypoints.Length - 1),
                AnchorCentreLon = Vessels[i].Waypoints[0].Lon,
                AnchorCentreLat = Vessels[i].Waypoints[0].Lat,
                AnchorHeading = _rng.NextDouble() * 360.0,
            };
        }

        while (!ct.IsCancellationRequested)
        {
            for (int i = 0; i < Vessels.Length; i++)
            {
                if (ct.IsCancellationRequested) yield break;

                var vessel = Vessels[i];
                var s = state[i];
                double lon, lat, heading, speedMps;

                switch (vessel.Behaviour)
                {
                    case VesselBehaviour.Anchored:
                        (lon, lat, heading) = SimulateAnchorSwing(s);
                        speedMps = 0.1 + _rng.NextDouble() * 0.2; // 0.1-0.3 m/s drift
                        break;

                    case VesselBehaviour.Looping:
                        AdvancePosition(vessel, s);
                        (lon, lat, heading) = Interpolate(vessel.Waypoints, s.Position);
                        (lon, lat) = ApplyJitter(lon, lat, 0.0001); // ~11m jitter
                        speedMps = vessel.SpeedKnots * 0.514444 * (0.85 + _rng.NextDouble() * 0.3);
                        break;

                    default: // Transit
                        AdvancePosition(vessel, s);
                        (lon, lat, heading) = Interpolate(vessel.Waypoints, s.Position);
                        (lon, lat) = ApplyJitter(lon, lat, 0.00005); // ~5.5m jitter
                        speedMps = vessel.SpeedKnots * 0.514444 * (0.9 + _rng.NextDouble() * 0.2);
                        break;
                }

                // ── AIS dark period (MERSEY TRADER only, index 0) ──────────────
                // When MERSEY TRADER approaches the end of its inbound transit
                // (Seaforth terminal), it goes dark for ~17 ticks (~35s at 2s interval).
                // The vessel reappears once the dark period expires.
                bool suppressObservation = false;
                if (i == 0 && vessel.Behaviour == VesselBehaviour.Transit)
                {
                    if (s.DarkTicksRemaining > 0)
                    {
                        // Currently dark — skip emission and count down
                        s.DarkTicksRemaining--;
                        suppressObservation = true;
                    }
                    else if (!s.HasGoneDark && s.Position >= vessel.Waypoints.Length - 1.5)
                    {
                        // Vessel has reached Seaforth — trigger dark period
                        s.DarkTicksRemaining = 17;
                        s.HasGoneDark = true;
                        suppressObservation = true;
                    }

                    // Reset the flag when the vessel starts a new forward pass
                    if (s.Forward && s.Position < 1.0)
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
                            behaviour = vessel.Behaviour.ToString(),
                        }),
                    };
                }

                // Stagger emissions: small random delay between each vessel
                if (i < Vessels.Length - 1)
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

    // ── Position advancement ───────────────────────────────────────────

    private void AdvancePosition(SimVessel vessel, VesselState s)
    {
        var step = SpeedToStepSize(vessel.SpeedKnots);

        if (vessel.Behaviour == VesselBehaviour.Looping)
        {
            // Loop: wrap around to start
            s.Position = (s.Position + step) % vessel.Waypoints.Length;
        }
        else
        {
            // Transit: bounce back and forth
            if (s.Forward)
            {
                s.Position += step;
                if (s.Position >= vessel.Waypoints.Length - 1)
                {
                    s.Position = vessel.Waypoints.Length - 1;
                    s.Forward = false;
                    // Pause at each end (simulate port operations)
                    s.PauseTicksRemaining = 15 + _rng.Next(30);
                }
            }
            else
            {
                if (s.PauseTicksRemaining > 0)
                {
                    s.PauseTicksRemaining--;
                    return;
                }
                s.Position -= step;
                if (s.Position <= 0)
                {
                    s.Position = 0;
                    s.Forward = true;
                    s.PauseTicksRemaining = 15 + _rng.Next(30);
                }
            }
        }
    }

    // ── Anchor swing simulation ────────────────────────────────────────

    private (double lon, double lat, double heading) SimulateAnchorSwing(VesselState s)
    {
        // Slow rotation around anchor point (tide/wind swing)
        s.AnchorHeading = (s.AnchorHeading + 0.3 + _rng.NextDouble() * 0.5) % 360.0;
        var radius = 0.0003 + _rng.NextDouble() * 0.0001; // ~30-40m swing radius
        var rad = s.AnchorHeading * Math.PI / 180.0;
        var lon = s.AnchorCentreLon + Math.Sin(rad) * radius;
        var lat = s.AnchorCentreLat + Math.Cos(rad) * radius;
        return (lon, lat, s.AnchorHeading);
    }

    // ── Interpolation between waypoints ────────────────────────────────

    private static (double lon, double lat, double heading) Interpolate(
        (double Lon, double Lat)[] waypoints, double position)
    {
        var n = waypoints.Length;
        var idxA = Math.Clamp((int)position, 0, n - 1);
        var idxB = Math.Clamp(idxA + 1, 0, n - 1);
        var t = position - Math.Floor(position);

        if (idxA == idxB) return (waypoints[idxA].Lon, waypoints[idxA].Lat, 0);

        var a = waypoints[idxA];
        var b = waypoints[idxB];

        var lon = a.Lon + (b.Lon - a.Lon) * t;
        var lat = a.Lat + (b.Lat - a.Lat) * t;

        // True bearing calculation
        var dLon = (b.Lon - a.Lon) * Math.PI / 180.0;
        var lat1 = a.Lat * Math.PI / 180.0;
        var lat2 = b.Lat * Math.PI / 180.0;
        var y = Math.Sin(dLon) * Math.Cos(lat2);
        var x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
        var heading = (Math.Atan2(y, x) * 180.0 / Math.PI + 360.0) % 360.0;

        return (lon, lat, heading);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private (double lon, double lat) ApplyJitter(double lon, double lat, double magnitude)
    {
        lon += (_rng.NextDouble() - 0.5) * magnitude;
        lat += (_rng.NextDouble() - 0.5) * magnitude;
        return (lon, lat);
    }

    private static double SpeedToStepSize(double knots) => knots * 0.001;

    private class VesselState
    {
        public double Position;
        public bool Forward = true;
        public int PauseTicksRemaining;
        public double AnchorCentreLon;
        public double AnchorCentreLat;
        public double AnchorHeading;
        public int DarkTicksRemaining;
        public bool HasGoneDark;
    }
}
