namespace SentinelMap.Domain.Entities;

public class WebhookEndpoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Url { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public string? EventFilter { get; set; }  // JSONB — e.g. {"types":["GeofenceBreach","AisDark"]}
    public bool IsActive { get; set; } = true;
    public int ConsecutiveFailures { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
