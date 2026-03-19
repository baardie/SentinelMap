using System.Runtime.CompilerServices;
using System.Text.Json;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;

namespace SentinelMap.Infrastructure.Connectors;

/// <summary>
/// Yields synthetic AIS observations for 4 vessels moving through the English Channel.
/// Requires no API keys — enables demo and offline development.
/// </summary>
public class SimulatedAisConnector : ISourceConnector
{
    private readonly int _updateIntervalMs;

    public SimulatedAisConnector(int updateIntervalMs = 2000)
    {
        _updateIntervalMs = updateIntervalMs;
    }

    public string SourceId => "simulated-ais";
    public string SourceType => "AIS";

    private record SimVessel(
        string Mmsi,
        string Name,
        string VesselType,
        double SpeedKnots,
        (double Lon, double Lat)[] Waypoints);

    private static readonly SimVessel[] Vessels =
    [
        new("235009888", "BRITANNIA STAR", "Cargo", 12,
            [(-3.5, 49.5), (-2.0, 50.0), (-0.5, 50.5), (1.0, 51.0), (2.5, 51.3)]),
        new("244670316", "ARCTIC DAWN", "Tanker", 8,
            [(-5.5, 50.2), (-4.0, 50.5), (-3.0, 51.0), (-3.5, 53.0), (-3.8, 53.4)]),
        new("227012345", "MARIE CELINE", "Passenger", 18,
            [(-1.8, 49.4), (-1.0, 49.8), (0.0, 50.2), (1.0, 50.7), (1.8, 51.0)]),
        new("338234567", "ATLAS VENTURE", "Cargo", 10,
            [(2.2, 51.5), (1.5, 51.3), (0.5, 51.0), (-0.5, 50.7), (-1.5, 50.4)]),
    ];

    public async IAsyncEnumerable<Observation> StreamAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var positions = new double[Vessels.Length];

        while (!ct.IsCancellationRequested)
        {
            for (int i = 0; i < Vessels.Length; i++)
            {
                if (ct.IsCancellationRequested) yield break;

                var vessel = Vessels[i];
                var step = SpeedToStepSize(vessel.SpeedKnots);
                positions[i] = (positions[i] + step) % vessel.Waypoints.Length;

                var (lon, lat, heading) = Interpolate(vessel.Waypoints, positions[i]);

                yield return new Observation
                {
                    SourceType = "AIS",
                    ExternalId = vessel.Mmsi,
                    Position = new Point(lon, lat) { SRID = 4326 },
                    Heading = heading,
                    SpeedMps = vessel.SpeedKnots * 0.514444,
                    ObservedAt = DateTimeOffset.UtcNow,
                    RawData = JsonSerializer.Serialize(new { displayName = vessel.Name, vesselType = vessel.VesselType }),
                };
            }

            try { await Task.Delay(_updateIntervalMs, ct); }
            catch (OperationCanceledException) { yield break; }
        }
    }

    private static (double lon, double lat, double heading) Interpolate(
        (double Lon, double Lat)[] waypoints, double position)
    {
        var n = waypoints.Length;
        var idxA = (int)position % n;
        var idxB = (idxA + 1) % n;
        var t = position - Math.Floor(position);

        var a = waypoints[idxA];
        var b = waypoints[idxB];

        var lon = a.Lon + (b.Lon - a.Lon) * t;
        var lat = a.Lat + (b.Lat - a.Lat) * t;

        var dLon = (b.Lon - a.Lon) * Math.PI / 180.0;
        var lat1 = a.Lat * Math.PI / 180.0;
        var lat2 = b.Lat * Math.PI / 180.0;
        var y = Math.Sin(dLon) * Math.Cos(lat2);
        var x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
        var heading = (Math.Atan2(y, x) * 180.0 / Math.PI + 360.0) % 360.0;

        return (lon, lat, heading);
    }

    private static double SpeedToStepSize(double knots) => knots * 0.001;
}
