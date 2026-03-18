using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SentinelMap.SharedKernel.DTOs;
using SentinelMap.SharedKernel.Interfaces;

namespace SentinelMap.Infrastructure.Services;

public class AuditService : IAuditService, IHostedService
{
    private readonly Channel<AuditEvent> _channel;
    private readonly ILogger<AuditService> _logger;
    private Task? _processingTask;

    public AuditService(ILogger<AuditService> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<AuditEvent>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }

    public async Task WriteSecurityEventAsync(AuditEvent evt)
    {
        _logger.LogInformation("AUDIT [Security] {Action} on {ResourceType}/{ResourceId} by {UserId}",
            evt.Action, evt.ResourceType, evt.ResourceId, evt.UserId);
        // TODO (M5): Write to PostgreSQL audit_events table synchronously
        await Task.CompletedTask;
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
            // TODO (M5): Batch write to PostgreSQL audit_events table
        }
    }
}
