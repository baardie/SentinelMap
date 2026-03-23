using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SentinelMap.Infrastructure.Data;
using SentinelMap.SharedKernel.DTOs;
using SentinelMap.SharedKernel.Interfaces;

namespace SentinelMap.Infrastructure.Services;

public class AuditService : IAuditService, IHostedService
{
    private readonly Channel<AuditEvent> _channel;
    private readonly ILogger<AuditService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private Task? _processingTask;

    public AuditService(ILogger<AuditService> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _channel = Channel.CreateBounded<AuditEvent>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }

    public async Task WriteSecurityEventAsync(AuditEvent evt)
    {
        _logger.LogInformation("AUDIT [Security] {Action} on {ResourceType}/{ResourceId} by {UserId}",
            evt.Action, evt.ResourceType, evt.ResourceId, evt.UserId);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            @"INSERT INTO audit_events (event_type, user_id, action, resource_type, resource_id, details, ip_address, timestamp)
              VALUES ({0}, {1}, {2}, {3}, {4}, {5}::jsonb, {6}::inet, {7})",
            evt.EventType,
            (object?)evt.UserId ?? DBNull.Value,
            evt.Action,
            evt.ResourceType,
            (object?)evt.ResourceId ?? DBNull.Value,
            (object?)(evt.Details is not null ? System.Text.Json.JsonSerializer.Serialize(evt.Details) : null) ?? DBNull.Value,
            (object?)evt.IpAddress?.ToString() ?? DBNull.Value,
            DateTimeOffset.UtcNow);
    }

    public void WriteOperationalEvent(AuditEvent evt)
    {
        if (!_channel.Writer.TryWrite(evt))
        {
            _logger.LogWarning("Audit channel full, dropping operational event: {Action}", evt.Action);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _processingTask = ProcessEventsAsync(cancellationToken);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.Complete();
        if (_processingTask != null)
            await _processingTask;
    }

    private async Task ProcessEventsAsync(CancellationToken ct)
    {
        await foreach (var evt in _channel.Reader.ReadAllAsync(ct))
        {
            _logger.LogInformation("AUDIT [Operational] {Action} on {ResourceType}/{ResourceId} by {UserId}",
                evt.Action, evt.ResourceType, evt.ResourceId, evt.UserId);

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
                await db.Database.ExecuteSqlRawAsync(
                    @"INSERT INTO audit_events (event_type, user_id, action, resource_type, resource_id, details, ip_address, timestamp)
                      VALUES ({0}, {1}, {2}, {3}, {4}, {5}::jsonb, {6}::inet, {7})",
                    evt.EventType,
                    (object?)evt.UserId ?? DBNull.Value,
                    evt.Action,
                    evt.ResourceType,
                    (object?)evt.ResourceId ?? DBNull.Value,
                    (object?)(evt.Details is not null ? System.Text.Json.JsonSerializer.Serialize(evt.Details) : null) ?? DBNull.Value,
                    (object?)evt.IpAddress?.ToString() ?? DBNull.Value,
                    DateTimeOffset.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist operational audit event: {Action}", evt.Action);
            }
        }
    }
}
