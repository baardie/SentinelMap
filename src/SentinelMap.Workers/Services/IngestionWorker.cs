using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SentinelMap.Domain.Interfaces;
using SentinelMap.Infrastructure.Pipeline;

namespace SentinelMap.Workers.Services;

public class IngestionWorker : BackgroundService
{
    private readonly ISourceConnector _connector;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IngestionWorker> _logger;

    private int _consecutiveFailures;
    private DateTimeOffset _circuitOpenedAt = DateTimeOffset.MinValue;

    private const int FailureThreshold = 3;
    private static readonly TimeSpan CircuitOpenDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    public IngestionWorker(ISourceConnector connector, IServiceScopeFactory scopeFactory, ILogger<IngestionWorker> logger)
    {
        _connector = connector;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        RunAsync(stoppingToken);

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IngestionWorker starting for source {SourceId}", _connector.SourceId);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_consecutiveFailures >= FailureThreshold &&
                DateTimeOffset.UtcNow - _circuitOpenedAt < CircuitOpenDuration)
            {
                await Task.Delay(1000, stoppingToken);
                continue;
            }

            try
            {
                await RunConnectorAsync(stoppingToken);
                _consecutiveFailures = 0;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _consecutiveFailures++;
                if (_consecutiveFailures >= FailureThreshold)
                    _circuitOpenedAt = DateTimeOffset.UtcNow;

                var delay = TimeSpan.FromSeconds(
                    Math.Min(MaxBackoff.TotalSeconds, Math.Pow(2, _consecutiveFailures)));

                _logger.LogError(ex, "IngestionWorker error for {SourceId} (failure {Count}), backing off {Delay:F0}s",
                    _connector.SourceId, _consecutiveFailures, delay.TotalSeconds);

                await Task.Delay(delay, stoppingToken);
            }
        }
    }

    private async Task RunConnectorAsync(CancellationToken ct)
    {
        _logger.LogInformation("Connecting to {SourceId}", _connector.SourceId);

        // Each connector run gets a fresh scope — IngestionPipeline is Transient and holds a DbContext.
        using var scope = _scopeFactory.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<IngestionPipeline>();

        await foreach (var observation in _connector.StreamAsync(ct))
        {
            await pipeline.ProcessAsync(observation, ct);
        }
    }
}
