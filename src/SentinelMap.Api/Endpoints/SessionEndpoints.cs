using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SentinelMap.Infrastructure.Data;

namespace SentinelMap.Api.Endpoints;

public static class SessionEndpoints
{
    public static void MapSessionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/sessions").WithTags("Sessions");

        group.MapGet("/", GetOwnSessions).RequireAuthorization("ViewerAccess");
        group.MapDelete("/{familyId}", RevokeSession).RequireAuthorization("ViewerAccess");

        var admin = app.MapGroup("/api/v1/admin/sessions").WithTags("Sessions");
        admin.MapGet("/", GetAllSessions).RequireAuthorization("AdminAccess");
        admin.MapDelete("/{familyId}", AdminRevokeSession).RequireAuthorization("AdminAccess");
    }

    private static Guid? GetUserId(HttpContext httpContext)
    {
        var userIdClaim = httpContext.User.FindFirstValue(
                System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)
            ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
    }

    private static string? GetCurrentFamilyId(HttpContext httpContext, SystemDbContext db)
    {
        // Client can pass the raw refresh token via X-Current-Token header
        // so we can identify which family is the "current" session
        var rawToken = httpContext.Request.Headers["X-Current-Token"].FirstOrDefault();
        if (string.IsNullOrEmpty(rawToken)) return null;

        var hash = HashToken(rawToken);
        var token = db.RefreshTokens.FirstOrDefault(t => t.TokenHash == hash);
        return token?.FamilyId;
    }

    private static string HashToken(string rawToken)
    {
        var bytes = Encoding.UTF8.GetBytes(rawToken);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<IResult> GetOwnSessions(
        SystemDbContext db,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext);
        if (userId is null) return Results.Unauthorized();

        var currentFamilyId = GetCurrentFamilyId(httpContext, db);

        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == userId.Value)
            .ToListAsync(ct);

        var sessions = tokens
            .GroupBy(t => t.FamilyId)
            .Select(g =>
            {
                var latest = g.OrderByDescending(t => t.CreatedAt).First();
                var isActive = g.Any(t => !t.IsRevoked && t.ExpiresAt > DateTimeOffset.UtcNow);
                return new
                {
                    familyId = g.Key,
                    deviceInfo = latest.DeviceInfo ?? "Unknown",
                    createdAt = g.Min(t => t.CreatedAt),
                    lastUsedAt = g.Max(t => t.LastUsedAt) ?? g.Max(t => t.CreatedAt),
                    isActive,
                    isCurrent = g.Key == currentFamilyId
                };
            })
            .OrderByDescending(s => s.lastUsedAt)
            .ToList();

        return Results.Ok(sessions);
    }

    private static async Task<IResult> RevokeSession(
        string familyId,
        SystemDbContext db,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext);
        if (userId is null) return Results.Unauthorized();

        var count = await db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.UserId == userId.Value && !t.IsRevoked)
            .ExecuteUpdateAsync(t => t.SetProperty(r => r.IsRevoked, true), ct);

        if (count == 0) return Results.NotFound();

        return Results.NoContent();
    }

    private static async Task<IResult> GetAllSessions(
        SystemDbContext db,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var currentFamilyId = GetCurrentFamilyId(httpContext, db);

        var tokens = await db.RefreshTokens
            .ToListAsync(ct);

        var users = await db.DomainUsers
            .Select(u => new { u.Id, u.Email })
            .ToListAsync(ct);

        var userMap = users.ToDictionary(u => u.Id, u => u.Email);

        var sessions = tokens
            .GroupBy(t => t.FamilyId)
            .Select(g =>
            {
                var latest = g.OrderByDescending(t => t.CreatedAt).First();
                var isActive = g.Any(t => !t.IsRevoked && t.ExpiresAt > DateTimeOffset.UtcNow);
                return new
                {
                    familyId = g.Key,
                    userId = latest.UserId,
                    userEmail = userMap.GetValueOrDefault(latest.UserId, "Unknown"),
                    deviceInfo = latest.DeviceInfo ?? "Unknown",
                    createdAt = g.Min(t => t.CreatedAt),
                    lastUsedAt = g.Max(t => t.LastUsedAt) ?? g.Max(t => t.CreatedAt),
                    isActive,
                    isCurrent = g.Key == currentFamilyId
                };
            })
            .OrderByDescending(s => s.lastUsedAt)
            .ToList();

        return Results.Ok(sessions);
    }

    private static async Task<IResult> AdminRevokeSession(
        string familyId,
        SystemDbContext db,
        CancellationToken ct)
    {
        var count = await db.RefreshTokens
            .Where(t => t.FamilyId == familyId && !t.IsRevoked)
            .ExecuteUpdateAsync(t => t.SetProperty(r => r.IsRevoked, true), ct);

        if (count == 0) return Results.NotFound();

        return Results.NoContent();
    }
}
