using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;
using SentinelMap.SharedKernel.Interfaces;

namespace SentinelMap.Api.Endpoints;

public static class GeofenceEndpoints
{
    public static void MapGeofenceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/geofences").WithTags("Geofences");

        group.MapGet("/", GetAll).RequireAuthorization("ViewerAccess");
        group.MapPost("/", Create).RequireAuthorization("AnalystAccess");
        group.MapGet("/{id:guid}", GetById).RequireAuthorization("ViewerAccess");
        group.MapPut("/{id:guid}", Update).RequireAuthorization("AnalystAccess");
        group.MapDelete("/{id:guid}", Delete).RequireAuthorization("AnalystAccess");
    }

    private static async Task<IResult> GetAll(
        IGeofenceRepository repo,
        CancellationToken ct)
    {
        var geofences = await repo.GetAllActiveAsync(ct);
        var response = geofences.Select(ToResponse).ToList();
        return Results.Ok(response);
    }

    private static async Task<IResult> GetById(
        Guid id,
        IGeofenceRepository repo,
        CancellationToken ct)
    {
        var geofence = await repo.GetByIdAsync(id, ct);
        if (geofence is null) return Results.NotFound();
        return Results.Ok(ToResponse(geofence));
    }

    private static async Task<IResult> Create(
        CreateGeofenceRequest request,
        IGeofenceRepository repo,
        IUserContext userContext,
        CancellationToken ct)
    {
        if (request.Coordinates is null || request.Coordinates.Length < 3)
            return Results.BadRequest("At least 3 coordinates are required.");

        var coords = request.Coordinates.Select(c => new Coordinate(c[0], c[1])).ToList();
        // Close the ring if not already closed
        if (!coords[0].Equals(coords[^1]))
            coords.Add(coords[0]);

        var ring = new LinearRing(coords.ToArray());
        var polygon = new Polygon(ring) { SRID = 4326 };

        var geofence = new Geofence
        {
            Name = request.Name,
            Geometry = polygon,
            FenceType = request.FenceType,
            Color = request.Color,
            CreatedBy = userContext.UserId ?? Guid.Empty
        };

        var created = await repo.AddAsync(geofence, ct);
        return Results.Created($"/api/v1/geofences/{created.Id}", ToResponse(created));
    }

    private static async Task<IResult> Update(
        Guid id,
        CreateGeofenceRequest request,
        IGeofenceRepository repo,
        CancellationToken ct)
    {
        var geofence = await repo.GetByIdAsync(id, ct);
        if (geofence is null) return Results.NotFound();

        if (request.Coordinates is not null && request.Coordinates.Length >= 3)
        {
            var coords = request.Coordinates.Select(c => new Coordinate(c[0], c[1])).ToList();
            if (!coords[0].Equals(coords[^1]))
                coords.Add(coords[0]);

            var ring = new LinearRing(coords.ToArray());
            geofence.Geometry = new Polygon(ring) { SRID = 4326 };
        }

        geofence.Name = request.Name;
        geofence.FenceType = request.FenceType;
        geofence.Color = request.Color ?? geofence.Color;
        geofence.UpdatedAt = DateTimeOffset.UtcNow;

        await repo.UpdateAsync(geofence, ct);
        return Results.Ok(ToResponse(geofence));
    }

    private static async Task<IResult> Delete(
        Guid id,
        IGeofenceRepository repo,
        CancellationToken ct)
    {
        var geofence = await repo.GetByIdAsync(id, ct);
        if (geofence is null) return Results.NotFound();

        await repo.DeleteAsync(id, ct);
        return Results.NoContent();
    }

    private static GeofenceResponse ToResponse(Geofence g)
    {
        var coords = g.Geometry?.Coordinates
            .Select(c => new[] { c.X, c.Y })
            .ToArray() ?? [];
        return new GeofenceResponse(g.Id, g.Name, coords, g.FenceType, g.IsActive, g.Color, g.CreatedAt);
    }
}

record CreateGeofenceRequest(string Name, double[][] Coordinates, string FenceType = "Both", string? Color = null);
record GeofenceResponse(Guid Id, string Name, double[][] Coordinates, string FenceType, bool IsActive, string? Color, DateTimeOffset CreatedAt);
