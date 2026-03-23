using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Infrastructure.Data;
using SentinelMap.SharedKernel.Interfaces;

namespace SentinelMap.Api.Endpoints;

public static class MapFeatureEndpoints
{
    public static void MapMapFeatureEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/map-features").WithTags("MapFeatures");

        group.MapGet("/", GetAll).RequireAuthorization("ViewerAccess");
        group.MapPost("/", Create).RequireAuthorization("AnalystAccess");
        group.MapPut("/{id:guid}", Update).RequireAuthorization("AnalystAccess");
        group.MapDelete("/{id:guid}", Delete).RequireAuthorization("AnalystAccess");
    }

    private static async Task<IResult> GetAll(
        SentinelMapDbContext db,
        string? type,
        CancellationToken ct)
    {
        var query = db.MapFeatures.Where(f => f.IsActive);

        if (!string.IsNullOrEmpty(type))
        {
            var types = type.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            query = query.Where(f => types.Contains(f.FeatureType));
        }

        var features = await query
            .OrderBy(f => f.FeatureType)
            .ThenBy(f => f.Name)
            .ToListAsync(ct);

        var response = features.Select(ToResponse).ToList();
        return Results.Ok(response);
    }

    private static async Task<IResult> Create(
        CreateMapFeatureRequest request,
        SentinelMapDbContext db,
        IUserContext userContext,
        CancellationToken ct)
    {
        var feature = new MapFeature
        {
            FeatureType = request.FeatureType ?? "CustomStructure",
            Name = request.Name,
            Position = new Point(request.Longitude, request.Latitude) { SRID = 4326 },
            Icon = request.Icon,
            Color = request.Color,
            Details = request.Details,
            Source = "user",
            CreatedBy = userContext.UserId ?? Guid.Empty,
        };

        db.MapFeatures.Add(feature);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/map-features/{feature.Id}", ToResponse(feature));
    }

    private static async Task<IResult> Update(
        Guid id,
        UpdateMapFeatureRequest request,
        SentinelMapDbContext db,
        CancellationToken ct)
    {
        var feature = await db.MapFeatures.FindAsync([id], ct);
        if (feature is null) return Results.NotFound();

        feature.Name = request.Name ?? feature.Name;
        feature.Icon = request.Icon ?? feature.Icon;
        feature.Color = request.Color ?? feature.Color;
        feature.Details = request.Details ?? feature.Details;
        feature.IsActive = request.IsActive ?? feature.IsActive;

        if (request.Longitude.HasValue && request.Latitude.HasValue)
        {
            feature.Position = new Point(request.Longitude.Value, request.Latitude.Value) { SRID = 4326 };
        }

        feature.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.Ok(ToResponse(feature));
    }

    private static async Task<IResult> Delete(
        Guid id,
        SentinelMapDbContext db,
        CancellationToken ct)
    {
        var feature = await db.MapFeatures.FindAsync([id], ct);
        if (feature is null) return Results.NotFound();

        // Only allow deletion of user-created features
        if (feature.Source != "user")
            return Results.Problem("Only user-created features can be deleted.", statusCode: 403);

        db.MapFeatures.Remove(feature);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static MapFeatureResponse ToResponse(MapFeature f) =>
        new(f.Id, f.FeatureType, f.Name, f.Position.X, f.Position.Y,
            f.Icon, f.Color, f.Details, f.Source, f.IsActive);
}

record MapFeatureResponse(Guid Id, string FeatureType, string Name, double Longitude, double Latitude,
    string? Icon, string? Color, string? Details, string Source, bool IsActive);

record CreateMapFeatureRequest(string Name, double Longitude, double Latitude,
    string? FeatureType = null, string? Icon = null, string? Color = null, string? Details = null);

record UpdateMapFeatureRequest(string? Name = null, double? Longitude = null, double? Latitude = null,
    string? Icon = null, string? Color = null, string? Details = null, bool? IsActive = null);
