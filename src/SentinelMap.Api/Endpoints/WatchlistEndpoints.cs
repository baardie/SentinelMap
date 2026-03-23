using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;
using SentinelMap.SharedKernel.Enums;
using SentinelMap.SharedKernel.Interfaces;

namespace SentinelMap.Api.Endpoints;

public static class WatchlistEndpoints
{
    public static void MapWatchlistEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/watchlists").WithTags("Watchlists");

        group.MapGet("/", GetAll).RequireAuthorization("AnalystAccess");
        group.MapPost("/", Create).RequireAuthorization("AnalystAccess");
        group.MapGet("/{id:guid}", GetById).RequireAuthorization("AnalystAccess");
        group.MapPost("/{id:guid}/entries", AddEntry).RequireAuthorization("AnalystAccess");
        group.MapDelete("/{watchlistId:guid}/entries/{entryId:guid}", RemoveEntry).RequireAuthorization("AnalystAccess");
    }

    private static async Task<IResult> GetAll(
        IWatchlistRepository repo,
        CancellationToken ct)
    {
        var watchlists = await repo.GetAllAsync(ct);
        return Results.Ok(watchlists.Select(w => new
        {
            w.Id,
            w.Name,
            w.Description,
            w.CreatedAt,
            EntryCount = w.Entries.Count
        }));
    }

    private static async Task<IResult> GetById(
        Guid id,
        IWatchlistRepository repo,
        CancellationToken ct)
    {
        var watchlist = await repo.GetByIdAsync(id, ct);
        if (watchlist is null) return Results.NotFound();

        return Results.Ok(new
        {
            watchlist.Id,
            watchlist.Name,
            watchlist.Description,
            watchlist.CreatedAt,
            Entries = watchlist.Entries.Select(e => new
            {
                e.Id,
                e.IdentifierType,
                e.IdentifierValue,
                e.Reason,
                Severity = e.Severity.ToString(),
                e.AddedAt
            })
        });
    }

    private static async Task<IResult> Create(
        CreateWatchlistRequest request,
        IWatchlistRepository repo,
        IUserContext userContext,
        CancellationToken ct)
    {
        var watchlist = new Watchlist
        {
            Name = request.Name,
            Description = request.Description,
            CreatedBy = userContext.UserId ?? Guid.Empty
        };

        var created = await repo.AddAsync(watchlist, ct);
        return Results.Created($"/api/v1/watchlists/{created.Id}", new
        {
            created.Id,
            created.Name,
            created.Description,
            created.CreatedAt
        });
    }

    private static async Task<IResult> AddEntry(
        Guid id,
        AddWatchlistEntryRequest request,
        IWatchlistRepository repo,
        IUserContext userContext,
        CancellationToken ct)
    {
        var watchlist = await repo.GetByIdAsync(id, ct);
        if (watchlist is null) return Results.NotFound();

        if (!Enum.TryParse<AlertSeverity>(request.Severity, ignoreCase: true, out var severity))
            severity = AlertSeverity.High;

        var entry = new WatchlistEntry
        {
            WatchlistId = id,
            IdentifierType = request.IdentifierType,
            IdentifierValue = request.IdentifierValue,
            Reason = request.Reason,
            Severity = severity,
            AddedBy = userContext.UserId ?? Guid.Empty
        };

        await repo.AddEntryAsync(entry, ct);
        return Results.Created($"/api/v1/watchlists/{id}/entries/{entry.Id}", new
        {
            entry.Id,
            entry.WatchlistId,
            entry.IdentifierType,
            entry.IdentifierValue,
            entry.Reason,
            Severity = entry.Severity.ToString(),
            entry.AddedAt
        });
    }

    private static async Task<IResult> RemoveEntry(
        Guid watchlistId,
        Guid entryId,
        IWatchlistRepository repo,
        CancellationToken ct)
    {
        var watchlist = await repo.GetByIdAsync(watchlistId, ct);
        if (watchlist is null) return Results.NotFound();

        await repo.RemoveEntryAsync(entryId, ct);
        return Results.NoContent();
    }
}

record CreateWatchlistRequest(string Name, string? Description);
record AddWatchlistEntryRequest(string IdentifierType, string IdentifierValue, string? Reason, string Severity = "High");
