namespace SentinelMap.Domain.Entities;

public class EntityIdentifier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EntityId { get; set; }
    public string IdentifierType { get; set; } = string.Empty;
    public string IdentifierValue { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset FirstSeen { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;

    public TrackedEntity Entity { get; set; } = null!;
}
