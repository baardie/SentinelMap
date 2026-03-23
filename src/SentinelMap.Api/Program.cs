using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SentinelMap.Api.Endpoints;
using SentinelMap.Api.Hubs;
using SentinelMap.Domain.Interfaces;
using SentinelMap.Infrastructure.Auth;
using SentinelMap.Infrastructure.Data;
using SentinelMap.Infrastructure.Identity;
using SentinelMap.Infrastructure.Repositories;
using SentinelMap.Infrastructure.Services;
using SentinelMap.SharedKernel.Interfaces;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// --- Database ---
builder.Services.AddDbContext<SentinelMapDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsql => npgsql.UseNetTopologySuite()));

builder.Services.AddDbContext<SystemDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsql => npgsql.UseNetTopologySuite()),
    ServiceLifetime.Transient);

// --- Identity ---
builder.Services.AddIdentityCore<AppIdentityUser>(options =>
    {
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<SentinelMapDbContext>();

// --- JWT Authentication ---
var rsa = RSA.Create();
// In production, load from file. For dev, generate ephemeral key.
var jwtKeyPath = builder.Configuration["Jwt:PrivateKeyPath"];
if (!string.IsNullOrEmpty(jwtKeyPath) && File.Exists(jwtKeyPath))
{
    rsa.ImportFromPem(File.ReadAllText(jwtKeyPath));
}

builder.Services.AddSingleton(rsa);
builder.Services.AddSingleton(sp => new JwtTokenService(
    sp.GetRequiredService<RSA>(),
    builder.Configuration["Jwt:Issuer"] ?? "SentinelMap",
    builder.Configuration["Jwt:Audience"] ?? "SentinelMap"
));

builder.Services.AddTransient<RefreshTokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new RsaSecurityKey(rsa),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

// --- Authorization Policies ---
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("ViewerAccess", p => p.RequireRole("Viewer", "Analyst", "Admin"))
    .AddPolicy("AnalystAccess", p => p.RequireRole("Analyst", "Admin"))
    .AddPolicy("AdminAccess", p => p.RequireRole("Admin"))
    .AddPolicy("ClassifiedAccess", p => p.AddRequirements(new ClassificationRequirement()));

builder.Services.AddScoped<IAuthorizationHandler, ClassificationAuthorizationHandler>();

// --- Services ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUserContext, UserContext>();
builder.Services.AddSingleton<AuditService>();
builder.Services.AddSingleton<IAuditService>(sp => sp.GetRequiredService<AuditService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<AuditService>());

// --- Redis (for SignalR backplane + TrackHubService) ---
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "redis:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(redisConnectionString));

// --- SignalR with Redis backplane ---
builder.Services.AddSignalR()
    .AddStackExchangeRedis(redisConnectionString, options =>
    {
        options.Configuration.ChannelPrefix = RedisChannel.Literal("sentinel");
    });

// --- TrackHubService ---
builder.Services.AddHostedService<TrackHubService>();
builder.Services.AddHostedService<AlertHubService>();

// --- Repositories ---
builder.Services.AddTransient<IGeofenceRepository, GeofenceRepository>();
builder.Services.AddTransient<IWatchlistRepository, WatchlistRepository>();
builder.Services.AddTransient<IAlertRepository, AlertRepository>();

// --- Rate Limiting ---
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;

    options.AddFixedWindowLimiter("auth", o =>
    {
        o.Window = TimeSpan.FromMinutes(1);
        o.PermitLimit = 10;
        o.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("api-read", o =>
    {
        o.Window = TimeSpan.FromMinutes(1);
        o.PermitLimit = 100;
        o.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("api-write", o =>
    {
        o.Window = TimeSpan.FromMinutes(1);
        o.PermitLimit = 30;
        o.QueueLimit = 0;
    });
});

// --- Swagger ---
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// --- CORS ---
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? ["https://localhost"])
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

var app = builder.Build();

// --- Migrations (run before app starts listening) ---
// Note: SentinelMapDbContext requires IUserContext, which defaults to Classification.Official
// when there's no HTTP context. This is safe because migrations don't query filtered tables —
// they only modify schema. The query filter is active but irrelevant during migration.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SentinelMapDbContext>();
    await db.Database.MigrateAsync();

    // Ensure observation partitions exist for today and tomorrow
    var today = DateTimeOffset.UtcNow.Date;
    for (int i = 0; i < 2; i++)
    {
        var date = today.AddDays(i);
        var next = date.AddDays(1);
        var tableName = $"observations_{date:yyyy_MM_dd}";
        var sql = $"CREATE TABLE IF NOT EXISTS {tableName} PARTITION OF observations FOR VALUES FROM ('{date:yyyy-MM-dd}') TO ('{next:yyyy-MM-dd}')";
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    // Ensure audit_events partitioned table and current-month partition exist
    var systemDb = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
    await systemDb.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS audit_events (
            id            BIGINT GENERATED ALWAYS AS IDENTITY,
            timestamp     TIMESTAMPTZ NOT NULL DEFAULT now(),
            event_type    TEXT NOT NULL,
            user_id       UUID,
            action        TEXT NOT NULL,
            resource_type TEXT NOT NULL,
            resource_id   UUID,
            details       JSONB,
            ip_address    INET,
            PRIMARY KEY (id, timestamp)
        ) PARTITION BY RANGE (timestamp);
        """);

    var monthStart = new DateTimeOffset(today.Year, today.Month, 1, 0, 0, 0, TimeSpan.Zero);
    var monthEnd = monthStart.AddMonths(1);
    var partitionName = $"audit_events_{monthStart:yyyy_MM}";
    await systemDb.Database.ExecuteSqlRawAsync(
        $"CREATE TABLE IF NOT EXISTS {partitionName} PARTITION OF audit_events " +
        $"FOR VALUES FROM ('{monthStart:yyyy-MM-dd}') TO ('{monthEnd:yyyy-MM-dd}')");
}

// --- Middleware ---
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// --- Endpoints ---
app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapGeofenceEndpoints();
app.MapWatchlistEndpoints();
app.MapAlertEndpoints();
app.MapMapFeatureEndpoints();
app.MapExportEndpoints();
app.MapAdminEndpoints();
app.MapSystemEndpoints();
app.MapHub<TrackHub>("/hubs/tracks");

await SentinelMap.Api.Services.UserSeeder.SeedAsync(app.Services);

var dataMode = (Environment.GetEnvironmentVariable("SENTINELMAP_DATA_MODE") ?? "Simulated").ToLowerInvariant();
if (dataMode != "live")
{
    await SentinelMap.Api.Services.DemoSeeder.SeedAsync(app.Services);
}

await SentinelMap.Api.Services.StaticFeatureSeeder.SeedAsync(app.Services);

app.Run();

// Make Program class accessible for WebApplicationFactory in tests
public partial class Program { }
