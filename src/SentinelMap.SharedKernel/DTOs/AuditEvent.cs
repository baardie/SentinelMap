using System.Net;

namespace SentinelMap.SharedKernel.DTOs;

public record AuditEvent
{
    public required string EventType { get; init; }  // "Security" or "Operational"
    public Guid? UserId { get; init; }
    public required string Action { get; init; }
    public required string ResourceType { get; init; }
    public Guid? ResourceId { get; init; }
    public object? Details { get; init; }
    public IPAddress? IpAddress { get; init; }
}
