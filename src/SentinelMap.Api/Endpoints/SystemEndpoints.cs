using Microsoft.EntityFrameworkCore;
using SentinelMap.Infrastructure.Data;
using SentinelMap.SharedKernel.Enums;

namespace SentinelMap.Api.Endpoints;

public static class SystemEndpoints
{
    public static void MapSystemEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/system").WithTags("System");

        group.MapGet("/status", GetStatus).RequireAuthorization("ViewerAccess");
    }

    private static async Task<IResult> GetStatus(
        SystemDbContext db,
        CancellationToken ct)
    {
        var vesselCount = await db.Entities
            .CountAsync(e => e.Type == EntityType.Vessel && e.Status == EntityStatus.Active, ct);

        var aircraftCount = await db.Entities
            .CountAsync(e => e.Type == EntityType.Aircraft && e.Status == EntityStatus.Active, ct);

        var darkVesselCount = await db.Entities
            .CountAsync(e => e.Type == EntityType.Vessel && e.Status == EntityStatus.Dark, ct);

        var activeAlertCount = await db.Alerts
            .CountAsync(a => a.Status == AlertStatus.Triggered, ct);

        var acknowledgedAlertCount = await db.Alerts
            .CountAsync(a => a.Status == AlertStatus.Acknowledged, ct);

        var geofenceCount = await db.Geofences
            .CountAsync(g => g.IsActive, ct);

        var watchlistCount = await db.Watchlists
            .CountAsync(ct);

        var userCount = await db.DomainUsers
            .CountAsync(u => u.IsActive, ct);

        var uptime = Environment.TickCount64;
        var uptimeSpan = TimeSpan.FromMilliseconds(uptime);

        return Results.Ok(new
        {
            status = "OK",
            timestamp = DateTimeOffset.UtcNow,
            uptimeHours = Math.Round(uptimeSpan.TotalHours, 2),
            entities = new
            {
                activeVessels = vesselCount,
                activeAircraft = aircraftCount,
                darkVessels = darkVesselCount,
                total = vesselCount + aircraftCount + darkVesselCount
            },
            alerts = new
            {
                triggered = activeAlertCount,
                acknowledged = acknowledgedAlertCount
            },
            geofences = new
            {
                active = geofenceCount
            },
            watchlists = new
            {
                total = watchlistCount
            },
            users = new
            {
                active = userCount
            }
        });
    }
}
