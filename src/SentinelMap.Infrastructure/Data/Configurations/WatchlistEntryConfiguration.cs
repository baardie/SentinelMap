using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SentinelMap.Domain.Entities;

namespace SentinelMap.Infrastructure.Data.Configurations;

public class WatchlistEntryConfiguration : IEntityTypeConfiguration<WatchlistEntry>
{
    public void Configure(EntityTypeBuilder<WatchlistEntry> builder)
    {
        builder.ToTable("watchlist_entries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.WatchlistId).HasColumnName("watchlist_id");
        builder.Property(e => e.IdentifierType).HasColumnName("identifier_type");
        builder.Property(e => e.IdentifierValue).HasColumnName("identifier_value");
        builder.Property(e => e.Reason).HasColumnName("reason");
        builder.Property(e => e.Severity).HasColumnName("severity").HasConversion<string>();
        builder.Property(e => e.AddedBy).HasColumnName("added_by");
        builder.Property(e => e.AddedAt).HasColumnName("added_at");

        builder.HasOne(e => e.Watchlist).WithMany(w => w.Entries).HasForeignKey(e => e.WatchlistId);
    }
}
