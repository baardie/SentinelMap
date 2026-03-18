using Microsoft.EntityFrameworkCore;
using SentinelMap.Domain.Entities;

namespace SentinelMap.Infrastructure.Data;

/// <summary>
/// Workers DbContext — unfiltered, system-level access.
/// Background processes run at system level; classification filtering
/// applies at the human interface boundary only. See ADR-006.
/// </summary>
public class SystemDbContext : DbContext
{
    public SystemDbContext(DbContextOptions<SystemDbContext> options) : base(options) { }

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

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SystemDbContext).Assembly);

        // No classification query filters — system level access
    }
}
