using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Infrastructure.Data;

namespace SentinelMap.Api.Services;

public static class StaticFeatureSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        // Seed airspace zones (has its own idempotency check)
        await SeedAirspaceZonesAsync(db);

        // Skip map features if static ones already exist
        if (await db.MapFeatures.AnyAsync(f => f.Source == "static")) return;

        var features = new List<MapFeature>();

        // --- UK Airports ---
        var airports = new (string Name, string Code, double Lon, double Lat)[]
        {
            ("Liverpool John Lennon", "EGGP", -2.8497, 53.3336),
            ("Manchester", "EGCC", -2.2750, 53.3537),
            ("Hawarden", "EGNR", -2.9778, 53.1781),
            ("Blackpool", "EGNH", -3.0286, 53.7717),
            ("Leeds Bradford", "EGNM", -1.6606, 53.8659),
            ("Isle of Man", "EGNS", -4.6239, 54.0833),
            ("Belfast City", "EGAC", -5.8725, 54.6181),
            ("Belfast International", "EGAA", -6.2158, 54.6575),
            ("Dublin", "EIDW", -6.2701, 53.4213),
            ("London Heathrow", "EGLL", -0.4614, 51.4700),
            ("London Gatwick", "EGKK", -0.1903, 51.1481),
            ("Birmingham", "EGBB", -1.7480, 52.4539),
            ("Edinburgh", "EGPH", -3.3725, 55.9500),
            ("Glasgow", "EGPF", -4.4331, 55.8719),
            ("Cardiff", "EGFF", -3.3433, 51.3967),
            ("Bristol", "EGGD", -2.7191, 51.3827),
            ("East Midlands", "EGNX", -1.3283, 52.8311),
            ("Newcastle", "EGNT", -1.6917, 55.0372),
            ("Aberdeen", "EGPD", -2.1978, 57.2019),
            ("Inverness", "EGPE", -4.0475, 57.5425),
        };

        foreach (var (name, code, lon, lat) in airports)
        {
            features.Add(new MapFeature
            {
                FeatureType = "Airport",
                Name = $"{name} ({code})",
                Position = new Point(lon, lat) { SRID = 4326 },
                Icon = "airport",
                Color = "#f97316",
                Details = System.Text.Json.JsonSerializer.Serialize(new { icao = code }),
                Source = "static",
                IsActive = true,
            });
        }

        // --- UK Military Bases ---
        var military = new (string Name, double Lon, double Lat)[]
        {
            ("RAF Woodvale", -3.0556, 53.5814),
            ("RAF Valley", -4.5353, 53.2481),
            ("HMNB Clyde (Faslane)", -4.8186, 56.0667),
            ("Cammell Laird Shipyard", -3.0167, 53.3750),
            ("BAE Systems Barrow", -3.2264, 54.1244),
            ("RAF Lossiemouth", -3.3439, 57.7053),
            ("RAF Coningsby", -0.1664, 53.0931),
            ("RNAS Culdrose", -5.2558, 50.0861),
            ("HMNB Portsmouth", -1.1081, 50.7989),
            ("HMNB Devonport", -4.1872, 50.3800),
            ("RAF Brize Norton", -1.5836, 51.7500),
            ("RAF Lakenheath", 0.5608, 52.4094),
            ("RAF Mildenhall", 0.4864, 52.3611),
            ("Aldermaston AWE", -1.1667, 51.3667),
            ("MOD Boscombe Down", -1.7481, 51.1522),
        };

        foreach (var (name, lon, lat) in military)
        {
            features.Add(new MapFeature
            {
                FeatureType = "MilitaryBase",
                Name = name,
                Position = new Point(lon, lat) { SRID = 4326 },
                Icon = "military",
                Color = "#ef4444",
                Details = null,
                Source = "static",
                IsActive = true,
            });
        }

        db.MapFeatures.AddRange(features);
        await db.SaveChangesAsync();

        var logger = scope.ServiceProvider.GetRequiredService<ILogger<MapFeature>>();
        logger.LogInformation("Static features seeded: {AirportCount} airports, {MilitaryCount} military bases",
            airports.Length, military.Length);
    }

    private static async Task SeedAirspaceZonesAsync(SystemDbContext db)
    {
        // Skip if airspace geofences already exist
        if (await db.Geofences.AnyAsync(g => g.FenceType.StartsWith("Airspace-"))) return;

        var factory = new GeometryFactory(new PrecisionModel(), 4326);
        var zones = new List<Geofence>();

        var airspaceData = new (string Name, string FenceType, string Color, (double Lon, double Lat)[] Coords)[]
        {
            ("LIVERPOOL CTR (CLASS D)", "Airspace-CTR", "#3b82f6", new[]
            {
                (-3.15, 53.28), (-2.55, 53.28), (-2.55, 53.45), (-3.15, 53.45), (-3.15, 53.28)
            }),
            ("MANCHESTER TMA (CLASS A)", "Airspace-CTR", "#3b82f6", new[]
            {
                (-2.55, 53.28), (-1.95, 53.28), (-1.95, 53.55), (-2.55, 53.55), (-2.55, 53.28)
            }),
            ("ATZ HAWARDEN", "Airspace-ATZ", "#60a5fa", new[]
            {
                (-3.05, 53.15), (-2.90, 53.15), (-2.90, 53.21), (-3.05, 53.21), (-3.05, 53.15)
            }),
            ("MATZ WOODVALE", "Airspace-MATZ", "#f97316", new[]
            {
                (-3.12, 53.54), (-2.99, 53.54), (-2.99, 53.62), (-3.12, 53.62), (-3.12, 53.54)
            }),
            ("D307 ALTCAR RANGES", "Airspace-Danger", "#ef4444", new[]
            {
                (-3.12, 53.50), (-3.02, 53.50), (-3.02, 53.56), (-3.12, 53.56), (-3.12, 53.50)
            }),
            ("D301 IRISH SEA", "Airspace-Danger", "#ef4444", new[]
            {
                (-4.50, 53.20), (-3.50, 53.20), (-3.50, 53.80), (-4.50, 53.80), (-4.50, 53.20)
            }),
            ("D406 BARROW/MORECAMBE BAY", "Airspace-Danger", "#ef4444", new[]
            {
                (-3.50, 53.90), (-2.80, 53.90), (-2.80, 54.20), (-3.50, 54.20), (-3.50, 53.90)
            }),
            ("P131 SELLAFIELD", "Airspace-Prohibited", "#dc2626", new[]
            {
                (-3.55, 54.38), (-3.40, 54.38), (-3.40, 54.45), (-3.55, 54.45), (-3.55, 54.38)
            }),
        };

        foreach (var (name, fenceType, color, coords) in airspaceData)
        {
            var ring = new LinearRing(
                coords.Select(c => new Coordinate(c.Lon, c.Lat)).ToArray());
            var polygon = factory.CreatePolygon(ring);

            zones.Add(new Geofence
            {
                Name = name,
                Geometry = polygon,
                FenceType = fenceType,
                Color = color,
                CreatedBy = Guid.Empty,
                IsActive = true,
            });
        }

        db.Geofences.AddRange(zones);
        await db.SaveChangesAsync();
    }
}
