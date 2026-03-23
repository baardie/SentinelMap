using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Messages;
using SentinelMap.Infrastructure.Data;
using StackExchange.Redis;

namespace SentinelMap.Infrastructure.Services;

/// <summary>
/// Subscribes to Redis "alerts:triggered" and delivers webhook notifications
/// to registered endpoints with HMAC-SHA256 signatures.
/// </summary>
public class WebhookService : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookService> _logger;

    private static readonly int[] RetryDelaysSeconds = [10, 60, 300];
    private const int MaxConsecutiveFailures = 10;

    public WebhookService(
        IConnectionMultiplexer redis,
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<WebhookService> logger)
    {
        _redis = redis;
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = _redis.GetSubscriber();

        await subscriber.SubscribeAsync(RedisChannel.Literal("alerts:triggered"), async (_, message) =>
        {
            if (message.IsNull) return;

            AlertTriggeredMessage? alertMsg;
            try { alertMsg = JsonSerializer.Deserialize<AlertTriggeredMessage>(message!); }
            catch { return; }
            if (alertMsg is null) return;

            try
            {
                await ProcessAlertAsync(alertMsg, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing webhook for alert {AlertId}", alertMsg.AlertId);
            }
        });

        _logger.LogInformation("WebhookService subscribed to alerts:triggered");
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ProcessAlertAsync(AlertTriggeredMessage alertMsg, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        var endpoints = await db.WebhookEndpoints
            .Where(e => e.IsActive)
            .ToListAsync(ct);

        var payload = JsonSerializer.Serialize(new
        {
            alertId = alertMsg.AlertId,
            type = alertMsg.Type,
            severity = alertMsg.Severity,
            entityId = alertMsg.EntityId,
            summary = alertMsg.Summary,
            createdAt = alertMsg.CreatedAt
        });

        foreach (var endpoint in endpoints)
        {
            if (!MatchesEventFilter(endpoint, alertMsg.Type))
                continue;

            var delivery = new WebhookDelivery
            {
                EndpointId = endpoint.Id,
                AlertId = alertMsg.AlertId,
                Status = "Pending"
            };
            db.WebhookDeliveries.Add(delivery);
            await db.SaveChangesAsync(ct);

            _ = DeliverWithRetriesAsync(endpoint.Id, delivery.Id, alertMsg.AlertId, payload, endpoint.Url, endpoint.Secret);
        }
    }

    private static bool MatchesEventFilter(WebhookEndpoint endpoint, string alertType)
    {
        if (string.IsNullOrEmpty(endpoint.EventFilter))
            return true;

        try
        {
            using var doc = JsonDocument.Parse(endpoint.EventFilter);
            if (doc.RootElement.TryGetProperty("types", out var typesElement) &&
                typesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in typesElement.EnumerateArray())
                {
                    if (string.Equals(t.GetString(), alertType, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }
        }
        catch
        {
            // If filter is malformed, allow delivery
        }

        return true;
    }

    private async Task DeliverWithRetriesAsync(
        Guid endpointId, long deliveryId, Guid alertId,
        string payload, string url, string secret)
    {
        for (int attempt = 0; attempt <= RetryDelaysSeconds.Length; attempt++)
        {
            if (attempt > 0)
            {
                var delay = RetryDelaysSeconds[Math.Min(attempt - 1, RetryDelaysSeconds.Length - 1)];
                await Task.Delay(TimeSpan.FromSeconds(delay));
            }

            try
            {
                var (success, statusCode, latencyMs) = await SendWebhookAsync(url, secret, payload);

                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

                var delivery = await db.WebhookDeliveries.FindAsync(deliveryId);
                if (delivery is null) return;

                delivery.AttemptCount = attempt + 1;
                delivery.ResponseCode = statusCode;
                delivery.LatencyMs = latencyMs;
                delivery.LastAttemptAt = DateTimeOffset.UtcNow;

                if (success)
                {
                    delivery.Status = "Success";

                    var endpoint = await db.WebhookEndpoints.FindAsync(endpointId);
                    if (endpoint is not null)
                        endpoint.ConsecutiveFailures = 0;

                    await db.SaveChangesAsync();
                    _logger.LogInformation("Webhook delivered to {Url} for alert {AlertId}", url, alertId);
                    return;
                }

                delivery.Status = "Failed";

                var ep = await db.WebhookEndpoints.FindAsync(endpointId);
                if (ep is not null)
                {
                    ep.ConsecutiveFailures++;
                    if (ep.ConsecutiveFailures >= MaxConsecutiveFailures)
                    {
                        ep.IsActive = false;
                        _logger.LogWarning("Webhook endpoint {EndpointId} auto-disabled after {Failures} consecutive failures",
                            endpointId, ep.ConsecutiveFailures);
                    }
                }

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Webhook delivery attempt {Attempt} failed for endpoint {EndpointId}",
                    attempt + 1, endpointId);
            }
        }
    }

    private async Task<(bool Success, int? StatusCode, int LatencyMs)> SendWebhookAsync(
        string url, string secret, string payload)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);

        var signature = ComputeHmacSha256(secret, payload);

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Signature-256", $"sha256={signature}");

        var sw = Stopwatch.StartNew();
        var response = await client.SendAsync(request);
        sw.Stop();

        return (response.IsSuccessStatusCode, (int)response.StatusCode, (int)sw.ElapsedMilliseconds);
    }

    private static string ComputeHmacSha256(string secret, string payload)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var data = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(key, data);
        return Convert.ToHexStringLower(hash);
    }
}
