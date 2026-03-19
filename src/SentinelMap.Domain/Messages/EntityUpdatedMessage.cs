namespace SentinelMap.Domain.Messages;

/// <summary>
/// Published to Redis channel "entities:updated" by CorrelationWorker.
/// Consumed by TrackHubService to push TrackUpdate events to SignalR clients.
/// </summary>
public record EntityUpdatedMessage(
    Guid EntityId,
    double Longitude,
    double Latitude,
    double? Heading,
    double? Speed,
    string EntityType,
    string Status,
    DateTimeOffset Timestamp);
