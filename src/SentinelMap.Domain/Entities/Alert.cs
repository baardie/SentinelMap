using SentinelMap.SharedKernel.Enums;

namespace SentinelMap.Domain.Entities;

public class Alert
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public AlertType Type { get; set; }
    public AlertSeverity Severity { get; set; }
    public Guid? EntityId { get; set; }
    public Guid? GeofenceId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? Details { get; set; }
    public AlertStatus Status { get; set; } = AlertStatus.Triggered;
    public Guid? AcknowledgedBy { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public Guid? ResolvedBy { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Classification Classification { get; set; } = Classification.Official;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public TrackedEntity? Entity { get; set; }
    public Geofence? Geofence { get; set; }
}
