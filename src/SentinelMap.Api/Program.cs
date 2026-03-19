using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SentinelMap.Api.Endpoints;
using SentinelMap.Api.Hubs;
using SentinelMap.Infrastructure.Auth;
using SentinelMap.Infrastructure.Data;
using SentinelMap.Infrastructure.Identity;
using SentinelMap.Infrastructure.Services;
using SentinelMap.SharedKernel.Interfaces;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// --- Database ---
builder.Services.AddDbContext<SentinelMapDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsql => npgsql.UseNetTopologySuite()));

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
}

// --- Middleware ---
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// --- Endpoints ---
app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapHub<TrackHub>("/hubs/tracks");

await SentinelMap.Api.Services.UserSeeder.SeedAsync(app.Services);

app.Run();

// Make Program class accessible for WebApplicationFactory in tests
public partial class Program { }
