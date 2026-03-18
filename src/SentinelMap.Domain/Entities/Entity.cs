using NetTopologySuite.Geometries;
using SentinelMap.SharedKernel.Enums;

namespace SentinelMap.Domain.Entities;

public class TrackedEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public EntityType Type { get; set; }
    public string? DisplayName { get; set; }
    public Point? LastKnownPosition { get; set; }
    public double? LastSpeedMps { get; set; }
    public double? LastHeading { get; set; }
    public DateTimeOffset? LastSeen { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
    public Classification Classification { get; set; } = Classification.Official;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<EntityIdentifier> Identifiers { get; set; } = [];
    public List<Alert> Alerts { get; set; } = [];

    public void UpdatePosition(Point position, double speedMps, double heading, DateTimeOffset timestamp)
    {
        LastKnownPosition = position;
        LastSpeedMps = speedMps;
        LastHeading = heading;
        LastSeen = timestamp;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
