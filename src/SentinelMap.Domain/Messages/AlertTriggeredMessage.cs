namespace SentinelMap.Domain.Messages;

public record AlertTriggeredMessage(
    Guid AlertId,
    string Type,
    string Severity,
    Guid? EntityId,
    string Summary,
    DateTimeOffset CreatedAt);
