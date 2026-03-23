using SentinelMap.Domain.Entities;

namespace SentinelMap.Domain.Interfaces;

public interface IAlertRepository
{
    Task<Alert> AddAsync(Alert alert, CancellationToken ct = default);
    Task<Alert?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Alert>> GetFeedAsync(int limit = 50, CancellationToken ct = default);
    Task UpdateAsync(Alert alert, CancellationToken ct = default);
}
