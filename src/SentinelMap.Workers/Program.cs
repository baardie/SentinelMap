using Microsoft.EntityFrameworkCore;
using SentinelMap.Domain.Interfaces;
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

// --- Connector selection based on data mode ---
var dataMode = builder.Configuration["SENTINELMAP_DATA_MODE"]
            ?? Environment.GetEnvironmentVariable("SENTINELMAP_DATA_MODE")
            ?? "Simulated";

builder.Services.AddSingleton<ISourceConnector>(sp =>
{
    return dataMode.ToLowerInvariant() switch
    {
        "live" => new AisStreamConnector(
            Environment.GetEnvironmentVariable("AISSTREAM_API_KEY")
                ?? throw new InvalidOperationException("AISSTREAM_API_KEY required for Live mode"),
            sp.GetRequiredService<ILogger<AisStreamConnector>>()),

        _ => new SimulatedAisConnector()
    };
});

// --- Background Services ---
builder.Services.AddHostedService<IngestionWorker>();
builder.Services.AddHostedService<CorrelationWorker>();

var host = builder.Build();
host.Run();
