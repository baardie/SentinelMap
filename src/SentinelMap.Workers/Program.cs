using Microsoft.EntityFrameworkCore;
using SentinelMap.Domain.Interfaces;
using SentinelMap.Infrastructure.Alerting;
using SentinelMap.Infrastructure.Connectors;
using SentinelMap.Infrastructure.Data;
using SentinelMap.Infrastructure.Pipeline;
using SentinelMap.Infrastructure.Repositories;
using SentinelMap.Workers.Services;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);

// --- Database (SystemDbContext — no classification filters) ---
builder.Services.AddDbContext<SystemDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsql => npgsql.UseNetTopologySuite()),
    ServiceLifetime.Transient);

// --- Redis ---
var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "redis:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(redisConnection));

// --- Repositories and Pipeline ---
builder.Services.AddTransient<IObservationRepository, ObservationRepository>();
builder.Services.AddTransient<IEntityRepository, EntityRepository>();
builder.Services.AddSingleton<IDeduplicationService, RedisDeduplicationService>();
builder.Services.AddSingleton<IObservationPublisher, RedisObservationPublisher>();
builder.Services.AddSingleton<ObservationValidator>();
builder.Services.AddTransient<IngestionPipeline>();

// --- Redis IDatabase (shared singleton, thread-safe) ---
builder.Services.AddSingleton<IDatabase>(sp =>
    sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());

// --- Alert repositories ---
builder.Services.AddTransient<IAlertRepository, AlertRepository>();
builder.Services.AddTransient<IGeofenceRepository, GeofenceRepository>();
builder.Services.AddTransient<IWatchlistRepository, WatchlistRepository>();

// --- Alert rules (resolved via IEnumerable<IAlertRule>) ---
builder.Services.AddTransient<IAlertRule, GeofenceBreachRule>();
builder.Services.AddTransient<IAlertRule, WatchlistMatchRule>();
builder.Services.AddTransient<IAlertRule, SpeedAnomalyRule>();

// --- HttpClient (required for live ADS-B connector) ---
builder.Services.AddHttpClient();

// --- Data mode resolution ---
var globalMode = (builder.Configuration["SENTINELMAP_DATA_MODE"]
              ?? Environment.GetEnvironmentVariable("SENTINELMAP_DATA_MODE")
              ?? "Simulated").ToLowerInvariant();

string ResolveMode(string sourceEnvVar)
{
    var value = Environment.GetEnvironmentVariable(sourceEnvVar)
             ?? builder.Configuration[sourceEnvVar];
    return string.IsNullOrEmpty(value) ? globalMode : value.ToLowerInvariant();
}

var aisMode = ResolveMode("SENTINELMAP_AIS_MODE");
var adsbMode = ResolveMode("SENTINELMAP_ADSB_MODE");

// --- Register AIS connector ---
builder.Services.AddSingleton<ISourceConnector>(sp =>
{
    if (aisMode == "live")
    {
        var apiKey = Environment.GetEnvironmentVariable("AISSTREAM_API_KEY")
            ?? throw new InvalidOperationException("AISSTREAM_API_KEY required for Live AIS mode");
        return new AisStreamConnector(apiKey, sp.GetRequiredService<ILogger<AisStreamConnector>>());
    }
    return new SimulatedAisConnector();
});

// --- Register ADS-B connector ---
builder.Services.AddSingleton<ISourceConnector>(sp =>
{
    if (adsbMode == "live")
    {
        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient();
        return new AdsbLiveConnector(httpClient, 53.38, -3.02, 50,
            sp.GetRequiredService<ILogger<AdsbLiveConnector>>());
    }
    return new SimulatedAdsbConnector();
});

// --- Background Services ---
builder.Services.AddHostedService(sp =>
{
    var connectors = sp.GetServices<ISourceConnector>().ToList();
    return new CompositeIngestionWorker(connectors, sp.GetRequiredService<IServiceScopeFactory>(),
        sp.GetRequiredService<ILogger<IngestionWorker>>());
});
builder.Services.AddHostedService<CorrelationWorker>();
builder.Services.AddHostedService<AlertingWorker>();

var host = builder.Build();
host.Run();
