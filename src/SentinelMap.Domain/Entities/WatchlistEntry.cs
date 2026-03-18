using SentinelMap.SharedKernel.Enums;

namespace SentinelMap.Domain.Entities;

public class WatchlistEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WatchlistId { get; set; }
    public string IdentifierType { get; set; } = string.Empty;
    public string IdentifierValue { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public AlertSeverity Severity { get; set; } = AlertSeverity.High;
    public Guid AddedBy { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;

    public Watchlist Watchlist { get; set; } = null!;
}
