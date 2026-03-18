using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SentinelMap.SharedKernel.Enums;
using SentinelMap.SharedKernel.Interfaces;

namespace SentinelMap.Infrastructure.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SentinelMapDbContext>
{
    public SentinelMapDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SentinelMapDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=sentinelmap;Username=sentinel;Password=sentinel_dev_password",
            npgsql => npgsql.UseNetTopologySuite());

        return new SentinelMapDbContext(optionsBuilder.Options, new SystemUserContext());
    }

    /// <summary>Design-time user context — system level (no filtering).</summary>
    private class SystemUserContext : IUserContext
    {
        public Guid? UserId => null;
        public string? Email => null;
        public string? Role => null;
        public Classification ClearanceLevel => Classification.Secret; // See all data at design time
        public bool IsAuthenticated => false;
    }
}
