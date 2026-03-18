using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SentinelMap.Domain.Entities;
using SentinelMap.Infrastructure.Identity;
using SentinelMap.SharedKernel.Enums;
using SentinelMap.SharedKernel.Interfaces;

namespace SentinelMap.Infrastructure.Data;

/// <summary>
/// API DbContext — filtered by user clearance level.
/// Classification query filter prevents data leakage through any endpoint.
/// </summary>
public class SentinelMapDbContext : IdentityDbContext<AppIdentityUser, IdentityRole<Guid>, Guid>
{
    private readonly IUserContext _userContext;

    public SentinelMapDbContext(DbContextOptions<SentinelMapDbContext> options, IUserContext userContext)
        : base(options)
    {
        _userContext = userContext;
    }

    public DbSet<TrackedEntity> Entities => Set<TrackedEntity>();
    public DbSet<EntityIdentifier> EntityIdentifiers => Set<EntityIdentifier>();
    public DbSet<Observation> Observations => Set<Observation>();
    public DbSet<Geofence> Geofences => Set<Geofence>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<Watchlist> Watchlists => Set<Watchlist>();
    public DbSet<WatchlistEntry> WatchlistEntries => Set<WatchlistEntry>();
    public DbSet<User> DomainUsers => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresExtension("postgis");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SentinelMapDbContext).Assembly);

        // Classification query filter — user sees only data at or below their clearance
        var clearance = _userContext.ClearanceLevel;
        modelBuilder.Entity<TrackedEntity>().HasQueryFilter(e => e.Classification <= clearance);
        modelBuilder.Entity<Alert>().HasQueryFilter(a => a.Classification <= clearance);
        modelBuilder.Entity<Geofence>().HasQueryFilter(g => g.Classification <= clearance);
    }
}
