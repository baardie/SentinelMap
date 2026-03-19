using Microsoft.AspNetCore.Identity;
using SentinelMap.Domain.Entities;
using SentinelMap.Infrastructure.Data;
using SentinelMap.Infrastructure.Identity;   // AppIdentityUser
using SentinelMap.SharedKernel.Enums;         // Roles, Classification

namespace SentinelMap.Api.Services;

public static class UserSeeder
{
    private static readonly (string Email, string Role, Classification Clearance)[] SeedUsers =
    [
        ("admin@sentinel.local",   Roles.Admin,   Classification.Secret),
        ("analyst@sentinel.local", Roles.Analyst, Classification.OfficialSensitive),
        ("viewer@sentinel.local",  Roles.Viewer,  Classification.Official),
    ];

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppIdentityUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var db = scope.ServiceProvider.GetRequiredService<SentinelMapDbContext>();

        var password = Environment.GetEnvironmentVariable("SENTINELMAP_SEED_PASSWORD") ?? "Demo123!";

        // Ensure roles exist
        foreach (var role in new[] { Roles.Admin, Roles.Analyst, Roles.Viewer })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));
        }

        foreach (var (email, role, clearance) in SeedUsers)
        {
            if (await userManager.FindByEmailAsync(email) is not null) continue;

            var identityUser = new AppIdentityUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
            };

            var result = await userManager.CreateAsync(identityUser, password);
            if (!result.Succeeded) continue;

            await userManager.AddToRoleAsync(identityUser, role);

            db.DomainUsers.Add(new User
            {
                Id = identityUser.Id,
                Email = email,
                DisplayName = email.Split('@')[0],
                Role = role,
                ClearanceLevel = clearance,
            });
        }

        await db.SaveChangesAsync();
    }
}
