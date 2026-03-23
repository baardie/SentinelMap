using SentinelMap.Domain.Entities;
using SentinelMap.SharedKernel.Enums;

namespace SentinelMap.Domain.Interfaces;

public record AlertTrigger(
    AlertType Type,
    AlertSeverity Severity,
    string Summary,
    string? Details = null,
    Guid? GeofenceId = null);

public interface IAlertRule
{
    string RuleId { get; }
    AlertType Type { get; }
    Task<IReadOnlyList<AlertTrigger>> EvaluateAsync(TrackedEntity entity, CancellationToken ct = default);
}
