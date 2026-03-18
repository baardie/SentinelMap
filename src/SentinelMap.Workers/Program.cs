using Microsoft.EntityFrameworkCore;
using SentinelMap.Infrastructure.Data;

var builder = Host.CreateApplicationBuilder(args);

// --- Database (SystemDbContext — no classification filters) ---
builder.Services.AddDbContext<SystemDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsql => npgsql.UseNetTopologySuite()));

// Background services will be registered here in M2-M4:
// - IngestionWorker (M2)
// - CorrelationWorker (M3)
// - AlertingWorker (M4)

var host = builder.Build();
host.Run();
