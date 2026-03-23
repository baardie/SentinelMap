using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Infrastructure.Data;
using SentinelMap.SharedKernel.Enums;

namespace SentinelMap.Api.Services;

public static class DemoSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        // Skip if demo data already exists
        if (await db.Geofences.AnyAsync()) return;

        // 1. Create demo geofence covering The Narrows / Seaforth approach
        // This area is transited by MERSEY TRADER and ATLANTIC BULKER
        var coordinates = new[]
        {
            new Coordinate(-3.12, 53.43),
            new Coordinate(-2.98, 53.43),
            new Coordinate(-2.98, 53.47),
            new Coordinate(-3.12, 53.47),
            new Coordinate(-3.12, 53.43), // close ring
        };

        var geofence = new Geofence
        {
            Name = "Liverpool Bay Restricted Zone",
            Geometry = new Polygon(new LinearRing(coordinates)) { SRID = 4326 },
            FenceType = "Both",
            CreatedBy = Guid.Empty, // System-created
        };
        db.Geofences.Add(geofence);

        // 2. Create demo watchlist with MERSEY TRADER (MMSI 235009888)
        var watchlist = new Watchlist
        {
            Name = "Vessels of Interest",
            Description = "Demo watchlist for golden-path scenario",
            CreatedBy = Guid.Empty,
        };
        db.Watchlists.Add(watchlist);
        await db.SaveChangesAsync();

        var entry = new WatchlistEntry
        {
            WatchlistId = watchlist.Id,
            IdentifierType = "MMSI",
            IdentifierValue = "235009888", // MERSEY TRADER
            Reason = "Under investigation — suspicious cargo manifest",
            Severity = AlertSeverity.Critical,
            AddedBy = Guid.Empty,
        };
        db.WatchlistEntries.Add(entry);
        await db.SaveChangesAsync();

        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Geofence>>();
        logger.LogInformation("Demo data seeded: geofence '{Name}', watchlist '{WatchlistName}' with 1 entry",
            geofence.Name, watchlist.Name);
    }
}
