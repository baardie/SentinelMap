# M3: Second Source + Correlation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add ADS-B ingestion (simulated + live via Airplanes.live), aviation track rendering, extend the correlation worker to handle both source types with correct EntityType, and add an entity detail panel — so that `docker compose up` shows both vessels *and* aircraft on the map in Simulated mode.

**Architecture:** The existing ingestion pipeline (Validate → Deduplicate → Persist → Publish) is source-agnostic. M3 adds a second `ISourceConnector` pair (`SimulatedAdsbConnector` + `AdsbLiveConnector`) that flows through the same pipeline. The CorrelationWorker already resolves `observations:*` into entities — it needs to determine EntityType from SourceType. The frontend adds an `AviationTrackLayer` parallel to `MaritimeTrackLayer`, an aircraft SVG icon, and an entity detail sidebar panel. The `useTrackHub` hook already carries `entityType` through TrackUpdate — no hub changes needed.

**Tech Stack:** .NET 9 BackgroundService, System.Net.Http (HttpClient for REST polling), MapLibre GL JS, existing Redis pub/sub + SignalR pipeline.

**Spec:** `docs/superpowers/specs/2026-03-18-sentinelmap-system-design.md`

**Codebase state:** M2 complete. AIS ingestion (simulated + live), ingestion pipeline, correlation worker skeleton, TrackHub + TrackHubService, MaritimeTrackLayer, PMTiles basemap — all working. 37 tests passing.

**IMPORTANT:** All work must stay within `C:\Users\lukeb\source\repos\SentinelMap`.

---

## File Structure (M3 Deliverables)

### New backend files
```
src/SentinelMap.Infrastructure/
└── Connectors/
    ├── SimulatedAdsbConnector.cs    (new)
    └── AdsbLiveConnector.cs         (new)
```

### Modified backend files
```
src/SentinelMap.Workers/
├── Services/
│   ├── IngestionWorker.cs           (refactor: support multiple connectors)
│   └── CorrelationWorker.cs         (set EntityType from SourceType)
└── Program.cs                       (register ADS-B connectors, multiple IngestionWorkers)

src/SentinelMap.Domain/
└── Messages/
    └── EntityUpdatedMessage.cs      (add DisplayName field)
```

### New test files
```
tests/SentinelMap.Infrastructure.Tests/
└── Connectors/
    ├── SimulatedAdsbConnectorTests.cs     (new)
    └── AdsbLiveConnectorTests.cs          (new)
```

### New frontend files
```
client/src/
├── components/map/
│   ├── AviationTrackLayer.tsx       (new)
│   ├── EntityDetailPanel.tsx        (new)
│   └── icons/
│       └── aircraft.ts              (new — SVG as data URL)
└── types/
    └── index.ts                     (add AircraftType)
```

### Modified frontend files
```
client/src/
├── components/map/
│   └── MapContainer.tsx             (add AviationTrackLayer, EntityDetailPanel, click handler)
├── hooks/
│   └── useTrackHub.ts               (pass through displayName from RawData)
└── types/
    └── index.ts                     (extend TrackProperties)
```

---

## Task 1: SimulatedAdsbConnector

**Context:** Mirror the SimulatedAisConnector pattern. Synthetic aircraft over the Liverpool / Mersey area — commercial flights on airways, a holding pattern, a helicopter, a light aircraft. SourceType = "ADSB", ExternalId = ICAO hex (e.g. "4CA123"). RawData JSON includes `displayName` (callsign), `aircraftType`, `altitude` (feet). Simulate realistic cruise speeds (commercial ~450kt, light aircraft ~120kt, helicopter ~100kt). Waypoints should cover approaches to Liverpool John Lennon Airport (EGGP), transiting traffic on airways overhead, and a helicopter operating near the docks.

**Files:**
- Create: `src/SentinelMap.Infrastructure/Connectors/SimulatedAdsbConnector.cs`
- Create: `tests/SentinelMap.Infrastructure.Tests/Connectors/SimulatedAdsbConnectorTests.cs`

- [ ] **Step 1: Write tests**

Create `tests/SentinelMap.Infrastructure.Tests/Connectors/SimulatedAdsbConnectorTests.cs`:

```csharp
using FluentAssertions;
using SentinelMap.Infrastructure.Connectors;

namespace SentinelMap.Infrastructure.Tests.Connectors;

public class SimulatedAdsbConnectorTests
{
    [Fact]
    public void SourceType_Should_Be_ADSB()
    {
        var connector = new SimulatedAdsbConnector();
        connector.SourceType.Should().Be("ADSB");
    }

    [Fact]
    public void SourceId_Should_Be_SimulatedAdsb()
    {
        var connector = new SimulatedAdsbConnector();
        connector.SourceId.Should().Be("simulated-adsb");
    }

    [Fact]
    public async Task StreamAsync_Should_Yield_Observations_With_Correct_Fields()
    {
        var connector = new SimulatedAdsbConnector(updateIntervalMs: 50);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var observations = new List<SentinelMap.Domain.Entities.Observation>();

        await foreach (var obs in connector.StreamAsync(cts.Token))
        {
            observations.Add(obs);
            if (observations.Count >= 5) break;
        }

        observations.Should().HaveCountGreaterThanOrEqualTo(5);
        foreach (var obs in observations)
        {
            obs.SourceType.Should().Be("ADSB");
            obs.ExternalId.Should().NotBeNullOrEmpty();
            obs.Position.Should().NotBeNull();
            obs.Position!.SRID.Should().Be(4326);
            obs.Position.Y.Should().BeInRange(51.0, 55.0);  // UK airspace latitude range
            obs.Position.X.Should().BeInRange(-5.0, 0.0);    // UK airspace longitude range
            obs.Heading.Should().BeInRange(0, 360);
            obs.SpeedMps.Should().BeGreaterThan(0);
            obs.RawData.Should().NotBeNullOrEmpty();
            obs.RawData.Should().Contain("aircraftType");
            obs.RawData.Should().Contain("altitude");
        }
    }

    [Fact]
    public async Task StreamAsync_Should_Produce_Multiple_Unique_Aircraft()
    {
        var connector = new SimulatedAdsbConnector(updateIntervalMs: 50);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var externalIds = new HashSet<string>();

        await foreach (var obs in connector.StreamAsync(cts.Token))
        {
            externalIds.Add(obs.ExternalId);
            if (externalIds.Count >= 6) break;
        }

        externalIds.Should().HaveCountGreaterThanOrEqualTo(6, "there should be multiple distinct aircraft");
    }

    [Fact]
    public async Task StreamAsync_Should_Cancel_Gracefully()
    {
        var connector = new SimulatedAdsbConnector(updateIntervalMs: 50);
        var cts = new CancellationTokenSource();
        cts.CancelAfter(200);

        var count = 0;
        await foreach (var obs in connector.StreamAsync(cts.Token))
        {
            count++;
        }

        count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task StreamAsync_RawData_Should_Contain_DisplayName()
    {
        var connector = new SimulatedAdsbConnector(updateIntervalMs: 50);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await foreach (var obs in connector.StreamAsync(cts.Token))
        {
            obs.RawData.Should().Contain("displayName");
            break;
        }
    }
}
```

Run: `dotnet test tests/SentinelMap.Infrastructure.Tests --filter SimulatedAdsbConnectorTests`
Expected: All tests fail (class doesn't exist yet).

- [ ] **Step 2: Implement SimulatedAdsbConnector**

Create `src/SentinelMap.Infrastructure/Connectors/SimulatedAdsbConnector.cs`. Follow the exact same patterns as `SimulatedAisConnector`:
- Same `VesselBehaviour` enum renamed to `FlightBehaviour` (Transit, Looping, Holding)
- Same waypoint interpolation, heading calculation, position jitter
- Same staggered emission pattern
- `SourceType = "ADSB"`, `SourceId = "simulated-adsb"`
- ExternalIds are ICAO hex codes (e.g. `"4CA123"`, `"407C52"`)

Aircraft definitions (at minimum 8 aircraft):

1. **Ryanair 737** — Inbound to EGGP from SE, descending approach. Callsign "RYR1234". ~180kt approach. Waypoints from overhead Manchester area down the ILS to EGGP runway 27.
2. **easyJet A320** — Departing EGGP to the south. Callsign "EZY5678". ~250kt climb. Waypoints from EGGP climbing out southward.
3. **British Airways A320** — Transiting overhead on airway at FL350. Callsign "BAW456". ~450kt. Waypoints crossing Liverpool Bay NW to SE.
4. **Cargo 747** — Transiting overhead NE to SW at FL380. Callsign "CLX789". ~460kt. Crossing from Yorkshire toward Ireland.
5. **Private Cessna 172** — VFR circuit near Hawarden Airport (EGNR). Callsign "GBXYZ". ~100kt. Looping pattern.
6. **Police helicopter** — Loitering near Liverpool docks / Birkenhead. Callsign "NPAS21". ~80kt. Tight looping pattern.
7. **Coast Guard helicopter** — Operating over Liverpool Bay. Callsign "COASTGD". ~110kt. Search pattern (looping).
8. **Military transport C-130** — Transiting from RAF Woodvale area heading north. Callsign "RRR4501". ~280kt.

RawData JSON should include:
```json
{
    "displayName": "RYR1234",
    "aircraftType": "B738",
    "altitude": 3500,
    "squawk": "7421"
}
```

Altitude should vary realistically:
- Approaching: 10000ft → 0ft
- Departing: 0ft → 25000ft
- Transiting: 35000-38000ft (constant with slight variation)
- Helicopter: 500-2000ft
- Light aircraft: 1500-3000ft

- [ ] **Step 3: Run tests**

Run: `dotnet test tests/SentinelMap.Infrastructure.Tests --filter SimulatedAdsbConnectorTests`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/SentinelMap.Infrastructure/Connectors/SimulatedAdsbConnector.cs tests/SentinelMap.Infrastructure.Tests/Connectors/SimulatedAdsbConnectorTests.cs
git commit -m "feat(m3): add SimulatedAdsbConnector with 8 realistic aircraft over Liverpool"
```

---

## Task 2: AdsbLiveConnector

**Context:** REST polling connector to Airplanes.live API. Endpoint: `GET https://api.airplanes.live/v2/point/{lat}/{lon}/{radius}` where radius is in nautical miles. No auth required. Poll every 5 seconds. Parse the JSON response array into Observation objects. ExternalId = ICAO hex. SourceType = "ADSB".

**Files:**
- Create: `src/SentinelMap.Infrastructure/Connectors/AdsbLiveConnector.cs`
- Create: `tests/SentinelMap.Infrastructure.Tests/Connectors/AdsbLiveConnectorTests.cs`

- [ ] **Step 1: Write tests**

Create `tests/SentinelMap.Infrastructure.Tests/Connectors/AdsbLiveConnectorTests.cs`:

```csharp
using FluentAssertions;
using SentinelMap.Infrastructure.Connectors;

namespace SentinelMap.Infrastructure.Tests.Connectors;

public class AdsbLiveConnectorTests
{
    [Fact]
    public void SourceType_Should_Be_ADSB()
    {
        var connector = new AdsbLiveConnector(
            new HttpClient(), 53.38, -3.02, 50,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AdsbLiveConnector>.Instance);
        connector.SourceType.Should().Be("ADSB");
    }

    [Fact]
    public void SourceId_Should_Be_AdsbLive()
    {
        var connector = new AdsbLiveConnector(
            new HttpClient(), 53.38, -3.02, 50,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AdsbLiveConnector>.Instance);
        connector.SourceId.Should().Be("adsb-airplaneslive");
    }

    // --- ParseAircraft tests (static method, no HTTP needed) ---

    [Fact]
    public void ParseAircraft_Should_Parse_Valid_Aircraft()
    {
        var json = """
        {
            "hex": "407c52",
            "flight": "BAW456 ",
            "lat": 53.38,
            "lon": -3.02,
            "alt_baro": 35000,
            "gs": 450.5,
            "track": 180.5,
            "squawk": "7421",
            "t": "A320",
            "r": "G-EUYA",
            "seen": 1.2
        }
        """;

        var obs = AdsbLiveConnector.ParseAircraft(json);
        obs.Should().NotBeNull();
        obs!.SourceType.Should().Be("ADSB");
        obs.ExternalId.Should().Be("407c52");
        obs.Position.Should().NotBeNull();
        obs.Position!.Y.Should().BeApproximately(53.38, 0.001);
        obs.Position.X.Should().BeApproximately(-3.02, 0.001);
        obs.SpeedMps.Should().BeApproximately(450.5 * 0.514444, 0.1);
        obs.Heading.Should().BeApproximately(180.5, 0.001);
        obs.RawData.Should().Contain("BAW456");
        obs.RawData.Should().Contain("A320");
        obs.RawData.Should().Contain("35000");
    }

    [Fact]
    public void ParseAircraft_Should_Return_Null_For_Missing_Position()
    {
        var json = """{ "hex": "407c52", "flight": "BAW456" }""";
        AdsbLiveConnector.ParseAircraft(json).Should().BeNull();
    }

    [Fact]
    public void ParseAircraft_Should_Return_Null_For_Missing_Hex()
    {
        var json = """{ "lat": 53.38, "lon": -3.02 }""";
        AdsbLiveConnector.ParseAircraft(json).Should().BeNull();
    }

    [Fact]
    public void ParseAircraft_Should_Handle_Ground_Aircraft()
    {
        var json = """
        {
            "hex": "4ca123",
            "flight": "RYR1234",
            "lat": 53.33,
            "lon": -2.85,
            "alt_baro": "ground",
            "gs": 0,
            "track": 270,
            "t": "B738"
        }
        """;

        var obs = AdsbLiveConnector.ParseAircraft(json);
        obs.Should().NotBeNull();
        obs!.RawData.Should().Contain("0"); // altitude 0 for ground
    }

    [Fact]
    public void ParseAircraft_Should_Trim_Callsign_Whitespace()
    {
        var json = """
        {
            "hex": "407c52",
            "flight": "  BAW456  ",
            "lat": 53.38,
            "lon": -3.02,
            "gs": 450,
            "track": 180
        }
        """;

        var obs = AdsbLiveConnector.ParseAircraft(json);
        obs.Should().NotBeNull();
        obs!.RawData.Should().Contain("BAW456");
        obs.RawData.Should().NotContain("  BAW456  ");
    }
}
```

Run: `dotnet test tests/SentinelMap.Infrastructure.Tests --filter AdsbLiveConnectorTests`
Expected: All tests fail.

- [ ] **Step 2: Implement AdsbLiveConnector**

Create `src/SentinelMap.Infrastructure/Connectors/AdsbLiveConnector.cs`:

```csharp
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;

namespace SentinelMap.Infrastructure.Connectors;

/// <summary>
/// Live ADS-B connector via Airplanes.live REST API.
/// Polls GET /v2/point/{lat}/{lon}/{radius} at a configurable interval.
/// No authentication required.
/// </summary>
public class AdsbLiveConnector : ISourceConnector
{
    private const string BaseUrl = "https://api.airplanes.live/v2";
    private const double KnotsToMps = 0.514444;

    private readonly HttpClient _http;
    private readonly double _centreLat;
    private readonly double _centreLon;
    private readonly int _radiusNm;
    private readonly int _pollIntervalMs;
    private readonly ILogger<AdsbLiveConnector> _logger;

    public AdsbLiveConnector(
        HttpClient http,
        double centreLat,
        double centreLon,
        int radiusNm = 50,
        ILogger<AdsbLiveConnector>? logger = null,
        int pollIntervalMs = 5000)
    {
        _http = http;
        _centreLat = centreLat;
        _centreLon = centreLon;
        _radiusNm = radiusNm;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AdsbLiveConnector>.Instance;
        _pollIntervalMs = pollIntervalMs;
    }

    public string SourceId => "adsb-airplaneslive";
    public string SourceType => "ADSB";

    public async IAsyncEnumerable<Observation> StreamAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        _logger.LogInformation("AdsbLiveConnector starting — polling {Url} every {Interval}ms",
            $"{BaseUrl}/point/{_centreLat}/{_centreLon}/{_radiusNm}", _pollIntervalMs);

        while (!ct.IsCancellationRequested)
        {
            var url = $"{BaseUrl}/point/{_centreLat}/{_centreLon}/{_radiusNm}";

            string? responseBody = null;
            try
            {
                responseBody = await _http.GetStringAsync(url, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to poll Airplanes.live");
            }

            if (responseBody is not null)
            {
                var node = JsonNode.Parse(responseBody);
                var acArray = node?["ac"]?.AsArray();

                if (acArray is not null)
                {
                    foreach (var ac in acArray)
                    {
                        if (ac is null) continue;
                        var obs = ParseAircraft(ac.ToJsonString());
                        if (obs is not null) yield return obs;
                    }

                    _logger.LogDebug("Polled {Count} aircraft from Airplanes.live", acArray.Count);
                }
            }

            try { await Task.Delay(_pollIntervalMs, ct); }
            catch (OperationCanceledException) { yield break; }
        }
    }

    /// <summary>
    /// Parses a single aircraft JSON object into an Observation.
    /// Public static for direct unit testing without HTTP.
    /// Returns null if required fields (hex, lat, lon) are missing.
    /// </summary>
    public static Observation? ParseAircraft(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is null) return null;

            var hex = node["hex"]?.GetValue<string>();
            if (string.IsNullOrEmpty(hex)) return null;

            var lat = node["lat"]?.GetValue<double>();
            var lon = node["lon"]?.GetValue<double>();
            if (lat is null || lon is null) return null;

            var gs = node["gs"]?.GetValue<double>() ?? 0;
            var track = node["track"]?.GetValue<double>() ?? 0;
            var callsign = node["flight"]?.GetValue<string>()?.Trim() ?? hex;
            var aircraftType = node["t"]?.GetValue<string>() ?? "";
            var registration = node["r"]?.GetValue<string>() ?? "";
            var squawk = node["squawk"]?.GetValue<string>() ?? "";

            // alt_baro can be a number or "ground"
            int altitude = 0;
            var altNode = node["alt_baro"];
            if (altNode is not null)
            {
                try { altitude = altNode.GetValue<int>(); }
                catch { altitude = 0; } // "ground" or other non-numeric
            }

            return new Observation
            {
                SourceType = "ADSB",
                ExternalId = hex,
                Position = new Point(lon.Value, lat.Value) { SRID = 4326 },
                Heading = track,
                SpeedMps = gs * KnotsToMps,
                ObservedAt = DateTimeOffset.UtcNow,
                RawData = JsonSerializer.Serialize(new
                {
                    displayName = callsign,
                    aircraftType,
                    altitude,
                    registration,
                    squawk,
                }),
            };
        }
        catch
        {
            return null;
        }
    }
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test tests/SentinelMap.Infrastructure.Tests --filter AdsbLiveConnectorTests`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/SentinelMap.Infrastructure/Connectors/AdsbLiveConnector.cs tests/SentinelMap.Infrastructure.Tests/Connectors/AdsbLiveConnectorTests.cs
git commit -m "feat(m3): add AdsbLiveConnector for Airplanes.live REST polling"
```

---

## Task 3: Multi-Connector IngestionWorker + Workers DI

**Context:** Currently, Workers `Program.cs` registers a single `ISourceConnector` and a single `IngestionWorker`. M3 requires two connectors running concurrently (AIS + ADS-B). Refactor to support multiple connectors. Each connector gets its own `IngestionWorker` instance. The data mode logic must support per-source overrides (`SENTINELMAP_AIS_MODE`, `SENTINELMAP_ADSB_MODE`).

**Files:**
- Modify: `src/SentinelMap.Workers/Services/IngestionWorker.cs` (no changes needed — already takes `ISourceConnector`)
- Modify: `src/SentinelMap.Workers/Program.cs` (register both connectors, create two IngestionWorker instances)

- [ ] **Step 1: Refactor IngestionWorker to accept a named connector**

The current `IngestionWorker` takes a single `ISourceConnector` via DI. Since we need two workers, we can't use the default DI pattern. Instead, create named worker instances using a factory pattern.

Edit `src/SentinelMap.Workers/Services/IngestionWorker.cs` — the existing code already accepts `ISourceConnector` in the constructor, so no changes are needed to the class itself. The DI registration in Program.cs will handle creating multiple instances.

- [ ] **Step 2: Update Workers Program.cs**

Edit `src/SentinelMap.Workers/Program.cs` — replace the connector registration and hosted service registration:

```csharp
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

// --- HttpClient for ADS-B live connector ---
builder.Services.AddHttpClient();

// --- Data mode resolution ---
var globalMode = (builder.Configuration["SENTINELMAP_DATA_MODE"]
              ?? Environment.GetEnvironmentVariable("SENTINELMAP_DATA_MODE")
              ?? "Simulated").ToLowerInvariant();

string ResolveMode(string sourceEnvVar) =>
    (Environment.GetEnvironmentVariable(sourceEnvVar)
     ?? builder.Configuration[sourceEnvVar])?.ToLowerInvariant()
    ?? globalMode;

var aisMode = ResolveMode("SENTINELMAP_AIS_MODE");
var adsbMode = ResolveMode("SENTINELMAP_ADSB_MODE");

// --- AIS Connector ---
builder.Services.AddSingleton<ISourceConnector>(sp =>
{
    if (aisMode == "live")
        return new AisStreamConnector(
            Environment.GetEnvironmentVariable("AISSTREAM_API_KEY")
                ?? throw new InvalidOperationException("AISSTREAM_API_KEY required for Live AIS mode"),
            sp.GetRequiredService<ILogger<AisStreamConnector>>());

    return new SimulatedAisConnector();
});

// --- ADS-B Connector ---
builder.Services.AddSingleton<ISourceConnector>(sp =>
{
    if (adsbMode == "live")
        return new AdsbLiveConnector(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
            centreLat: 53.38, centreLon: -3.02, radiusNm: 50,
            sp.GetRequiredService<ILogger<AdsbLiveConnector>>());

    return new SimulatedAdsbConnector();
});

// --- Background Services: one IngestionWorker per connector ---
builder.Services.AddHostedService(sp =>
{
    var connectors = sp.GetServices<ISourceConnector>().ToList();
    // Return a composite hosted service that runs all connectors
    return new CompositeIngestionWorker(connectors, sp.GetRequiredService<IServiceScopeFactory>(),
        sp.GetRequiredService<ILogger<IngestionWorker>>());
});

builder.Services.AddHostedService<CorrelationWorker>();

var host = builder.Build();
host.Run();
```

- [ ] **Step 3: Create CompositeIngestionWorker**

Since `IHostedService` doesn't natively support multiple instances of the same service with different constructor args, create a thin wrapper.

Create a new class in `src/SentinelMap.Workers/Services/CompositeIngestionWorker.cs`:

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SentinelMap.Domain.Interfaces;
using SentinelMap.Infrastructure.Pipeline;

namespace SentinelMap.Workers.Services;

/// <summary>
/// Hosts multiple IngestionWorkers — one per ISourceConnector.
/// Each connector runs in its own async loop with independent circuit breaking.
/// </summary>
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workers = _connectors.Select(c =>
            new IngestionWorker(c, _scopeFactory, _logger));

        // Run all ingestion workers concurrently
        var tasks = workers.Select(w => w.StartAsync(stoppingToken));
        await Task.WhenAll(tasks);
    }
}
```

Actually, the above approach won't work cleanly because `BackgroundService.StartAsync` is designed for the host. Instead, refactor `IngestionWorker` to expose a public `RunAsync` method:

Edit `src/SentinelMap.Workers/Services/IngestionWorker.cs` — extract the core loop into a public method that `CompositeIngestionWorker` can call directly. Keep `IngestionWorker` as a `BackgroundService` for backwards compatibility but also make the core loop callable:

```csharp
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

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => RunAsync(stoppingToken);

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IngestionWorker starting for source {SourceId} ({SourceType})",
            _connector.SourceId, _connector.SourceType);

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

        using var scope = _scopeFactory.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<IngestionPipeline>();

        await foreach (var observation in _connector.StreamAsync(ct))
        {
            await pipeline.ProcessAsync(observation, ct);
        }
    }
}
```

Then `CompositeIngestionWorker`:

```csharp
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CompositeIngestionWorker starting {Count} connectors", _connectors.Count);

        var tasks = _connectors.Select(connector =>
        {
            var worker = new IngestionWorker(connector, _scopeFactory, _logger);
            return worker.RunAsync(stoppingToken);
        });

        await Task.WhenAll(tasks);
    }
}
```

- [ ] **Step 4: Build and verify**

Run:
```bash
dotnet build SentinelMap.slnx
dotnet test tests/SentinelMap.Infrastructure.Tests
dotnet test tests/SentinelMap.Workers.Tests
```
Expected: All pass.

- [ ] **Step 5: Commit**

```bash
git add src/SentinelMap.Workers/ src/SentinelMap.Infrastructure/Connectors/
git commit -m "feat(m3): multi-connector ingestion with CompositeIngestionWorker and per-source data mode"
```

---

## Task 4: CorrelationWorker EntityType Resolution

**Context:** The CorrelationWorker currently hardcodes `EntityType.Vessel` for all entities. It needs to determine entity type from the observation's `SourceType`. Also add `DisplayName` to `EntityUpdatedMessage` so the frontend can show callsigns/vessel names.

**Files:**
- Modify: `src/SentinelMap.Workers/Services/CorrelationWorker.cs`
- Modify: `src/SentinelMap.Domain/Messages/EntityUpdatedMessage.cs`
- Modify: `tests/SentinelMap.Workers.Tests/Services/CorrelationWorkerTests.cs`

- [ ] **Step 1: Add DisplayName to EntityUpdatedMessage**

Edit `src/SentinelMap.Domain/Messages/EntityUpdatedMessage.cs` — add `DisplayName` field:

```csharp
namespace SentinelMap.Domain.Messages;

public record EntityUpdatedMessage(
    Guid EntityId,
    double Longitude,
    double Latitude,
    double? Heading,
    double? Speed,
    string EntityType,
    string Status,
    DateTimeOffset Timestamp,
    string? DisplayName = null);
```

- [ ] **Step 2: Add DisplayName to ObservationPublishedMessage**

Edit `src/SentinelMap.Domain/Messages/ObservationPublishedMessage.cs` — add `DisplayName` field:

```csharp
namespace SentinelMap.Domain.Messages;

public record ObservationPublishedMessage(
    long ObservationId,
    DateTimeOffset ObservedAt,
    string SourceType,
    string ExternalId,
    double Longitude,
    double Latitude,
    double? Heading,
    double? SpeedMps,
    string? DisplayName = null);
```

- [ ] **Step 3: Update RedisObservationPublisher to include DisplayName**

Edit `src/SentinelMap.Infrastructure/Pipeline/RedisObservationPublisher.cs` — extract `displayName` from `RawData` and include it in the published message.

Find where `ObservationPublishedMessage` is constructed and add the displayName extraction:

```csharp
// Extract displayName from RawData JSON
string? displayName = null;
if (!string.IsNullOrEmpty(observation.RawData))
{
    try
    {
        var rawNode = System.Text.Json.Nodes.JsonNode.Parse(observation.RawData);
        displayName = rawNode?["displayName"]?.GetValue<string>();
    }
    catch { /* ignore parse errors */ }
}

var message = new ObservationPublishedMessage(
    observation.Id,
    observation.ObservedAt,
    observation.SourceType,
    observation.ExternalId,
    observation.Position!.X,
    observation.Position.Y,
    observation.Heading,
    observation.SpeedMps,
    displayName);
```

- [ ] **Step 4: Update CorrelationProcessor to resolve EntityType and pass DisplayName**

Edit `src/SentinelMap.Workers/Services/CorrelationWorker.cs`:

In `CorrelationProcessor.ProcessAsync()`:
1. Resolve entity type: `var entityType = msg.SourceType == "ADSB" ? EntityType.Aircraft : EntityType.Vessel;`
2. Set `entity.Type = entityType` when creating new entities
3. Use `entityType.ToString()` in `EntityUpdatedMessage` instead of hardcoded `EntityType.Vessel.ToString()`
4. Set `entity.DisplayName = msg.DisplayName` on new entities
5. Pass `msg.DisplayName` through `EntityUpdatedMessage`

- [ ] **Step 5: Update existing CorrelationWorker tests**

Edit existing tests to account for the new `DisplayName` parameter and EntityType resolution. Add a test that verifies ADSB observations create Aircraft entities.

- [ ] **Step 6: Build and test**

```bash
dotnet build SentinelMap.slnx
dotnet test
```
Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/SentinelMap.Domain/Messages/ src/SentinelMap.Workers/Services/CorrelationWorker.cs src/SentinelMap.Infrastructure/Pipeline/RedisObservationPublisher.cs tests/
git commit -m "feat(m3): resolve EntityType from SourceType and pass DisplayName through pipeline"
```

---

## Task 5: Aircraft SVG Icon + AviationTrackLayer

**Context:** Create an aircraft SVG icon (simplified plane outline, same SDF approach as the vessel icon) and an `AviationTrackLayer` component parallel to `MaritimeTrackLayer`. Filter tracks by `entityType === 'Aircraft'`. Colour: sky blue (#0ea5e9). The layer should render above the maritime layer.

**Files:**
- Create: `client/src/components/map/icons/aircraft.ts`
- Create: `client/src/components/map/AviationTrackLayer.tsx`
- Modify: `client/src/types/index.ts` (add AircraftType)
- Modify: `client/src/components/map/MapContainer.tsx` (add AviationTrackLayer)

- [ ] **Step 1: Create aircraft SVG icon**

Create `client/src/components/map/icons/aircraft.ts`:

The SVG should be a simplified top-down aircraft silhouette (similar to how vessel.ts has a ship outline). 24x32 viewBox, SDF-compatible (single fill colour for recolouring). Design: triangle fuselage pointing up, swept wings, small tail. Keep it simple and recognisable at small sizes.

```typescript
// Simplified aircraft silhouette SVG — SDF-compatible for MapLibre icon recolouring.
// Oriented pointing up (north), rotated by `icon-rotate` to match heading.
const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 32" width="24" height="32">
  <path d="M12 1 L14 12 L23 15 L14 17 L15 28 L12 26 L9 28 L10 17 L1 15 L10 12 Z" fill="white"/>
</svg>`

export const AIRCRAFT_ICON_DATA_URL = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`
```

- [ ] **Step 2: Extend types**

Edit `client/src/types/index.ts` — add aircraft-specific types:

```typescript
export type AircraftType = 'Commercial' | 'Cargo' | 'Private' | 'Military' | 'Helicopter' | 'Unknown'
```

Add `aircraftType` and `displayName` to `TrackProperties`:

```typescript
export interface TrackProperties {
  entityId: string
  heading: number | null
  speed: number | null
  entityType: EntityType
  status: EntityStatus
  vesselType: VesselType
  aircraftType: AircraftType
  displayName: string
  lastUpdated: string
}
```

- [ ] **Step 3: Update useTrackHub to pass displayName**

Edit `client/src/hooks/useTrackHub.ts` — update `trackUpdateToFeature` to include `displayName` from the TrackUpdate. The TrackUpdate already flows through SignalR — but `TrackHubService` needs to pass `DisplayName`. Add `displayName` to the `TrackUpdate` type and the feature converter.

Edit `client/src/types/index.ts` — add `displayName` to `TrackUpdate`:
```typescript
export interface TrackUpdate {
  entityId: string
  position: [number, number]
  heading: number | null
  speed: number | null
  entityType: EntityType
  status: EntityStatus
  timestamp: string
  displayName: string | null
}
```

Edit `client/src/hooks/useTrackHub.ts` — update `trackUpdateToFeature`:
```typescript
function trackUpdateToFeature(update: TrackUpdate): TrackFeature {
  return {
    type: 'Feature',
    geometry: {
      type: 'Point',
      coordinates: update.position,
    },
    properties: {
      entityId: update.entityId,
      heading: update.heading,
      speed: update.speed,
      entityType: update.entityType,
      status: update.status,
      vesselType: 'Unknown',
      aircraftType: 'Unknown',
      displayName: update.displayName ?? '',
      lastUpdated: update.timestamp,
    } satisfies TrackProperties,
  }
}
```

- [ ] **Step 4: Update TrackHubService to pass DisplayName**

Edit `src/SentinelMap.Api/Hubs/TrackHubService.cs` — add `displayName` to the anonymous object sent via SignalR:

```csharp
var trackUpdate = new
{
    entityId = evt.EntityId,
    position = new[] { evt.Longitude, evt.Latitude },
    heading = evt.Heading,
    speed = evt.Speed,
    entityType = evt.EntityType,
    status = evt.Status,
    timestamp = evt.Timestamp,
    displayName = evt.DisplayName,
};
```

- [ ] **Step 5: Create AviationTrackLayer**

Create `client/src/components/map/AviationTrackLayer.tsx`:

```typescript
import { useEffect, useRef } from 'react'
import maplibregl from 'maplibre-gl'
import type { TrackFeature } from '../../types'
import { AIRCRAFT_ICON_DATA_URL } from './icons/aircraft'

const SOURCE_ID = 'aviation-tracks'
const LAYER_ID = 'aviation-track-symbols'
const ICON_ID = 'aircraft-icon'

interface AviationTrackLayerProps {
  map: maplibregl.Map
  tracks: TrackFeature[]
}

export function AviationTrackLayer({ map, tracks }: AviationTrackLayerProps) {
  const iconLoaded = useRef(false)

  // Filter to aircraft only
  const aircraftTracks = tracks.filter(t => t.properties.entityType === 'Aircraft')

  useEffect(() => {
    if (iconLoaded.current || map.hasImage(ICON_ID)) {
      iconLoaded.current = true
      return
    }

    const img = new Image(24, 32)
    img.onload = () => {
      if (!map.hasImage(ICON_ID)) {
        map.addImage(ICON_ID, img, { sdf: true })
        iconLoaded.current = true
      }
    }
    img.src = AIRCRAFT_ICON_DATA_URL
  }, [map])

  useEffect(() => {
    if (map.getSource(SOURCE_ID)) return

    map.addSource(SOURCE_ID, {
      type: 'geojson',
      data: { type: 'FeatureCollection', features: [] },
    })

    map.addLayer({
      id: LAYER_ID,
      type: 'symbol',
      source: SOURCE_ID,
      layout: {
        'icon-image': ICON_ID,
        'icon-size': 0.7,
        'icon-rotate': ['get', 'heading'],
        'icon-rotation-alignment': 'map',
        'icon-allow-overlap': true,
        'text-field': ['get', 'displayName'],
        'text-font': ['Noto Sans Regular'],
        'text-size': 10,
        'text-offset': [0, 1.5],
        'text-optional': true,
      },
      paint: {
        'icon-color': '#0ea5e9', // sky blue for all aircraft
        'icon-opacity': [
          'case',
          ['==', ['get', 'status'], 'Dark'], 0.3,
          1.0,
        ],
        'text-color': '#0ea5e9',
        'text-halo-color': '#0f172a',
        'text-halo-width': 1,
      },
    })

    return () => {
      if (map.getLayer(LAYER_ID)) map.removeLayer(LAYER_ID)
      if (map.getSource(SOURCE_ID)) map.removeSource(SOURCE_ID)
    }
  }, [map])

  useEffect(() => {
    const source = map.getSource(SOURCE_ID) as maplibregl.GeoJSONSource | undefined
    if (!source) return

    source.setData({
      type: 'FeatureCollection',
      features: aircraftTracks,
    })
  }, [map, aircraftTracks])

  return null
}
```

- [ ] **Step 6: Update MaritimeTrackLayer to filter vessels only**

Edit `client/src/components/map/MaritimeTrackLayer.tsx` — filter tracks to `entityType === 'Vessel'` only:

```typescript
// Inside the component, filter to vessels only
const vesselTracks = tracks.filter(t => t.properties.entityType === 'Vessel')
```

Then use `vesselTracks` instead of `tracks` in the data update `useEffect`.

- [ ] **Step 7: Add AviationTrackLayer to MapContainer**

Edit `client/src/components/map/MapContainer.tsx`:

```typescript
import { AviationTrackLayer } from './AviationTrackLayer'

// In the return:
return (
    <div ref={mapContainerRef} className="h-full w-full">
      {map && <MaritimeTrackLayer map={map} tracks={tracks} />}
      {map && <AviationTrackLayer map={map} tracks={tracks} />}
    </div>
)
```

- [ ] **Step 8: Build and verify**

```bash
cd client && npm run build
```
Expected: Build succeeds with no errors.

- [ ] **Step 9: Commit**

```bash
git add client/src/
git commit -m "feat(m3): add AviationTrackLayer with aircraft SVG icon and entity-type filtering"
```

---

## Task 6: Entity Detail Panel

**Context:** Clicking on a vessel or aircraft on the map should open a detail panel showing entity information. The panel slides in from the right side, showing: display name, entity type, coordinates, speed, heading, status, and source-specific metadata (vessel type / aircraft type, altitude for aircraft). This is a simple sidebar panel, not a modal.

**Files:**
- Create: `client/src/components/map/EntityDetailPanel.tsx`
- Modify: `client/src/components/map/MapContainer.tsx` (click handlers, selected entity state)

- [ ] **Step 1: Create EntityDetailPanel**

Create `client/src/components/map/EntityDetailPanel.tsx`:

```typescript
import type { TrackProperties } from '../../types'

interface EntityDetailPanelProps {
  entity: TrackProperties
  onClose: () => void
}

export function EntityDetailPanel({ entity, onClose }: EntityDetailPanelProps) {
  return (
    <div className="absolute right-0 top-0 h-full w-80 bg-slate-900 border-l border-slate-700 p-4 overflow-y-auto z-10">
      <div className="flex justify-between items-center mb-4">
        <h2 className="text-sm font-mono font-semibold text-slate-100 uppercase tracking-wider">
          {entity.entityType === 'Aircraft' ? 'Aircraft Detail' : 'Vessel Detail'}
        </h2>
        <button
          onClick={onClose}
          className="text-slate-400 hover:text-slate-200 text-lg leading-none"
          aria-label="Close detail panel"
        >
          ×
        </button>
      </div>

      <div className="space-y-3">
        <DetailRow label="Name" value={entity.displayName || '—'} mono />
        <DetailRow label="Entity ID" value={entity.entityId} mono />
        <DetailRow label="Type" value={entity.entityType} />
        <DetailRow label="Status" value={entity.status}
          className={entity.status === 'Active' ? 'text-green-400' : entity.status === 'Dark' ? 'text-red-400' : 'text-amber-400'} />

        <div className="border-t border-slate-700 my-2" />

        <DetailRow label="Heading" value={entity.heading != null ? `${entity.heading.toFixed(1)}°` : '—'} />
        <DetailRow label="Speed" value={entity.speed != null ? `${(entity.speed * 1.94384).toFixed(1)} kt` : '—'} />

        <div className="border-t border-slate-700 my-2" />

        {entity.entityType === 'Vessel' && (
          <DetailRow label="Vessel Type" value={entity.vesselType} />
        )}

        <DetailRow label="Last Update" value={new Date(entity.lastUpdated).toLocaleTimeString()} />
      </div>
    </div>
  )
}

function DetailRow({ label, value, mono, className }: {
  label: string
  value: string
  mono?: boolean
  className?: string
}) {
  return (
    <div className="flex justify-between">
      <span className="text-xs text-slate-400 uppercase tracking-wide">{label}</span>
      <span className={`text-sm ${mono ? 'font-mono' : ''} ${className ?? 'text-slate-200'}`}>
        {value}
      </span>
    </div>
  )
}
```

- [ ] **Step 2: Add click handlers and selected state to MapContainer**

Edit `client/src/components/map/MapContainer.tsx`:

1. Add `useState` for selected entity: `const [selectedEntity, setSelectedEntity] = useState<TrackProperties | null>(null)`
2. On map load, register click handlers for both track layers:
   ```typescript
   m.on('click', 'maritime-track-symbols', (e) => {
     if (e.features?.[0]) {
       setSelectedEntity(e.features[0].properties as unknown as TrackProperties)
     }
   })
   m.on('click', 'aviation-track-symbols', (e) => {
     if (e.features?.[0]) {
       setSelectedEntity(e.features[0].properties as unknown as TrackProperties)
     }
   })
   // Change cursor on hover
   m.on('mouseenter', 'maritime-track-symbols', () => { m.getCanvas().style.cursor = 'pointer' })
   m.on('mouseleave', 'maritime-track-symbols', () => { m.getCanvas().style.cursor = '' })
   m.on('mouseenter', 'aviation-track-symbols', () => { m.getCanvas().style.cursor = 'pointer' })
   m.on('mouseleave', 'aviation-track-symbols', () => { m.getCanvas().style.cursor = '' })
   ```
3. Render `EntityDetailPanel` when an entity is selected:
   ```typescript
   return (
     <div ref={mapContainerRef} className="h-full w-full relative">
       {map && <MaritimeTrackLayer map={map} tracks={tracks} />}
       {map && <AviationTrackLayer map={map} tracks={tracks} />}
       {selectedEntity && (
         <EntityDetailPanel entity={selectedEntity} onClose={() => setSelectedEntity(null)} />
       )}
     </div>
   )
   ```

- [ ] **Step 3: Build and verify**

```bash
cd client && npm run build
```
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add client/src/
git commit -m "feat(m3): add EntityDetailPanel sidebar with click-to-select on map tracks"
```

---

## Task 7: Docker Compose + End-to-End Verification

**Context:** Rebuild the Docker stack and verify both AIS and ADS-B simulated tracks appear on the map. Verify the entity detail panel works. Fix any integration issues.

- [ ] **Step 1: Build solution**

```bash
dotnet build SentinelMap.slnx
```
Expected: 0 errors.

- [ ] **Step 2: Run full test suite**

```bash
dotnet test SentinelMap.slnx
```
Expected: All tests pass.

- [ ] **Step 3: Frontend build**

```bash
cd client && npm run build
```
Expected: Build succeeds.

- [ ] **Step 4: Docker compose rebuild**

```bash
docker compose up --build -d
```
Expected: All services start. API healthy. Workers running two ingestion loops (AIS + ADSB).

- [ ] **Step 5: Verify logs**

```bash
docker compose logs workers --tail 30
```
Expected: See both `IngestionWorker starting for source simulated-ais (AIS)` and `IngestionWorker starting for source simulated-adsb (ADSB)` log lines.

- [ ] **Step 6: Verify UI**

Open `http://localhost` in browser:
1. Vessel icons (ships) visible on the Mersey — same as M2
2. Aircraft icons (planes) visible over Liverpool area — sky blue colour
3. Click on a vessel → EntityDetailPanel opens showing vessel details
4. Click on an aircraft → EntityDetailPanel opens showing aircraft details
5. Close panel via × button
6. Both types update in real-time (positions change)

- [ ] **Step 7: Fix any issues found**

Address any build, runtime, or display issues discovered during E2E verification.

- [ ] **Step 8: Final commit if needed**

```bash
git add -A
git commit -m "fix(m3): resolve integration issues from E2E verification"
```

---

## Verification Checklist

At the end of M3, the following should be true:

- [ ] `dotnet build SentinelMap.slnx` — 0 errors
- [ ] `dotnet test SentinelMap.slnx` — all tests pass
- [ ] `cd client && npm run build` — succeeds
- [ ] `docker compose up --build` — all 6 services healthy
- [ ] Workers log shows both AIS and ADSB ingestion workers running
- [ ] Browser shows vessel tracks (ship icons) on the Mersey
- [ ] Browser shows aircraft tracks (plane icons) over Liverpool
- [ ] Clicking a track opens the EntityDetailPanel with correct data
- [ ] SignalR connection is stable (no 502s, no reconnect loops)
- [ ] Entity types are correctly resolved (Vessel for AIS, Aircraft for ADSB)
