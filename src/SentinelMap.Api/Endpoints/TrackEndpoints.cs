using Microsoft.EntityFrameworkCore;
using SentinelMap.Infrastructure.Data;

namespace SentinelMap.Api.Endpoints;

public static class TrackEndpoints
{
    public static void MapTrackEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/entities").WithTags("Tracks");

        group.MapGet("/{id:guid}/track", GetTrackHistory)
            .RequireAuthorization("ViewerAccess");
    }

    private static async Task<IResult> GetTrackHistory(
        Guid id,
        SystemDbContext db,
        CancellationToken ct,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int limit = 1000)
    {
        var now = DateTimeOffset.UtcNow;
        var fromTime = from ?? now.AddHours(-1);
        var toTime = to ?? now;

        if (limit < 1) limit = 1;
        if (limit > 5000) limit = 5000;

        var observations = await db.Observations
            .Where(o => o.EntityId == id
                && o.ObservedAt >= fromTime
                && o.ObservedAt <= toTime)
            .OrderBy(o => o.ObservedAt)
            .Take(limit)
            .Select(o => new
            {
                longitude = o.Position!.X,
                latitude = o.Position!.Y,
                heading = o.Heading,
                speedKnots = o.SpeedMps.HasValue ? o.SpeedMps.Value * 1.94384 : (double?)null,
                observedAt = o.ObservedAt,
            })
            .ToListAsync(ct);

        var totalCount = await db.Observations
            .CountAsync(o => o.EntityId == id
                && o.ObservedAt >= fromTime
                && o.ObservedAt <= toTime, ct);

        return Results.Ok(new
        {
            entityId = id,
            positions = observations,
            totalCount,
        });
    }
}
