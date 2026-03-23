namespace SentinelMap.Domain.Messages;

/// <summary>
/// Published to Redis channel "observations:{sourceType}" after an observation is persisted.
/// Contains enough data for CorrelationWorker to skip a DB round-trip on the hot path.
/// </summary>
public record ObservationPublishedMessage(
    long ObservationId,
    DateTimeOffset ObservedAt,
    string SourceType,
    string ExternalId,
    double Longitude,
    double Latitude,
    double? Heading,
    double? SpeedMps,
    string? DisplayName = null,
    string? VesselType = null,
    string? AircraftType = null);
