using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SentinelMap.Domain.Entities;

namespace SentinelMap.Infrastructure.Data.Configurations;

public class EntityIdentifierConfiguration : IEntityTypeConfiguration<EntityIdentifier>
{
    public void Configure(EntityTypeBuilder<EntityIdentifier> builder)
    {
        builder.ToTable("entity_identifiers");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.EntityId).HasColumnName("entity_id");
        builder.Property(e => e.IdentifierType).HasColumnName("identifier_type");
        builder.Property(e => e.IdentifierValue).HasColumnName("identifier_value");
        builder.Property(e => e.Source).HasColumnName("source");
        builder.Property(e => e.FirstSeen).HasColumnName("first_seen");
        builder.Property(e => e.LastSeen).HasColumnName("last_seen");

        builder.HasIndex(e => new { e.IdentifierType, e.IdentifierValue }).IsUnique();
        builder.HasOne(e => e.Entity).WithMany(e => e.Identifiers).HasForeignKey(e => e.EntityId);
    }
}
