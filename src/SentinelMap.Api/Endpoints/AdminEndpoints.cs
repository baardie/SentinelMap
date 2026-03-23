using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SentinelMap.Infrastructure.Data;
using SentinelMap.Infrastructure.Identity;
using SentinelMap.SharedKernel.Enums;

namespace SentinelMap.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin").WithTags("Admin");

        group.MapGet("/users", GetUsers).RequireAuthorization("AdminAccess");
        group.MapPatch("/users/{id:guid}/role", ChangeUserRole).RequireAuthorization("AdminAccess");
    }

    private static async Task<IResult> GetUsers(
        SystemDbContext db,
        CancellationToken ct)
    {
        var users = await db.DomainUsers
            .OrderBy(u => u.Email)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.DisplayName,
                u.Role,
                ClearanceLevel = u.ClearanceLevel.ToString(),
                u.IsActive,
                u.CreatedAt
            })
            .ToListAsync(ct);

        return Results.Ok(users);
    }

    private static async Task<IResult> ChangeUserRole(
        Guid id,
        ChangeRoleRequest request,
        SystemDbContext db,
        UserManager<AppIdentityUser> userManager,
        CancellationToken ct)
    {
        if (request.Role is not (Roles.Viewer or Roles.Analyst or Roles.Admin))
            return Results.BadRequest(new { message = $"Role must be one of: {Roles.Viewer}, {Roles.Analyst}, {Roles.Admin}." });

        // Update domain user record
        var domainUser = await db.DomainUsers.FindAsync([id], ct);
        if (domainUser is null) return Results.NotFound();

        domainUser.Role = request.Role;
        await db.SaveChangesAsync(ct);

        // Keep Identity roles in sync
        var identityUser = await userManager.FindByIdAsync(id.ToString());
        if (identityUser is not null)
        {
            var currentRoles = await userManager.GetRolesAsync(identityUser);
            await userManager.RemoveFromRolesAsync(identityUser, currentRoles);
            await userManager.AddToRoleAsync(identityUser, request.Role);
        }

        return Results.Ok(new
        {
            domainUser.Id,
            domainUser.Email,
            domainUser.Role,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }
}

record ChangeRoleRequest(string Role);
