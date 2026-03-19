using SentinelMap.Domain.Entities;

namespace SentinelMap.Domain.Interfaces;

public interface IObservationPublisher
{
    Task PublishAsync(Observation observation, CancellationToken ct = default);
}
