using NetTopologySuite.Geometries;
using SentinelMap.SharedKernel.Enums;

namespace SentinelMap.Domain.Entities;

public class Geofence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public Polygon Geometry { get; set; } = null!;
    public string FenceType { get; set; } = "Both";
    public Classification Classification { get; set; } = Classification.Official;
    public Guid CreatedBy { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
