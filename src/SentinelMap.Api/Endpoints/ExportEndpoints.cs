using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SentinelMap.Infrastructure.Data;
using SentinelMap.SharedKernel.Enums;
using SentinelMap.SharedKernel.Interfaces;

namespace SentinelMap.Api.Endpoints;

public static class ExportEndpoints
{
    public static void MapExportEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/export").WithTags("Export");

        group.MapPost("/", Export).RequireAuthorization("AnalystAccess");
    }

    private static async Task<IResult> Export(
        ExportRequest request,
        SystemDbContext db,
        IUserContext userContext,
        CancellationToken ct)
    {
        if (request.Format is not ("csv" or "geojson"))
            return Results.BadRequest(new { message = "Format must be 'csv' or 'geojson'." });

        var clearance = userContext.ClearanceLevel;
        var exportedBy = userContext.Email ?? userContext.UserId?.ToString() ?? "unknown";
        var exportedAt = DateTimeOffset.UtcNow;

        // Build entity query with classification filter
        var query = db.Entities
            .Include(e => e.Identifiers)
            .Where(e => (int)e.Classification <= (int)clearance);

        // Optionally filter to specific entity IDs
        if (request.EntityIds is { Length: > 0 })
            query = query.Where(e => request.EntityIds.Contains(e.Id));

        // Optionally filter by time window (LastSeen)
        if (request.From.HasValue)
            query = query.Where(e => e.LastSeen >= request.From.Value);
        if (request.To.HasValue)
            query = query.Where(e => e.LastSeen <= request.To.Value);

        var entities = await query.OrderBy(e => e.DisplayName).ToListAsync(ct);

        var classificationLabel = clearance switch
        {
            Classification.Secret => "SECRET",
            Classification.OfficialSensitive => "OFFICIAL-SENSITIVE",
            _ => "OFFICIAL"
        };

        if (request.Format == "csv")
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# CLASSIFICATION: {classificationLabel}");
            sb.AppendLine($"# Exported: {exportedAt:O}");
            sb.AppendLine($"# User: {exportedBy}");
            sb.AppendLine("EntityId,Type,DisplayName,Latitude,Longitude,Speed(kt),Heading,Status,LastSeen");

            const double MpsToKnots = 1.94384;

            foreach (var e in entities)
            {
                var lat = e.LastKnownPosition?.Y.ToString("F6") ?? "";
                var lon = e.LastKnownPosition?.X.ToString("F6") ?? "";
                var speedKt = e.LastSpeedMps.HasValue
                    ? Math.Round(e.LastSpeedMps.Value * MpsToKnots, 1).ToString("F1")
                    : "";
                var heading = e.LastHeading.HasValue ? e.LastHeading.Value.ToString("F1") : "";
                var lastSeen = e.LastSeen.HasValue ? e.LastSeen.Value.ToString("O") : "";
                var displayName = EscapeCsvField(e.DisplayName ?? "");

                sb.AppendLine($"{e.Id},{e.Type},{displayName},{lat},{lon},{speedKt},{heading},{e.Status},{lastSeen}");
            }

            var csvBytes = Encoding.UTF8.GetBytes(sb.ToString());
            return Results.File(
                csvBytes,
                contentType: "text/csv; charset=utf-8",
                fileDownloadName: $"sentinelmap-export-{exportedAt:yyyyMMdd-HHmmss}.csv");
        }
        else // geojson
        {
            var features = entities.Select(e =>
            {
                var properties = new Dictionary<string, object?>
                {
                    ["entityId"] = e.Id,
                    ["type"] = e.Type.ToString(),
                    ["displayName"] = e.DisplayName,
                    ["speedKnots"] = e.LastSpeedMps.HasValue ? Math.Round(e.LastSpeedMps.Value * 1.94384, 1) : (object?)null,
                    ["heading"] = e.LastHeading,
                    ["status"] = e.Status.ToString(),
                    ["classification"] = e.Classification.ToString(),
                    ["lastSeen"] = e.LastSeen?.ToString("O"),
                };

                object? geometry = e.LastKnownPosition != null
                    ? new
                    {
                        type = "Point",
                        coordinates = new[] { e.LastKnownPosition.X, e.LastKnownPosition.Y }
                    }
                    : null;

                return new { type = "Feature", geometry, properties };
            }).ToList();

            var featureCollection = new
            {
                type = "FeatureCollection",
                properties = new
                {
                    classification = classificationLabel,
                    exportedAt = exportedAt.ToString("O"),
                    exportedBy,
                    count = features.Count
                },
                features
            };

            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(featureCollection, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            return Results.File(
                jsonBytes,
                contentType: "application/geo+json",
                fileDownloadName: $"sentinelmap-export-{exportedAt:yyyyMMdd-HHmmss}.geojson");
        }
    }

    private static string EscapeCsvField(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}

record ExportRequest(string Format, Guid[]? EntityIds = null, DateTimeOffset? From = null, DateTimeOffset? To = null);
