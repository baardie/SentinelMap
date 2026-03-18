using SentinelMap.Domain.Entities;

namespace SentinelMap.Domain.Interfaces;

public interface ISourceConnector
{
    string SourceId { get; }
    string SourceType { get; }
    IAsyncEnumerable<Observation> StreamAsync(CancellationToken ct);
}
