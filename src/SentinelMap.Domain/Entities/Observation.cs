using NetTopologySuite.Geometries;

namespace SentinelMap.Domain.Entities;

public class Observation
{
    public long Id { get; set; }
    public Guid? EntityId { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public Point? Position { get; set; }
    public double? SpeedMps { get; set; }
    public double? Heading { get; set; }
    public string? RawData { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public DateTimeOffset IngestedAt { get; set; } = DateTimeOffset.UtcNow;

    public TrackedEntity? Entity { get; set; }
}
