using SentinelMap.Domain.Interfaces;
using SentinelMap.SharedKernel.Enums;
using SentinelMap.SharedKernel.Interfaces;

namespace SentinelMap.Api.Endpoints;

public static class AlertEndpoints
{
    public static void MapAlertEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/alerts").WithTags("Alerts");

        group.MapGet("/", GetFeed).RequireAuthorization("ViewerAccess");
        group.MapGet("/{id:guid}", GetById).RequireAuthorization("ViewerAccess");
        group.MapPatch("/{id:guid}/acknowledge", Acknowledge).RequireAuthorization("AnalystAccess");
        group.MapPatch("/{id:guid}/resolve", Resolve).RequireAuthorization("AnalystAccess");
    }

    private static async Task<IResult> GetFeed(
        IAlertRepository repo,
        int limit = 50,
        CancellationToken ct = default)
    {
        var alerts = await repo.GetFeedAsync(limit, ct);
        return Results.Ok(alerts.Select(a => new
        {
            a.Id,
            Type = a.Type.ToString(),
            Severity = a.Severity.ToString(),
            a.EntityId,
            a.GeofenceId,
            a.Summary,
            a.Details,
            Status = a.Status.ToString(),
            a.AcknowledgedAt,
            a.ResolvedAt,
            a.CreatedAt
        }));
    }

    private static async Task<IResult> GetById(
        Guid id,
        IAlertRepository repo,
        CancellationToken ct)
    {
        var alert = await repo.GetByIdAsync(id, ct);
        if (alert is null) return Results.NotFound();

        return Results.Ok(new
        {
            alert.Id,
            Type = alert.Type.ToString(),
            Severity = alert.Severity.ToString(),
            alert.EntityId,
            alert.GeofenceId,
            alert.Summary,
            alert.Details,
            Status = alert.Status.ToString(),
            alert.AcknowledgedBy,
            alert.AcknowledgedAt,
            alert.ResolvedBy,
            alert.ResolvedAt,
            alert.CreatedAt
        });
    }

    private static async Task<IResult> Acknowledge(
        Guid id,
        IAlertRepository repo,
        IUserContext userContext,
        CancellationToken ct)
    {
        var alert = await repo.GetByIdAsync(id, ct);
        if (alert is null) return Results.NotFound();
        if (alert.Status != AlertStatus.Triggered)
            return Results.Conflict(new { message = $"Alert is already {alert.Status}." });

        alert.Status = AlertStatus.Acknowledged;
        alert.AcknowledgedBy = userContext.UserId;
        alert.AcknowledgedAt = DateTimeOffset.UtcNow;

        await repo.UpdateAsync(alert, ct);
        return Results.Ok(new
        {
            alert.Id,
            Status = alert.Status.ToString(),
            alert.AcknowledgedBy,
            alert.AcknowledgedAt
        });
    }

    private static async Task<IResult> Resolve(
        Guid id,
        IAlertRepository repo,
        IUserContext userContext,
        CancellationToken ct)
    {
        var alert = await repo.GetByIdAsync(id, ct);
        if (alert is null) return Results.NotFound();
        if (alert.Status == AlertStatus.Resolved)
            return Results.Conflict(new { message = "Alert is already resolved." });

        alert.Status = AlertStatus.Resolved;
        alert.ResolvedBy = userContext.UserId;
        alert.ResolvedAt = DateTimeOffset.UtcNow;

        await repo.UpdateAsync(alert, ct);
        return Results.Ok(new
        {
            alert.Id,
            Status = alert.Status.ToString(),
            alert.ResolvedBy,
            alert.ResolvedAt
        });
    }
}
