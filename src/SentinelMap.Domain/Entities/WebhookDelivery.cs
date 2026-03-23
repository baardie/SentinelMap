namespace SentinelMap.Domain.Entities;

public class WebhookDelivery
{
    public long Id { get; set; }
    public Guid EndpointId { get; set; }
    public Guid AlertId { get; set; }
    public string Status { get; set; } = "Pending";  // Pending, Success, Failed
    public int? ResponseCode { get; set; }
    public int? LatencyMs { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public WebhookEndpoint Endpoint { get; set; } = null!;
}
