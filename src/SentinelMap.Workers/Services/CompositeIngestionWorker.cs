using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SentinelMap.Domain.Interfaces;

namespace SentinelMap.Workers.Services;

public class CompositeIngestionWorker : BackgroundService
{
    private readonly IReadOnlyList<ISourceConnector> _connectors;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IngestionWorker> _logger;

    public CompositeIngestionWorker(
        IReadOnlyList<ISourceConnector> connectors,
        IServiceScopeFactory scopeFactory,
        ILogger<IngestionWorker> logger)
    {
        _connectors = connectors;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CompositeIngestionWorker starting {Count} connector(s)", _connectors.Count);

        var tasks = _connectors.Select(connector =>
        {
            var worker = new IngestionWorker(connector, _scopeFactory, _logger);
            return worker.RunAsync(stoppingToken);
        }).ToArray();

        return Task.WhenAll(tasks);
    }
}
