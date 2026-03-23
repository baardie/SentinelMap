using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SentinelMap.Infrastructure.Auth;
using SentinelMap.Infrastructure.Data;
using SentinelMap.Infrastructure.Identity;

namespace SentinelMap.Api.Endpoints;

public static class AuthEndpoints
{
    private record LoginRequest(string Email, string Password);
    private record RefreshRequest(string RefreshToken);

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/login", Login).AllowAnonymous().RequireRateLimiting("auth");
        group.MapPost("/refresh", Refresh).AllowAnonymous().RequireRateLimiting("auth");
        group.MapPost("/revoke", Revoke).RequireAuthorization();
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        UserManager<AppIdentityUser> userManager,
        JwtTokenService jwtTokenService,
        RefreshTokenService refreshTokenService,
        SystemDbContext db,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var identityUser = await userManager.FindByEmailAsync(request.Email);
        if (identityUser is null)
            return Results.Unauthorized();

        if (await userManager.IsLockedOutAsync(identityUser))
            return Results.StatusCode(423); // Locked

        if (!await userManager.CheckPasswordAsync(identityUser, request.Password))
        {
            await userManager.AccessFailedAsync(identityUser);
            return Results.Unauthorized();
        }

        await userManager.ResetAccessFailedCountAsync(identityUser);

        var domainUser = await db.DomainUsers
            .FirstOrDefaultAsync(u => u.Id == identityUser.Id, ct);

        if (domainUser is null)
            return Results.Unauthorized();

        var roles = await userManager.GetRolesAsync(identityUser);
        var role = roles.FirstOrDefault() ?? "Viewer";

        var accessToken = jwtTokenService.GenerateAccessToken(
            identityUser.Id,
            identityUser.Email!,
            role,
            domainUser.ClearanceLevel.ToString());

        var deviceInfo = httpContext.Request.Headers.UserAgent.ToString();
        var (refreshToken, _) = await refreshTokenService.CreateAsync(
            identityUser.Id, deviceInfo, ct);

        return Results.Ok(new
        {
            accessToken,
            refreshToken,
            expiresIn = 900
        });
    }

    private static async Task<IResult> Refresh(
        RefreshRequest request,
        RefreshTokenService refreshTokenService,
        UserManager<AppIdentityUser> userManager,
        JwtTokenService jwtTokenService,
        SystemDbContext db,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var deviceInfo = httpContext.Request.Headers.UserAgent.ToString();
        var (oldToken, newRawToken, _) = await refreshTokenService.ValidateAndRotateAsync(
            request.RefreshToken, deviceInfo, ct);

        if (oldToken is null || newRawToken is null)
            return Results.Unauthorized();

        var identityUser = await userManager.FindByIdAsync(oldToken.UserId.ToString());
        if (identityUser is null)
            return Results.Unauthorized();

        var domainUser = await db.DomainUsers
            .FirstOrDefaultAsync(u => u.Id == oldToken.UserId, ct);

        if (domainUser is null)
            return Results.Unauthorized();

        var roles = await userManager.GetRolesAsync(identityUser);
        var role = roles.FirstOrDefault() ?? "Viewer";

        var accessToken = jwtTokenService.GenerateAccessToken(
            identityUser.Id,
            identityUser.Email!,
            role,
            domainUser.ClearanceLevel.ToString());

        return Results.Ok(new
        {
            accessToken,
            refreshToken = newRawToken,
            expiresIn = 900
        });
    }

    private static async Task<IResult> Revoke(
        RefreshTokenService refreshTokenService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userIdClaim = httpContext.User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)
            ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (userIdClaim is null || !Guid.TryParse(userIdClaim, out var userId))
            return Results.Unauthorized();

        await refreshTokenService.RevokeAllForUserAsync(userId, ct);

        return Results.NoContent();
    }
}
