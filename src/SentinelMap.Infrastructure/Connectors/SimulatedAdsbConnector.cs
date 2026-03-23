using System.Runtime.CompilerServices;
using System.Text.Json;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;

namespace SentinelMap.Infrastructure.Connectors;

/// <summary>
/// Yields synthetic ADS-B observations for aircraft operating over the Liverpool/Mersey area.
/// Includes inbound/outbound commercial jets at EGGP (Liverpool John Lennon Airport),
/// transiting airliners at altitude, general aviation, and emergency service helicopters.
/// Uses realistic waypoints, altitude profiles, and position jitter.
/// Requires no API keys — enables demo and offline development.
/// </summary>
public class SimulatedAdsbConnector : ISourceConnector
{
    private readonly int _baseIntervalMs;
    private readonly Random _rng = new();

    public SimulatedAdsbConnector(int updateIntervalMs = 2000)
    {
        _baseIntervalMs = updateIntervalMs;
    }

    public string SourceId => "simulated-adsb";
    public string SourceType => "ADSB";

    private enum AircraftBehaviour { Transit, Looping, Holding }

    private record SimAircraft(
        string IcaoHex,
        string Callsign,
        string AircraftType,
        double SpeedKnots,
        AircraftBehaviour Behaviour,
        string Squawk,
        (double Lon, double Lat, double AltFt)[] Waypoints);

    // ── Aircraft definitions ──────────────────────────────────────────────
    // Waypoints cover Liverpool John Lennon Airport (EGGP, lat ~53.33, lon ~-2.85),
    // Liverpool Bay, Hawarden Airport (EGNR), and surrounding airspace.
    private static readonly SimAircraft[] Aircraft =
    [
        // ── Commercial — approach to EGGP ────────────────────────────────
        // Ryanair 737 inbound from SE (Warrington corridor → EGGP final)
        new("4CA123", "RYR1234", "B738", 180, AircraftBehaviour.Transit, "7421",
        [
            (-2.58, 53.43, 10000),   // Overhead Warrington
            (-2.63, 53.40,  8000),   // Descending SE of airport
            (-2.68, 53.37,  6000),   // Heading towards ILS
            (-2.73, 53.35,  4000),   // Extended centreline
            (-2.78, 53.34,  2500),   // Final approach
            (-2.82, 53.335, 1500),   // Short final
            (-2.85, 53.333,  500),   // Threshold EGGP
        ]),

        // ── Commercial — departure from EGGP ─────────────────────────────
        // easyJet A320 departing south, climbing to FL250
        new("406A2F", "EZY5678", "A320", 250, AircraftBehaviour.Transit, "1234",
        [
            (-2.85, 53.333,   500),  // Runway EGGP
            (-2.87, 53.320,  3000),  // Initial climb south
            (-2.90, 53.300,  6000),  // Climbing through Cheshire
            (-2.92, 53.270, 10000),  // Passing FL100
            (-2.95, 53.230, 15000),  // Enroute climb
            (-2.98, 53.190, 20000),  // High climb
            (-3.00, 53.150, 25000),  // Cruise level FL250
        ]),

        // ── Commercial — transiting at altitude ───────────────────────────
        // British Airways A320 crossing Liverpool Bay NW→SE at FL350
        new("407C52", "BAW456", "A320", 450, AircraftBehaviour.Transit, "2000",
        [
            (-3.60, 53.65, 35000),   // NW over Irish Sea
            (-3.40, 53.58, 35000),   // Entering Liverpool Bay
            (-3.20, 53.52, 35000),   // Mid-bay
            (-3.00, 53.46, 35000),   // Inland NW England
            (-2.80, 53.40, 35000),   // Over Cheshire
            (-2.60, 53.34, 35000),   // SE transit
        ]),

        // Cargolux 747 transiting NE→SW at FL380
        new("4D0221", "CLX789", "B744", 460, AircraftBehaviour.Transit, "2000",
        [
            (-2.50, 53.70, 38000),   // NE entry (Lancashire)
            (-2.70, 53.60, 38000),   // Passing north of Liverpool
            (-2.90, 53.50, 38000),   // Over Liverpool Bay coast
            (-3.10, 53.42, 38000),   // Liverpool Bay
            (-3.30, 53.35, 38000),   // SW transit
            (-3.55, 53.25, 38000),   // Open Irish Sea
        ]),

        // ── General aviation ──────────────────────────────────────────────
        // Private Cessna 172 flying VFR circuit near Hawarden (EGNR)
        new("407D8A", "GBXYZ", "C172", 100, AircraftBehaviour.Looping, "7000",
        [
            (-2.98, 53.178, 2000),   // Hawarden EGNR threshold
            (-2.96, 53.182, 2000),   // Crosswind leg
            (-2.95, 53.192, 2000),   // Downwind leg
            (-2.97, 53.196, 2000),   // Base leg
            (-2.99, 53.190, 2000),   // Final
            (-2.98, 53.178, 2000),   // Touch and go
        ]),

        // ── Emergency services — helicopters ─────────────────────────────
        // Police helicopter (NPAS) loitering over Liverpool docks area
        new("43C6E1", "NPAS21", "EC35", 80, AircraftBehaviour.Holding, "0000",
        [
            (-2.990, 53.405, 1000),  // Liverpool docks N
            (-2.985, 53.400, 1000),  // Swinging east
            (-2.990, 53.395, 1000),  // South of docks
            (-2.995, 53.400, 1000),  // Swinging west
            (-2.990, 53.405, 1000),  // Back north
        ]),

        // HM Coastguard helicopter search pattern over Liverpool Bay
        new("43C001", "COASTGD", "AW189", 110, AircraftBehaviour.Holding, "0022",
        [
            (-3.15, 53.450,  500),   // Search box NE
            (-3.20, 53.450,  500),   // Search box NW
            (-3.20, 53.430,  500),   // Search box SW
            (-3.15, 53.430,  500),   // Search box SE
            (-3.15, 53.450,  500),   // Back to start
        ]),

        // RAF C-130 Hercules transiting north from RAF Woodvale area
        new("43C501", "RRR4501", "C130", 280, AircraftBehaviour.Transit, "1234",
        [
            (-3.05, 53.580, 15000),  // Off Woodvale (EGNO)
            (-3.08, 53.620, 15000),  // North over Southport
            (-3.10, 53.670, 15000),  // Climbing north
            (-3.12, 53.720, 15000),  // Open sea NW
            (-3.15, 53.780, 15000),  // Further north
        ]),
    ];

    public async IAsyncEnumerable<Observation> StreamAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var state = new AircraftState[Aircraft.Length];
        for (int i = 0; i < Aircraft.Length; i++)
        {
            // Stagger start positions so aircraft aren't all at waypoint 0
            state[i] = new AircraftState
            {
                Position = _rng.NextDouble() * (Aircraft[i].Waypoints.Length - 1),
                HoldingCentreLon = Aircraft[i].Waypoints[0].Lon,
                HoldingCentreLat = Aircraft[i].Waypoints[0].Lat,
                HoldingHeading = _rng.NextDouble() * 360.0,
            };
        }

        while (!ct.IsCancellationRequested)
        {
            for (int i = 0; i < Aircraft.Length; i++)
            {
                if (ct.IsCancellationRequested) yield break;

                var aircraft = Aircraft[i];
                var s = state[i];
                double lon, lat, heading, speedMps, altitudeFt;

                switch (aircraft.Behaviour)
                {
                    case AircraftBehaviour.Holding:
                        AdvancePosition(aircraft, s);
                        (lon, lat, heading, altitudeFt) = InterpolateWithAlt(aircraft.Waypoints, s.Position);
                        (lon, lat) = ApplyJitter(lon, lat, 0.0002); // ~22m jitter
                        altitudeFt += (_rng.NextDouble() - 0.5) * 200; // ±100ft helicopter jitter
                        speedMps = aircraft.SpeedKnots * 0.514444 * (0.85 + _rng.NextDouble() * 0.3);
                        break;

                    case AircraftBehaviour.Looping:
                        AdvancePosition(aircraft, s);
                        (lon, lat, heading, altitudeFt) = InterpolateWithAlt(aircraft.Waypoints, s.Position);
                        (lon, lat) = ApplyJitter(lon, lat, 0.0001); // ~11m jitter
                        altitudeFt += (_rng.NextDouble() - 0.5) * 200; // ±100ft circuit jitter
                        speedMps = aircraft.SpeedKnots * 0.514444 * (0.85 + _rng.NextDouble() * 0.3);
                        break;

                    default: // Transit
                        AdvancePosition(aircraft, s);
                        (lon, lat, heading, altitudeFt) = InterpolateWithAlt(aircraft.Waypoints, s.Position);
                        (lon, lat) = ApplyJitter(lon, lat, 0.00005); // ~5.5m jitter
                        // For high-altitude transits, add slight random altitude variation ±200ft
                        if (altitudeFt > 10000)
                            altitudeFt += (_rng.NextDouble() - 0.5) * 400;
                        speedMps = aircraft.SpeedKnots * 0.514444 * (0.9 + _rng.NextDouble() * 0.2);
                        break;
                }

                yield return new Observation
                {
                    SourceType = "ADSB",
                    ExternalId = aircraft.IcaoHex,
                    Position = new Point(lon, lat) { SRID = 4326 },
                    Heading = heading,
                    SpeedMps = speedMps,
                    ObservedAt = DateTimeOffset.UtcNow,
                    RawData = JsonSerializer.Serialize(new
                    {
                        displayName = aircraft.Callsign,
                        aircraftType = aircraft.AircraftType,
                        altitude = (int)Math.Max(0, altitudeFt),
                        squawk = aircraft.Squawk,
                    }),
                };

                // Stagger emissions: small random delay between each aircraft
                if (i < Aircraft.Length - 1)
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

    // ── Position advancement ────────────────────────────────────────────

    private void AdvancePosition(SimAircraft aircraft, AircraftState s)
    {
        var step = SpeedToStepSize(aircraft.SpeedKnots);

        if (aircraft.Behaviour is AircraftBehaviour.Looping or AircraftBehaviour.Holding)
        {
            // Loop: wrap around to start
            s.Position = (s.Position + step) % aircraft.Waypoints.Length;
        }
        else
        {
            // Transit: bounce back and forth
            if (s.Forward)
            {
                s.Position += step;
                if (s.Position >= aircraft.Waypoints.Length - 1)
                {
                    s.Position = aircraft.Waypoints.Length - 1;
                    s.Forward = false;
                    // Pause at each end (simulate holding / turnaround)
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

    // ── Interpolation between waypoints (with altitude) ─────────────────

    private static (double lon, double lat, double heading, double altFt) InterpolateWithAlt(
        (double Lon, double Lat, double AltFt)[] waypoints, double position)
    {
        var n = waypoints.Length;
        var idxA = Math.Clamp((int)position, 0, n - 1);
        var idxB = Math.Clamp(idxA + 1, 0, n - 1);
        var t = position - Math.Floor(position);

        if (idxA == idxB)
            return (waypoints[idxA].Lon, waypoints[idxA].Lat, 0, waypoints[idxA].AltFt);

        var a = waypoints[idxA];
        var b = waypoints[idxB];

        var lon = a.Lon + (b.Lon - a.Lon) * t;
        var lat = a.Lat + (b.Lat - a.Lat) * t;
        var altFt = a.AltFt + (b.AltFt - a.AltFt) * t;

        // True bearing calculation
        var dLon = (b.Lon - a.Lon) * Math.PI / 180.0;
        var lat1 = a.Lat * Math.PI / 180.0;
        var lat2 = b.Lat * Math.PI / 180.0;
        var y = Math.Sin(dLon) * Math.Cos(lat2);
        var x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
        var heading = (Math.Atan2(y, x) * 180.0 / Math.PI + 360.0) % 360.0;

        return (lon, lat, heading, altFt);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private (double lon, double lat) ApplyJitter(double lon, double lat, double magnitude)
    {
        lon += (_rng.NextDouble() - 0.5) * magnitude;
        lat += (_rng.NextDouble() - 0.5) * magnitude;
        return (lon, lat);
    }

    private static double SpeedToStepSize(double knots) => knots * 0.001;

    private class AircraftState
    {
        public double Position;
        public bool Forward = true;
        public int PauseTicksRemaining;
        public double HoldingCentreLon;
        public double HoldingCentreLat;
        public double HoldingHeading;
    }
}
