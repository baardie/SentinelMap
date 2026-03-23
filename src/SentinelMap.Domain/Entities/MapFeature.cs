using NetTopologySuite.Geometries;

namespace SentinelMap.Domain.Entities;

public class MapFeature
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FeatureType { get; set; } = string.Empty;  // AisBaseStation, AidToNavigation, Airport, MilitaryBase, CustomStructure
    public string Name { get; set; } = string.Empty;
    public Point Position { get; set; } = null!;
    public string? Icon { get; set; }
    public string? Color { get; set; }
    public string? Details { get; set; }  // JSONB metadata
    public string Source { get; set; } = string.Empty;  // ais, static, user
    public bool IsActive { get; set; } = true;
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
