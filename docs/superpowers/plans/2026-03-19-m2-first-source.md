# M2: First Source Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement AIS ingestion (simulated + live), the ingestion pipeline, a correlation worker skeleton, SignalR track hub, and maritime track rendering — so that `docker compose up` shows vessels moving on the map in Simulated mode with no API keys required.

**Architecture:** Workers host runs IngestionWorker (connector → pipeline → Redis pub/sub) and CorrelationWorker skeleton (hot-path cache → entity upsert → publish `entities:updated`). API host runs TrackHubService (Redis subscriber → SignalR broadcast). React client connects to `/hubs/tracks` and renders vessel tracks via MaritimeTrackLayer. PMTiles protocol handler added for dark vector basemap.

**Tech Stack:** .NET 9 BackgroundService, StackExchange.Redis, FluentValidation, System.Net.WebSockets, Microsoft.AspNetCore.SignalR + Redis backplane, MapLibre GL JS, pmtiles npm package, @microsoft/signalr.

**Spec:** `docs/superpowers/specs/2026-03-18-sentinelmap-system-design.md`

**Codebase state:** M1 complete. All packages already installed: StackExchange.Redis + FluentValidation in Infrastructure; Microsoft.AspNetCore.SignalR.StackExchangeRedis in Api; pmtiles + @microsoft/signalr in client.

**IMPORTANT:** All work must stay within `C:\Users\lukeb\source\repos\SentinelMap`.

---

## File Structure (M2 Deliverables)

### New backend files
```
src/SentinelMap.Domain/
├── Interfaces/
│   ├── IObservationRepository.cs    (new)
│   ├── IDeduplicationService.cs     (new)
│   └── IObservationPublisher.cs     (new)
└── Messages/
    ├── ObservationPublishedMessage.cs   (new)
    └── EntityUpdatedMessage.cs          (new)

src/SentinelMap.Infrastructure/
├── Connectors/
│   ├── SimulatedAisConnector.cs     (new)
│   └── AisStreamConnector.cs        (new)
├── Pipeline/
│   ├── ObservationValidator.cs      (new)
│   ├── RedisDeduplicationService.cs (new)
│   ├── RedisObservationPublisher.cs (new)
│   └── IngestionPipeline.cs         (new)
└── Repositories/
    ├── ObservationRepository.cs     (new)
    └── EntityRepository.cs          (new)

src/SentinelMap.Workers/
└── Services/
    ├── IngestionWorker.cs           (new)
    └── CorrelationWorker.cs         (new)

src/SentinelMap.Api/
├── Hubs/
│   ├── TrackHub.cs                  (new)
│   └── TrackHubService.cs           (new)
└── Services/
    └── UserSeeder.cs                (new)

# Modified
src/SentinelMap.Workers/Program.cs  (add Redis, workers, connector DI)
src/SentinelMap.Api/Program.cs      (add SignalR, hub route, seed users, SignalR JWT)
```

### New test files
```
tests/SentinelMap.Infrastructure.Tests/
├── Pipeline/
│   ├── ObservationValidatorTests.cs     (new)
│   ├── RedisDeduplicationServiceTests.cs (new)
│   ├── RedisObservationPublisherTests.cs (new)
│   └── IngestionPipelineTests.cs        (new)
└── Connectors/
    ├── SimulatedAisConnectorTests.cs    (new)
    └── AisStreamConnectorTests.cs       (new)

tests/SentinelMap.Workers.Tests/         (new project)
├── SentinelMap.Workers.Tests.csproj
└── Services/
    └── CorrelationWorkerTests.cs        (new)
```

### New frontend files
```
client/src/
├── hooks/
│   └── useTrackHub.ts               (new)
└── components/map/
    ├── MaritimeTrackLayer.tsx        (new)
    └── icons/
        └── vessel.ts                (new — SVG as data URL)

# Modified
client/src/components/map/MapContainer.tsx  (PMTiles protocol + track layer + hook)
client/src/types/index.ts                   (extend TrackUpdate + TrackFeature)
```

### New scripts
```
scripts/
└── download-pmtiles.sh              (new)
```

---

## Task 1: Test Project Setup + Domain Message Types

**Files:**
- Modify: `tests/SentinelMap.Infrastructure.Tests/SentinelMap.Infrastructure.Tests.csproj`
- Create: `tests/SentinelMap.Workers.Tests/SentinelMap.Workers.Tests.csproj`
- Create: `src/SentinelMap.Domain/Messages/ObservationPublishedMessage.cs`
- Create: `src/SentinelMap.Domain/Messages/EntityUpdatedMessage.cs`
- Create: `src/SentinelMap.Domain/Interfaces/IObservationRepository.cs`
- Create: `src/SentinelMap.Domain/Interfaces/IDeduplicationService.cs`
- Create: `src/SentinelMap.Domain/Interfaces/IObservationPublisher.cs`

- [ ] **Step 1: Add Moq to Infrastructure.Tests**

Edit `tests/SentinelMap.Infrastructure.Tests/SentinelMap.Infrastructure.Tests.csproj` — add Moq:

```xml
<PackageReference Include="Moq" Version="4.20.72" />
```

Run:
```bash
cd C:/Users/lukeb/source/repos/SentinelMap
dotnet restore tests/SentinelMap.Infrastructure.Tests/SentinelMap.Infrastructure.Tests.csproj
```
Expected: Restored successfully.

- [ ] **Step 2: Create Workers.Tests project**

```bash
cd C:/Users/lukeb/source/repos/SentinelMap
dotnet new xunit -n SentinelMap.Workers.Tests -o tests/SentinelMap.Workers.Tests --framework net9.0
dotnet sln SentinelMap.slnx add tests/SentinelMap.Workers.Tests/SentinelMap.Workers.Tests.csproj
```

Edit `tests/SentinelMap.Workers.Tests/SentinelMap.Workers.Tests.csproj` — replace contents:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.2" />
    <PackageReference Include="FluentAssertions" Version="8.9.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="Moq" Version="4.20.72" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\SentinelMap.Workers\SentinelMap.Workers.csproj" />
    <ProjectReference Include="..\..\src\SentinelMap.Infrastructure\SentinelMap.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create domain interfaces**

`src/SentinelMap.Domain/Interfaces/IObservationRepository.cs`:
```csharp
using SentinelMap.Domain.Entities;

namespace SentinelMap.Domain.Interfaces;

public interface IObservationRepository
{
    Task AddAsync(Observation observation, CancellationToken ct = default);
    Task<Observation?> GetByIdAsync(long id, DateTimeOffset observedAt, CancellationToken ct = default);
}
```

`src/SentinelMap.Domain/Interfaces/IDeduplicationService.cs`:
```csharp
namespace SentinelMap.Domain.Interfaces;

/// <summary>
/// Returns true if the key was already seen within the TTL (duplicate).
/// Returns false if this is the first occurrence (and records it).
/// </summary>
public interface IDeduplicationService
{
    Task<bool> IsDuplicateAsync(string key, TimeSpan ttl, CancellationToken ct = default);
}
```

`src/SentinelMap.Domain/Interfaces/IObservationPublisher.cs`:
```csharp
using SentinelMap.Domain.Entities;

namespace SentinelMap.Domain.Interfaces;

public interface IObservationPublisher
{
    Task PublishAsync(Observation observation, CancellationToken ct = default);
}
```

- [ ] **Step 4: Create domain message records**

`src/SentinelMap.Domain/Messages/ObservationPublishedMessage.cs`:
```csharp
namespace SentinelMap.Domain.Messages;

/// <summary>
/// Published to Redis channel "observations:{sourceType}" after an observation is persisted.
/// Contains enough data for CorrelationWorker to skip a DB round-trip on the hot path.
/// </summary>
public record ObservationPublishedMessage(
    long ObservationId,
    DateTimeOffset ObservedAt,   // needed to query the partition
    string SourceType,
    string ExternalId,
    double Longitude,
    double Latitude,
    double? Heading,
    double? SpeedMps);
```

`src/SentinelMap.Domain/Messages/EntityUpdatedMessage.cs`:
```csharp
namespace SentinelMap.Domain.Messages;

/// <summary>
/// Published to Redis channel "entities:updated" by CorrelationWorker.
/// Consumed by TrackHubService to push TrackUpdate events to SignalR clients.
/// </summary>
public record EntityUpdatedMessage(
    Guid EntityId,
    double Longitude,
    double Latitude,
    double? Heading,
    double? Speed,
    string EntityType,
    string Status,
    DateTimeOffset Timestamp);
```

- [ ] **Step 5: Verify build**

```bash
cd C:/Users/lukeb/source/repos/SentinelMap
dotnet build SentinelMap.slnx
```
Expected: 0 Error(s).

- [ ] **Step 6: Commit**

```bash
git add src/SentinelMap.Domain/ tests/SentinelMap.Infrastructure.Tests/SentinelMap.Infrastructure.Tests.csproj tests/SentinelMap.Workers.Tests/
git commit -m "feat(m2): add domain interfaces, message types, and Workers.Tests project"
```

---

## Task 2: ObservationValidator

**Files:**
- Create: `src/SentinelMap.Infrastructure/Pipeline/ObservationValidator.cs`
- Create: `tests/SentinelMap.Infrastructure.Tests/Pipeline/ObservationValidatorTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/SentinelMap.Infrastructure.Tests/Pipeline/ObservationValidatorTests.cs`:
```csharp
using FluentAssertions;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Infrastructure.Pipeline;

namespace SentinelMap.Infrastructure.Tests.Pipeline;

public class ObservationValidatorTests
{
    private readonly ObservationValidator _validator = new();

    private static Observation ValidObservation() => new()
    {
        SourceType = "AIS",
        ExternalId = "235009888",
        Position = new Point(-1.5, 51.0) { SRID = 4326 },
        ObservedAt = DateTimeOffset.UtcNow.AddSeconds(-10),
    };

    [Fact]
    public async Task ValidObservation_PassesValidation()
    {
        var result = await _validator.ValidateAsync(ValidObservation());
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task MissingExternalId_FailsValidation()
    {
        var obs = ValidObservation();
        obs.ExternalId = "";
        var result = await _validator.ValidateAsync(obs);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(Observation.ExternalId));
    }

    [Fact]
    public async Task MissingSourceType_FailsValidation()
    {
        var obs = ValidObservation();
        obs.SourceType = "";
        var result = await _validator.ValidateAsync(obs);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task NullPosition_FailsValidation()
    {
        var obs = ValidObservation();
        obs.Position = null;
        var result = await _validator.ValidateAsync(obs);
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(-91.0, 0.0)]
    [InlineData(91.0, 0.0)]
    [InlineData(0.0, -181.0)]
    [InlineData(0.0, 181.0)]
    public async Task OutOfBoundsPosition_FailsValidation(double lat, double lon)
    {
        var obs = ValidObservation();
        obs.Position = new Point(lon, lat) { SRID = 4326 };
        var result = await _validator.ValidateAsync(obs);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task FutureTimestamp_FailsValidation()
    {
        var obs = ValidObservation();
        obs.ObservedAt = DateTimeOffset.UtcNow.AddMinutes(2);
        var result = await _validator.ValidateAsync(obs);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task StaleTimestamp_FailsValidation()
    {
        var obs = ValidObservation();
        obs.ObservedAt = DateTimeOffset.UtcNow.AddHours(-25);
        var result = await _validator.ValidateAsync(obs);
        result.IsValid.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

```bash
cd C:/Users/lukeb/source/repos/SentinelMap
dotnet test tests/SentinelMap.Infrastructure.Tests/ --filter "Pipeline.ObservationValidatorTests" -v minimal 2>&1 | tail -5
```
Expected: build error (ObservationValidator not found).

- [ ] **Step 3: Implement ObservationValidator**

`src/SentinelMap.Infrastructure/Pipeline/ObservationValidator.cs`:
```csharp
using FluentValidation;
using SentinelMap.Domain.Entities;

namespace SentinelMap.Infrastructure.Pipeline;

public class ObservationValidator : AbstractValidator<Observation>
{
    private static readonly TimeSpan FutureSkew = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxStaleness = TimeSpan.FromHours(24);

    public ObservationValidator()
    {
        RuleFor(o => o.SourceType).NotEmpty();
        RuleFor(o => o.ExternalId).NotEmpty();

        RuleFor(o => o.Position).NotNull();

        When(o => o.Position is not null, () =>
        {
            RuleFor(o => o.Position!.Y)   // Y = latitude in NetTopologySuite
                .InclusiveBetween(-90.0, 90.0)
                .WithName("Latitude");

            RuleFor(o => o.Position!.X)   // X = longitude
                .InclusiveBetween(-180.0, 180.0)
                .WithName("Longitude");
        });

        RuleFor(o => o.ObservedAt)
            .Must(t => t <= DateTimeOffset.UtcNow.Add(FutureSkew))
            .WithMessage("ObservedAt is in the future.")
            .Must(t => t >= DateTimeOffset.UtcNow - MaxStaleness)
            .WithMessage("ObservedAt is more than 24h stale.");
    }
}
```

- [ ] **Step 4: Run tests — verify they pass**

```bash
dotnet test tests/SentinelMap.Infrastructure.Tests/ --filter "Pipeline.ObservationValidatorTests" -v minimal
```
Expected: 10 passed (the Theory with 4 InlineData rows counts as 4 test cases).

- [ ] **Step 5: Commit**

```bash
git add src/SentinelMap.Infrastructure/Pipeline/ObservationValidator.cs tests/SentinelMap.Infrastructure.Tests/Pipeline/ObservationValidatorTests.cs
git commit -m "feat(m2): add ObservationValidator with FluentValidation rules"
```

---

## Task 3: RedisDeduplicationService

**Files:**
- Create: `src/SentinelMap.Infrastructure/Pipeline/RedisDeduplicationService.cs`
- Create: `src/SentinelMap.Infrastructure/Pipeline/InMemoryDeduplicationService.cs` (test double)
- Create: `tests/SentinelMap.Infrastructure.Tests/Pipeline/RedisDeduplicationServiceTests.cs`

**Dedup key format:** `dedup:{sourceType}:{externalId}:{lat4dp}:{lon4dp}:{minuteBucket}`
- Lat/lon truncated to 4dp (~11m precision) prevents micro-jitter from defeating dedup
- Minute bucket (Unix epoch / 60) gives 60s windows

- [ ] **Step 1: Write the failing tests**

`tests/SentinelMap.Infrastructure.Tests/Pipeline/RedisDeduplicationServiceTests.cs`:
```csharp
using FluentAssertions;
using SentinelMap.Infrastructure.Pipeline;

namespace SentinelMap.Infrastructure.Tests.Pipeline;

public class RedisDeduplicationServiceTests
{
    private readonly InMemoryDeduplicationService _sut = new();

    [Fact]
    public async Task FirstOccurrence_ReturnsFalse()
    {
        var result = await _sut.IsDuplicateAsync("key1", TimeSpan.FromMinutes(1));
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SecondOccurrenceWithinTtl_ReturnsTrue()
    {
        await _sut.IsDuplicateAsync("key1", TimeSpan.FromMinutes(1));
        var result = await _sut.IsDuplicateAsync("key1", TimeSpan.FromMinutes(1));
        result.Should().BeTrue();
    }

    [Fact]
    public async Task DifferentKeys_BothReturnFalse()
    {
        var r1 = await _sut.IsDuplicateAsync("key1", TimeSpan.FromMinutes(1));
        var r2 = await _sut.IsDuplicateAsync("key2", TimeSpan.FromMinutes(1));
        r1.Should().BeFalse();
        r2.Should().BeFalse();
    }

    [Fact]
    public async Task ObservationDeduplicationKey_SamePositionSameBucket_IsDuplicate()
    {
        // Arrange — same observation from the same source
        var key = RedisDeduplicationService.BuildKey("AIS", "235009888", 51.1234, -1.5678, DateTimeOffset.UtcNow);
        await _sut.IsDuplicateAsync(key, TimeSpan.FromMinutes(1));

        // Same key — should be a duplicate
        var key2 = RedisDeduplicationService.BuildKey("AIS", "235009888", 51.12345, -1.56789, DateTimeOffset.UtcNow);
        var result = await _sut.IsDuplicateAsync(key2, TimeSpan.FromMinutes(1));

        result.Should().BeTrue("micro-jitter within 4dp precision maps to the same key");
    }

    [Fact]
    public async Task ObservationDeduplicationKey_DifferentPosition_IsNotDuplicate()
    {
        var key1 = RedisDeduplicationService.BuildKey("AIS", "235009888", 51.1234, -1.5678, DateTimeOffset.UtcNow);
        var key2 = RedisDeduplicationService.BuildKey("AIS", "235009888", 52.0000, -2.0000, DateTimeOffset.UtcNow);

        await _sut.IsDuplicateAsync(key1, TimeSpan.FromMinutes(1));
        var result = await _sut.IsDuplicateAsync(key2, TimeSpan.FromMinutes(1));

        result.Should().BeFalse("genuinely different position is not a duplicate");
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

```bash
dotnet test tests/SentinelMap.Infrastructure.Tests/ --filter "Pipeline.RedisDeduplicationServiceTests" -v minimal 2>&1 | tail -5
```
Expected: build error.

- [ ] **Step 3: Implement**

`src/SentinelMap.Infrastructure/Pipeline/RedisDeduplicationService.cs`:
```csharp
using SentinelMap.Domain.Interfaces;
using StackExchange.Redis;

namespace SentinelMap.Infrastructure.Pipeline;

public class RedisDeduplicationService : IDeduplicationService
{
    private readonly IDatabase _db;

    public RedisDeduplicationService(IConnectionMultiplexer redis)
    {
        _db = redis.GetDatabase();
    }

    public async Task<bool> IsDuplicateAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        // SET key 1 NX EX ttl — returns true if key was new (first occurrence)
        // We invert: if NOT set (key existed) → it's a duplicate
        var wasNew = await _db.StringSetAsync(key, 1, ttl, When.NotExists);
        return !wasNew;
    }

    /// <summary>
    /// Builds a dedup key for an AIS/ADS-B observation.
    /// Lat/lon truncated to 4 decimal places (~11m) prevents micro-jitter from creating spurious new keys.
    /// 60-second buckets prevent identical static-vessel reports from flooding storage.
    /// </summary>
    public static string BuildKey(string sourceType, string externalId, double lat, double lon, DateTimeOffset timestamp)
    {
        var lat4 = Math.Round(lat, 4).ToString("F4");
        var lon4 = Math.Round(lon, 4).ToString("F4");
        var bucket = timestamp.ToUnixTimeSeconds() / 60;
        return $"dedup:{sourceType}:{externalId}:{lat4}:{lon4}:{bucket}";
    }
}
```

`src/SentinelMap.Infrastructure/Pipeline/InMemoryDeduplicationService.cs`:
```csharp
using SentinelMap.Domain.Interfaces;

namespace SentinelMap.Infrastructure.Pipeline;

/// <summary>Test double for IDeduplicationService. Not for production use.</summary>
public class InMemoryDeduplicationService : IDeduplicationService
{
    private readonly Dictionary<string, DateTimeOffset> _seen = new();

    public Task<bool> IsDuplicateAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (_seen.TryGetValue(key, out var expiry) && expiry > now)
            return Task.FromResult(true);

        _seen[key] = now.Add(ttl);
        return Task.FromResult(false);
    }
}
```

- [ ] **Step 4: Run tests — verify they pass**

```bash
dotnet test tests/SentinelMap.Infrastructure.Tests/ --filter "Pipeline.RedisDeduplicationServiceTests" -v minimal
```
Expected: 5 passed.

- [ ] **Step 5: Commit**

```bash
git add src/SentinelMap.Infrastructure/Pipeline/RedisDeduplicationService.cs src/SentinelMap.Infrastructure/Pipeline/InMemoryDeduplicationService.cs tests/SentinelMap.Infrastructure.Tests/Pipeline/RedisDeduplicationServiceTests.cs
git commit -m "feat(m2): add RedisDeduplicationService with 4dp position + 60s bucket key strategy"
```

---

## Task 4: ObservationRepository + RedisObservationPublisher

**Files:**
- Create: `src/SentinelMap.Infrastructure/Repositories/ObservationRepository.cs`
- Create: `src/SentinelMap.Infrastructure/Pipeline/RedisObservationPublisher.cs`
- Create: `tests/SentinelMap.Infrastructure.Tests/Pipeline/RedisObservationPublisherTests.cs`

- [ ] **Step 1: Write publisher tests**

`tests/SentinelMap.Infrastructure.Tests/Pipeline/RedisObservationPublisherTests.cs`:
```csharp
using FluentAssertions;
using Moq;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Infrastructure.Pipeline;
using StackExchange.Redis;

namespace SentinelMap.Infrastructure.Tests.Pipeline;

public class RedisObservationPublisherTests
{
    [Fact]
    public async Task Publish_SendsToCorrectChannel()
    {
        // Arrange
        var mockSubscriber = new Mock<ISubscriber>();
        var mockMultiplexer = new Mock<IConnectionMultiplexer>();
        mockMultiplexer.Setup(m => m.GetSubscriber(It.IsAny<object>())).Returns(mockSubscriber.Object);

        var publisher = new RedisObservationPublisher(mockMultiplexer.Object);

        var observation = new Observation
        {
            Id = 1,
            ObservedAt = DateTimeOffset.UtcNow,
            SourceType = "AIS",
            ExternalId = "235009888",
            Position = new Point(-1.5, 51.0) { SRID = 4326 },
            Heading = 90.0,
            SpeedMps = 6.0,
        };

        // Act
        await publisher.PublishAsync(observation);

        // Assert — published to the correct channel
        mockSubscriber.Verify(
            s => s.PublishAsync(
                It.Is<RedisChannel>(c => c.ToString() == "observations:AIS"),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task Publish_MessageContainsExternalId()
    {
        // Arrange
        RedisValue capturedMessage = default;
        var mockSubscriber = new Mock<ISubscriber>();
        mockSubscriber
            .Setup(s => s.PublishAsync(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((_, msg, _) => capturedMessage = msg)
            .ReturnsAsync(1L);

        var mockMultiplexer = new Mock<IConnectionMultiplexer>();
        mockMultiplexer.Setup(m => m.GetSubscriber(It.IsAny<object>())).Returns(mockSubscriber.Object);

        var publisher = new RedisObservationPublisher(mockMultiplexer.Object);
        var observation = new Observation
        {
            Id = 42,
            ObservedAt = DateTimeOffset.UtcNow,
            SourceType = "AIS",
            ExternalId = "235009888",
            Position = new Point(-1.5, 51.0) { SRID = 4326 },
        };

        // Act
        await publisher.PublishAsync(observation);

        // Assert — message JSON contains the externalId
        capturedMessage.ToString().Should().Contain("235009888");
        capturedMessage.ToString().Should().Contain("42");
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

```bash
dotnet test tests/SentinelMap.Infrastructure.Tests/ --filter "Pipeline.RedisObservationPublisherTests" -v minimal 2>&1 | tail -5
```
Expected: build error.

- [ ] **Step 3: Implement ObservationRepository**

`src/SentinelMap.Infrastructure/Repositories/ObservationRepository.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;
using SentinelMap.Infrastructure.Data;

namespace SentinelMap.Infrastructure.Repositories;

public class ObservationRepository : IObservationRepository
{
    private readonly SystemDbContext _db;

    public ObservationRepository(SystemDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(Observation observation, CancellationToken ct = default)
    {
        _db.Observations.Add(observation);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<Observation?> GetByIdAsync(long id, DateTimeOffset observedAt, CancellationToken ct = default)
    {
        // Must include observedAt for partition pruning on the composite PK
        return await _db.Observations
            .Where(o => o.Id == id && o.ObservedAt == observedAt)
            .FirstOrDefaultAsync(ct);
    }
}
```

- [ ] **Step 4: Implement RedisObservationPublisher**

`src/SentinelMap.Infrastructure/Pipeline/RedisObservationPublisher.cs`:
```csharp
using System.Text.Json;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;
using SentinelMap.Domain.Messages;
using StackExchange.Redis;

namespace SentinelMap.Infrastructure.Pipeline;

public class RedisObservationPublisher : IObservationPublisher
{
    private readonly ISubscriber _subscriber;

    public RedisObservationPublisher(IConnectionMultiplexer redis)
    {
        _subscriber = redis.GetSubscriber();
    }

    public async Task PublishAsync(Observation observation, CancellationToken ct = default)
    {
        var message = new ObservationPublishedMessage(
            ObservationId: observation.Id,
            ObservedAt: observation.ObservedAt,
            SourceType: observation.SourceType,
            ExternalId: observation.ExternalId,
            Longitude: observation.Position?.X ?? 0,
            Latitude: observation.Position?.Y ?? 0,
            Heading: observation.Heading,
            SpeedMps: observation.SpeedMps);

        var json = JsonSerializer.Serialize(message);
        var channel = RedisChannel.Literal($"observations:{observation.SourceType}");
        await _subscriber.PublishAsync(channel, json);
    }
}
```

- [ ] **Step 5: Run tests — verify they pass**

```bash
dotnet test tests/SentinelMap.Infrastructure.Tests/ --filter "Pipeline.RedisObservationPublisherTests" -v minimal
```
Expected: 2 passed.

- [ ] **Step 6: Commit**

```bash
git add src/SentinelMap.Infrastructure/Repositories/ObservationRepository.cs src/SentinelMap.Infrastructure/Pipeline/RedisObservationPublisher.cs tests/SentinelMap.Infrastructure.Tests/Pipeline/RedisObservationPublisherTests.cs
git commit -m "feat(m2): add ObservationRepository and RedisObservationPublisher"
```

---

## Task 5: IngestionPipeline

**Files:**
- Create: `src/SentinelMap.Infrastructure/Pipeline/IngestionPipeline.cs`
- Create: `tests/SentinelMap.Infrastructure.Tests/Pipeline/IngestionPipelineTests.cs`

The pipeline orchestrates: Validate → Deduplicate → Persist → Publish. Each stage is injected via interfaces so the pipeline is fully testable with mocks.

- [ ] **Step 1: Write the failing tests**

`tests/SentinelMap.Infrastructure.Tests/Pipeline/IngestionPipelineTests.cs`:
```csharp
using FluentAssertions;
using Moq;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;
using SentinelMap.Infrastructure.Pipeline;

namespace SentinelMap.Infrastructure.Tests.Pipeline;

public class IngestionPipelineTests
{
    private readonly ObservationValidator _validator = new();
    private readonly InMemoryDeduplicationService _dedup = new();
    private readonly Mock<IObservationRepository> _repo = new();
    private readonly Mock<IObservationPublisher> _publisher = new();

    private IngestionPipeline BuildPipeline() =>
        new(_validator, _dedup, _repo.Object, _publisher.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<IngestionPipeline>.Instance);

    private static Observation ValidObservation() => new()
    {
        SourceType = "AIS",
        ExternalId = "235009888",
        Position = new Point(-1.5, 51.0) { SRID = 4326 },
        ObservedAt = DateTimeOffset.UtcNow.AddSeconds(-5),
    };

    [Fact]
    public async Task ValidObservation_PersistsAndPublishes()
    {
        var pipeline = BuildPipeline();
        var obs = ValidObservation();

        await pipeline.ProcessAsync(obs);

        _repo.Verify(r => r.AddAsync(obs, It.IsAny<CancellationToken>()), Times.Once);
        _publisher.Verify(p => p.PublishAsync(obs, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidObservation_SkipsWithoutPersisting()
    {
        var pipeline = BuildPipeline();
        var obs = ValidObservation();
        obs.ExternalId = "";  // invalid

        await pipeline.ProcessAsync(obs);

        _repo.Verify(r => r.AddAsync(It.IsAny<Observation>(), It.IsAny<CancellationToken>()), Times.Never);
        _publisher.Verify(p => p.PublishAsync(It.IsAny<Observation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DuplicateObservation_SkipsWithoutPersisting()
    {
        var pipeline = BuildPipeline();
        var obs = ValidObservation();

        await pipeline.ProcessAsync(obs);  // first — passes through
        await pipeline.ProcessAsync(obs);  // second — same position/time bucket → duplicate

        _repo.Verify(r => r.AddAsync(It.IsAny<Observation>(), It.IsAny<CancellationToken>()), Times.Once);
        _publisher.Verify(p => p.PublishAsync(It.IsAny<Observation>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishExceptionDoesNotPreventCommit()
    {
        _publisher.Setup(p => p.PublishAsync(It.IsAny<Observation>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Redis unavailable"));

        var pipeline = BuildPipeline();
        var obs = ValidObservation();

        // Should not throw — publish failure is logged, not propagated
        await pipeline.Invoking(p => p.ProcessAsync(obs)).Should().NotThrowAsync();

        // Observation was still persisted
        _repo.Verify(r => r.AddAsync(obs, It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

```bash
dotnet test tests/SentinelMap.Infrastructure.Tests/ --filter "Pipeline.IngestionPipelineTests" -v minimal 2>&1 | tail -5
```
Expected: build error.

- [ ] **Step 3: Implement IngestionPipeline**

`src/SentinelMap.Infrastructure/Pipeline/IngestionPipeline.cs`:
```csharp
using Microsoft.Extensions.Logging;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;

namespace SentinelMap.Infrastructure.Pipeline;

public class IngestionPipeline
{
    private readonly ObservationValidator _validator;
    private readonly IDeduplicationService _dedup;
    private readonly IObservationRepository _repo;
    private readonly IObservationPublisher _publisher;
    private readonly ILogger<IngestionPipeline> _logger;

    private static readonly TimeSpan DedupTtl = TimeSpan.FromMinutes(2);

    public IngestionPipeline(
        ObservationValidator validator,
        IDeduplicationService dedup,
        IObservationRepository repo,
        IObservationPublisher publisher,
        ILogger<IngestionPipeline> logger)
    {
        _validator = validator;
        _dedup = dedup;
        _repo = repo;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task ProcessAsync(Observation observation, CancellationToken ct = default)
    {
        // Stage 1: Validate
        var validation = await _validator.ValidateAsync(observation, ct);
        if (!validation.IsValid)
        {
            _logger.LogDebug("Observation failed validation: {Errors}",
                string.Join(", ", validation.Errors.Select(e => e.ErrorMessage)));
            return;
        }

        // Stage 2: Deduplicate
        var dedupKey = RedisDeduplicationService.BuildKey(
            observation.SourceType,
            observation.ExternalId,
            observation.Position!.Y,
            observation.Position.X,
            observation.ObservedAt);

        if (await _dedup.IsDuplicateAsync(dedupKey, DedupTtl, ct))
        {
            _logger.LogDebug("Observation deduplicated: {Source}:{ExternalId}", observation.SourceType, observation.ExternalId);
            return;
        }

        // Stage 3: Persist
        await _repo.AddAsync(observation, ct);

        // Stage 4: Publish (non-blocking — publish failure must not roll back the persist)
        try
        {
            await _publisher.PublishAsync(observation, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish observation {Id} to Redis — observation is persisted, correlation will recover on restart", observation.Id);
        }
    }
}
```

- [ ] **Step 4: Run tests — verify they pass**

```bash
dotnet test tests/SentinelMap.Infrastructure.Tests/ --filter "Pipeline.IngestionPipelineTests" -v minimal
```
Expected: 4 passed.

- [ ] **Step 5: Commit**

```bash
git add src/SentinelMap.Infrastructure/Pipeline/IngestionPipeline.cs tests/SentinelMap.Infrastructure.Tests/Pipeline/IngestionPipelineTests.cs
git commit -m "feat(m2): add IngestionPipeline (validate→dedup→persist→publish)"
```

---

## Task 6: SimulatedAisConnector

**Files:**
- Create: `src/SentinelMap.Infrastructure/Connectors/SimulatedAisConnector.cs`
- Create: `tests/SentinelMap.Infrastructure.Tests/Connectors/SimulatedAisConnectorTests.cs`

Yields 4 vessels moving through the English Channel on 2-second intervals. Each vessel interpolates linearly between waypoints and cycles. The `RawData` JSON field carries `displayName` and `vesselType` for use by CorrelationWorker when creating entities.

- [ ] **Step 1: Write the failing tests**

`tests/SentinelMap.Infrastructure.Tests/Connectors/SimulatedAisConnectorTests.cs`:
```csharp
using FluentAssertions;
using SentinelMap.Infrastructure.Connectors;

namespace SentinelMap.Infrastructure.Tests.Connectors;

public class SimulatedAisConnectorTests
{
    [Fact]
    public void SourceId_IsSimulatedAis()
    {
        var connector = new SimulatedAisConnector(updateIntervalMs: 50);
        connector.SourceId.Should().Be("simulated-ais");
        connector.SourceType.Should().Be("AIS");
    }

    [Fact]
    public async Task StreamAsync_YieldsObservations()
    {
        var connector = new SimulatedAisConnector(updateIntervalMs: 10);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var observations = new List<SentinelMap.Domain.Entities.Observation>();
        await foreach (var obs in connector.StreamAsync(cts.Token))
        {
            observations.Add(obs);
            if (observations.Count >= 5) break;
        }

        observations.Should().HaveCountGreaterOrEqualTo(4, "at least one observation per vessel");
    }

    [Fact]
    public async Task StreamAsync_ObservationsHaveValidPositions()
    {
        var connector = new SimulatedAisConnector(updateIntervalMs: 10);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var observations = new List<SentinelMap.Domain.Entities.Observation>();
        await foreach (var obs in connector.StreamAsync(cts.Token))
        {
            observations.Add(obs);
            if (observations.Count >= 4) break;
        }

        foreach (var obs in observations)
        {
            obs.SourceType.Should().Be("AIS");
            obs.ExternalId.Should().NotBeNullOrEmpty();
            obs.Position.Should().NotBeNull();
            obs.Position!.Y.Should().BeInRange(-90, 90, "latitude");
            obs.Position!.X.Should().BeInRange(-180, 180, "longitude");
            obs.ObservedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
            obs.RawData.Should().NotBeNullOrEmpty("connector must embed displayName in RawData");
        }
    }

    [Fact]
    public async Task StreamAsync_CancellationStopsYielding()
    {
        var connector = new SimulatedAisConnector(updateIntervalMs: 50);
        using var cts = new CancellationTokenSource();

        var count = 0;
        await foreach (var _ in connector.StreamAsync(cts.Token))
        {
            count++;
            if (count == 2) cts.Cancel();
        }

        count.Should().BeLessOrEqualTo(4, "cancellation should stop iteration promptly");
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

```bash
dotnet test tests/SentinelMap.Infrastructure.Tests/ --filter "Connectors.SimulatedAisConnectorTests" -v minimal 2>&1 | tail -5
```
Expected: build error.

- [ ] **Step 3: Implement SimulatedAisConnector**

`src/SentinelMap.Infrastructure/Connectors/SimulatedAisConnector.cs`:
```csharp
using System.Runtime.CompilerServices;
using System.Text.Json;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;

namespace SentinelMap.Infrastructure.Connectors;

/// <summary>
/// Yields synthetic AIS observations for 4 vessels moving through the English Channel.
/// Requires no API keys — enables demo and offline development.
/// Position cycles through waypoints. Update interval is configurable (default 2s).
/// </summary>
public class SimulatedAisConnector : ISourceConnector
{
    private readonly int _updateIntervalMs;

    public SimulatedAisConnector(int updateIntervalMs = 2000)
    {
        _updateIntervalMs = updateIntervalMs;
    }

    public string SourceId => "simulated-ais";
    public string SourceType => "AIS";

    private record SimVessel(
        string Mmsi,
        string Name,
        string VesselType,
        double SpeedKnots,
        (double Lon, double Lat)[] Waypoints);

    private static readonly SimVessel[] Vessels =
    [
        new("235009888", "BRITANNIA STAR", "Cargo", 12,
            [(-3.5, 49.5), (-2.0, 50.0), (-0.5, 50.5), (1.0, 51.0), (2.5, 51.3)]),
        new("244670316", "ARCTIC DAWN", "Tanker", 8,
            [(-5.5, 50.2), (-4.0, 50.5), (-3.0, 51.0), (-3.5, 53.0), (-3.8, 53.4)]),
        new("227012345", "MARIE CELINE", "Passenger", 18,
            [(-1.8, 49.4), (-1.0, 49.8), (0.0, 50.2), (1.0, 50.7), (1.8, 51.0)]),
        new("338234567", "ATLAS VENTURE", "Cargo", 10,
            [(2.2, 51.5), (1.5, 51.3), (0.5, 51.0), (-0.5, 50.7), (-1.5, 50.4)]),
    ];

    public async IAsyncEnumerable<Observation> StreamAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Track waypoint position for each vessel — double to allow sub-index interpolation
        var positions = new double[Vessels.Length];
        var stepSize = SpeedToStepSize(2.0);  // step per interval (recalculated per vessel below)

        while (!ct.IsCancellationRequested)
        {
            for (int i = 0; i < Vessels.Length; i++)
            {
                if (ct.IsCancellationRequested) yield break;

                var vessel = Vessels[i];
                var waypoints = vessel.Waypoints;
                var step = SpeedToStepSize(vessel.SpeedKnots);

                positions[i] = (positions[i] + step) % waypoints.Length;

                var (lon, lat, heading) = Interpolate(waypoints, positions[i]);

                yield return new Observation
                {
                    SourceType = "AIS",
                    ExternalId = vessel.Mmsi,
                    Position = new Point(lon, lat) { SRID = 4326 },
                    Heading = heading,
                    SpeedMps = vessel.SpeedKnots * 0.514444,
                    ObservedAt = DateTimeOffset.UtcNow,
                    RawData = JsonSerializer.Serialize(new { displayName = vessel.Name, vesselType = vessel.VesselType }),
                };
            }

            try
            {
                await Task.Delay(_updateIntervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }

    /// <summary>Lerp between two waypoints. Returns (lon, lat, heading).</summary>
    private static (double lon, double lat, double heading) Interpolate(
        (double Lon, double Lat)[] waypoints, double position)
    {
        var n = waypoints.Length;
        var idxA = (int)position % n;
        var idxB = (idxA + 1) % n;
        var t = position - Math.Floor(position);

        var a = waypoints[idxA];
        var b = waypoints[idxB];

        var lon = a.Lon + (b.Lon - a.Lon) * t;
        var lat = a.Lat + (b.Lat - a.Lat) * t;

        // Heading: bearing from A to B in degrees (0 = N, 90 = E)
        var dLon = (b.Lon - a.Lon) * Math.PI / 180.0;
        var lat1 = a.Lat * Math.PI / 180.0;
        var lat2 = b.Lat * Math.PI / 180.0;
        var y = Math.Sin(dLon) * Math.Cos(lat2);
        var x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
        var heading = (Math.Atan2(y, x) * 180.0 / Math.PI + 360.0) % 360.0;

        return (lon, lat, heading);
    }

    /// <summary>Converts speed in knots to fractional waypoint step per 2-second interval.</summary>
    private static double SpeedToStepSize(double knots)
    {
        // ~0.001 waypoint steps per interval per knot — vessels take ~60s to traverse a segment
        return knots * 0.001;
    }
}
```

- [ ] **Step 4: Run tests — verify they pass**

```bash
dotnet test tests/SentinelMap.Infrastructure.Tests/ --filter "Connectors.SimulatedAisConnectorTests" -v minimal
```
Expected: 4 passed.

- [ ] **Step 5: Commit**

```bash
git add src/SentinelMap.Infrastructure/Connectors/SimulatedAisConnector.cs tests/SentinelMap.Infrastructure.Tests/Connectors/SimulatedAisConnectorTests.cs
git commit -m "feat(m2): add SimulatedAisConnector with 4 vessels in the English Channel"
```

---

## Task 7: AisStreamConnector (Live Mode)

**Files:**
- Create: `src/SentinelMap.Infrastructure/Connectors/AisStreamConnector.cs`
- Create: `tests/SentinelMap.Infrastructure.Tests/Connectors/AisStreamConnectorTests.cs`

Tests cover JSON parsing only (no network required). WebSocket connectivity is integration-tested manually.

- [ ] **Step 1: Write the failing tests**

`tests/SentinelMap.Infrastructure.Tests/Connectors/AisStreamConnectorTests.cs`:
```csharp
using FluentAssertions;
using SentinelMap.Infrastructure.Connectors;

namespace SentinelMap.Infrastructure.Tests.Connectors;

public class AisStreamConnectorTests
{
    [Fact]
    public void ParsePositionReport_ExtractsCorrectFields()
    {
        var json = """
        {
          "MessageType": "PositionReport",
          "MetaData": {
            "MMSI": 235009888,
            "ShipName": "BRITANNIA STAR",
            "latitude": 51.1234,
            "longitude": -1.5678,
            "time_utc": "2026-03-19 14:30:00.000000"
          },
          "Message": {
            "PositionReport": {
              "Cog": 45.2,
              "Sog": 12.4,
              "TrueHeading": 44,
              "NavigationalStatus": 0
            }
          }
        }
        """;

        var obs = AisStreamConnector.ParseMessage(json);

        obs.Should().NotBeNull();
        obs!.SourceType.Should().Be("AIS");
        obs.ExternalId.Should().Be("235009888");
        obs.Position.Should().NotBeNull();
        obs.Position!.Y.Should().BeApproximately(51.1234, 0.0001);
        obs.Position!.X.Should().BeApproximately(-1.5678, 0.0001);
        obs.Heading.Should().BeApproximately(44.0, 0.1);
        obs.SpeedMps.Should().BeApproximately(12.4 * 0.514444, 0.01);
    }

    [Fact]
    public void ParsePositionReport_HeadingUnavailable_FallsBackToCog()
    {
        var json = """
        {
          "MessageType": "PositionReport",
          "MetaData": { "MMSI": 123, "latitude": 51.0, "longitude": 1.0, "time_utc": "2026-03-19 12:00:00.000000" },
          "Message": { "PositionReport": { "Cog": 90.0, "Sog": 5.0, "TrueHeading": 511 } }
        }
        """;

        var obs = AisStreamConnector.ParseMessage(json);

        obs.Should().NotBeNull();
        obs!.Heading.Should().BeApproximately(90.0, 0.1, "511 means heading unavailable — fall back to COG");
    }

    [Fact]
    public void ParseShipStaticData_ExtractsDisplayName()
    {
        var json = """
        {
          "MessageType": "ShipStaticData",
          "MetaData": { "MMSI": 235009888, "latitude": 51.0, "longitude": -1.0, "time_utc": "2026-03-19 12:00:00.000000" },
          "Message": {
            "ShipStaticData": {
              "Name": "BRITANNIA STAR",
              "CallSign": "MBST",
              "Type": 70,
              "ImoNumber": 1234567
            }
          }
        }
        """;

        var obs = AisStreamConnector.ParseMessage(json);

        obs.Should().NotBeNull();
        obs!.RawData.Should().Contain("BRITANNIA STAR");
        obs.RawData.Should().Contain("MBST");
    }

    [Fact]
    public void ParseUnknownMessageType_ReturnsNull()
    {
        var json = """{"MessageType": "Unknown", "MetaData": {}, "Message": {}}""";
        var obs = AisStreamConnector.ParseMessage(json);
        obs.Should().BeNull();
    }

    [Fact]
    public void ParseMalformedJson_ReturnsNull()
    {
        var obs = AisStreamConnector.ParseMessage("not json at all");
        obs.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

```bash
dotnet test tests/SentinelMap.Infrastructure.Tests/ --filter "Connectors.AisStreamConnectorTests" -v minimal 2>&1 | tail -5
```
Expected: build error.

- [ ] **Step 3: Implement AisStreamConnector**

`src/SentinelMap.Infrastructure/Connectors/AisStreamConnector.cs`:
```csharp
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;

namespace SentinelMap.Infrastructure.Connectors;

/// <summary>
/// Live AIS connector via AISStream.io WebSocket.
/// Requires AISSTREAM_API_KEY environment variable.
/// Parses message types: PositionReport (1-3), ShipStaticData (5), StandardClassBPositionReport (18-19).
/// </summary>
public class AisStreamConnector : ISourceConnector
{
    private const string WssUrl = "wss://stream.aisstream.io/v0/stream";
    private const int HeadingUnavailable = 511;
    private const double KnotsToMps = 0.514444;

    private readonly string _apiKey;
    private readonly ILogger<AisStreamConnector> _logger;

    public AisStreamConnector(string apiKey, ILogger<AisStreamConnector> logger)
    {
        _apiKey = apiKey;
        _logger = logger;
    }

    public string SourceId => "aisstream";
    public string SourceType => "AIS";

    public async IAsyncEnumerable<Observation> StreamAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(WssUrl), ct);

        var subscription = JsonSerializer.Serialize(new
        {
            APIKey = _apiKey,
            BoundingBoxes = new[] { new[] { new[] { -180.0, -90.0 }, new[] { 180.0, 90.0 } } },
            FilterMessageTypes = new[] { "PositionReport", "ShipStaticData", "StandardClassBPositionReport" }
        });

        var subscriptionBytes = Encoding.UTF8.GetBytes(subscription);
        await ws.SendAsync(subscriptionBytes, WebSocketMessageType.Text, true, ct);

        _logger.LogInformation("AISStream WebSocket connected and subscribed");

        var buffer = new byte[64 * 1024];

        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            var messageBuilder = new StringBuilder();

            do
            {
                result = await ws.ReceiveAsync(buffer, ct);
                messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            } while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close) break;

            var message = messageBuilder.ToString();
            var observation = ParseMessage(message);
            if (observation is not null)
                yield return observation;
        }
    }

    /// <summary>
    /// Parses a raw AISStream JSON message into an Observation.
    /// Internal visibility allows direct unit testing of parsing logic without a WebSocket.
    /// Returns null for unsupported message types or parse failures.
    /// </summary>
    public static Observation? ParseMessage(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is null) return null;

            var messageType = node["MessageType"]?.GetValue<string>();
            var meta = node["MetaData"];

            if (meta is null || messageType is null) return null;

            var mmsi = meta["MMSI"]?.ToString() ?? meta["MMSI_String"]?.GetValue<string>();
            if (string.IsNullOrEmpty(mmsi)) return null;

            // Parse time — AISStream format: "2026-03-19 14:30:00.000000"
            var timeStr = meta["time_utc"]?.GetValue<string>() ?? "";
            if (!DateTimeOffset.TryParse(timeStr, out var observedAt))
                observedAt = DateTimeOffset.UtcNow;

            return messageType switch
            {
                "PositionReport" => ParsePositionReport(node, mmsi, observedAt),
                "StandardClassBPositionReport" => ParsePositionReport(node, mmsi, observedAt),
                "ShipStaticData" => ParseShipStaticData(node, mmsi, observedAt),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    private static Observation? ParsePositionReport(JsonNode node, string mmsi, DateTimeOffset observedAt)
    {
        var meta = node["MetaData"];
        var report = node["Message"]?["PositionReport"]
                  ?? node["Message"]?["StandardClassBPositionReport"];

        if (report is null) return null;

        var lat = meta?["latitude"]?.GetValue<double>() ?? 0;
        var lon = meta?["longitude"]?.GetValue<double>() ?? 0;
        var cog = report["Cog"]?.GetValue<double>() ?? 0;
        var sog = report["Sog"]?.GetValue<double>() ?? 0;
        var trueHeading = report["TrueHeading"]?.GetValue<int>() ?? HeadingUnavailable;

        var heading = trueHeading == HeadingUnavailable ? cog : (double)trueHeading;

        return new Observation
        {
            SourceType = "AIS",
            ExternalId = mmsi,
            Position = new Point(lon, lat) { SRID = 4326 },
            Heading = heading,
            SpeedMps = sog * KnotsToMps,
            ObservedAt = observedAt,
            RawData = JsonSerializer.Serialize(new
            {
                displayName = meta?["ShipName"]?.GetValue<string>(),
                vesselType = "Unknown",
            }),
        };
    }

    private static Observation? ParseShipStaticData(JsonNode node, string mmsi, DateTimeOffset observedAt)
    {
        var meta = node["MetaData"];
        var staticData = node["Message"]?["ShipStaticData"];
        if (staticData is null) return null;

        var lat = meta?["latitude"]?.GetValue<double>() ?? 0;
        var lon = meta?["longitude"]?.GetValue<double>() ?? 0;

        var name = staticData["Name"]?.GetValue<string>() ?? mmsi;
        var callsign = staticData["CallSign"]?.GetValue<string>();
        var imo = staticData["ImoNumber"]?.GetValue<int>();
        var shipType = staticData["Type"]?.GetValue<int>();

        return new Observation
        {
            SourceType = "AIS",
            ExternalId = mmsi,
            Position = new Point(lon, lat) { SRID = 4326 },
            ObservedAt = observedAt,
            RawData = JsonSerializer.Serialize(new
            {
                displayName = name,
                callsign,
                imoNumber = imo,
                aisShipType = shipType,
                vesselType = MapAisShipType(shipType),
            }),
        };
    }

    private static string MapAisShipType(int? aisType) => aisType switch
    {
        >= 70 and <= 79 => "Cargo",
        >= 80 and <= 89 => "Tanker",
        >= 60 and <= 69 => "Passenger",
        30 => "Fishing",
        _ => "Unknown",
    };
}
```

- [ ] **Step 4: Run tests — verify they pass**

```bash
dotnet test tests/SentinelMap.Infrastructure.Tests/ --filter "Connectors.AisStreamConnectorTests" -v minimal
```
Expected: 5 passed.

- [ ] **Step 5: Commit**

```bash
git add src/SentinelMap.Infrastructure/Connectors/AisStreamConnector.cs tests/SentinelMap.Infrastructure.Tests/Connectors/AisStreamConnectorTests.cs
git commit -m "feat(m2): add AisStreamConnector with WebSocket streaming and AIS message parsing"
```

---

## Task 8: EntityRepository + CorrelationWorker

**Files:**
- Create: `src/SentinelMap.Infrastructure/Repositories/EntityRepository.cs`
- Create: `src/SentinelMap.Workers/Services/CorrelationWorker.cs`
- Create: `tests/SentinelMap.Workers.Tests/Services/CorrelationWorkerTests.cs`

The CorrelationWorker is M2's skeleton: it handles the hot-path cache and entity creation. Full fuzzy matching is M3. Key design: every observation results in an entity update — either an existing entity's position is updated (hot-path cache hit), or a new entity is created (miss). The correlation hot-path cache key is `correlation:link:{sourceType}:{externalId}`.

- [ ] **Step 1: Implement EntityRepository**

`src/SentinelMap.Infrastructure/Repositories/EntityRepository.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;
using SentinelMap.Infrastructure.Data;
using SentinelMap.SharedKernel.Enums;

namespace SentinelMap.Infrastructure.Repositories;

public class EntityRepository : IEntityRepository
{
    private readonly SystemDbContext _db;

    public EntityRepository(SystemDbContext db)
    {
        _db = db;
    }

    public async Task<TrackedEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Entities.FindAsync([id], ct);
    }

    public async Task<TrackedEntity> AddAsync(TrackedEntity entity, CancellationToken ct = default)
    {
        _db.Entities.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task UpdateAsync(TrackedEntity entity, CancellationToken ct = default)
    {
        _db.Entities.Update(entity);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Bulk-updates a single entity's position fields.
    /// Uses raw SQL to avoid loading the entity — this is the hot path.
    /// </summary>
    public async Task UpdatePositionAsync(
        Guid entityId,
        Point position,
        double? speedMps,
        double? heading,
        DateTimeOffset lastSeen,
        CancellationToken ct = default)
    {
        await _db.Database.ExecuteSqlRawAsync(
            @"UPDATE entities
              SET last_known_position = ST_SetSRID(ST_MakePoint({0}, {1}), 4326),
                  last_speed_mps = {2},
                  last_heading = {3},
                  last_seen = {4},
                  updated_at = now()
              WHERE id = {5}",
            position.X, position.Y, speedMps, heading, lastSeen, entityId,
            cancellationToken: ct);
    }
}
```

Also add `UpdatePositionAsync` to `IEntityRepository` in Domain:

Edit `src/SentinelMap.Domain/Interfaces/IEntityRepository.cs`:
```csharp
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;

namespace SentinelMap.Domain.Interfaces;

public interface IEntityRepository
{
    Task<TrackedEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<TrackedEntity> AddAsync(TrackedEntity entity, CancellationToken ct = default);
    Task UpdateAsync(TrackedEntity entity, CancellationToken ct = default);
    Task UpdatePositionAsync(Guid entityId, Point position, double? speedMps, double? heading, DateTimeOffset lastSeen, CancellationToken ct = default);
}
```

- [ ] **Step 2: Write the failing CorrelationWorker tests**

`tests/SentinelMap.Workers.Tests/Services/CorrelationWorkerTests.cs`:
```csharp
using FluentAssertions;
using Moq;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;
using SentinelMap.Domain.Messages;
using SentinelMap.SharedKernel.Enums;
using SentinelMap.Workers.Services;
using StackExchange.Redis;

namespace SentinelMap.Workers.Tests.Services;

public class CorrelationWorkerTests
{
    // CorrelationWorker.ProcessObservation is internal-ish logic extracted for testability.
    // We test CorrelationProcessor which is the extracted pure logic class.

    private readonly Mock<IEntityRepository> _entityRepo = new();
    private readonly Mock<IDatabase> _redisDb = new();

    [Fact]
    public async Task CacheHit_UpdatesEntityPositionWithoutCreatingNew()
    {
        var entityId = Guid.NewGuid();
        _redisDb.Setup(db => db.StringGetAsync("correlation:link:AIS:235009888", It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)entityId.ToString());

        var processor = new CorrelationProcessor(_entityRepo.Object, _redisDb.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CorrelationProcessor>.Instance);

        var msg = new ObservationPublishedMessage(1, DateTimeOffset.UtcNow, "AIS", "235009888", -1.5, 51.0, 90.0, 6.17);

        await processor.ProcessAsync(msg);

        _entityRepo.Verify(r => r.UpdatePositionAsync(entityId, It.IsAny<Point>(), It.IsAny<double?>(), It.IsAny<double?>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        _entityRepo.Verify(r => r.AddAsync(It.IsAny<TrackedEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CacheMiss_CreatesNewEntityAndCachesLink()
    {
        _redisDb.Setup(db => db.StringGetAsync("correlation:link:AIS:999999999", It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        _entityRepo.Setup(r => r.AddAsync(It.IsAny<TrackedEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrackedEntity e, CancellationToken _) => e);

        var processor = new CorrelationProcessor(_entityRepo.Object, _redisDb.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CorrelationProcessor>.Instance);

        var msg = new ObservationPublishedMessage(2, DateTimeOffset.UtcNow, "AIS", "999999999", 1.0, 51.0, null, null);

        await processor.ProcessAsync(msg);

        _entityRepo.Verify(r => r.AddAsync(It.IsAny<TrackedEntity>(), It.IsAny<CancellationToken>()), Times.Once);
        // Match the 4-arg overload: StringSetAsync(key, value, expiry, when, flags)
        // The implementation calls: await _db.StringSetAsync(cacheKey, entityId, CacheTtl)
        // which resolves to StringSetAsync(RedisKey, RedisValue, TimeSpan?, When, CommandFlags)
        _redisDb.Verify(db => db.StringSetAsync(
            "correlation:link:AIS:999999999",
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }
}
```

- [ ] **Step 3: Run tests — verify they fail**

```bash
dotnet test tests/SentinelMap.Workers.Tests/ --filter "Services.CorrelationWorkerTests" -v minimal 2>&1 | tail -5
```
Expected: build error.

- [ ] **Step 4: Implement CorrelationProcessor + CorrelationWorker**

`src/SentinelMap.Workers/Services/CorrelationWorker.cs`:
```csharp
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;
using SentinelMap.Domain.Messages;
using SentinelMap.SharedKernel.Enums;
using StackExchange.Redis;

namespace SentinelMap.Workers.Services;

/// <summary>
/// Extracted processing logic — testable without a BackgroundService host.
/// </summary>
public class CorrelationProcessor
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(7);
    private readonly IEntityRepository _entityRepo;
    private readonly IDatabase _db;
    private readonly ILogger<CorrelationProcessor> _logger;

    public CorrelationProcessor(IEntityRepository entityRepo, IDatabase db, ILogger<CorrelationProcessor> logger)
    {
        _entityRepo = entityRepo;
        _db = db;
        _logger = logger;
    }

    public async Task<EntityUpdatedMessage?> ProcessAsync(ObservationPublishedMessage msg, CancellationToken ct = default)
    {
        var cacheKey = $"correlation:link:{msg.SourceType}:{msg.ExternalId}";
        var cached = await _db.StringGetAsync(cacheKey);
        var position = new Point(msg.Longitude, msg.Latitude) { SRID = 4326 };

        if (!cached.IsNull && Guid.TryParse(cached.ToString(), out var entityId))
        {
            // Hot path: known entity — update position only
            await _entityRepo.UpdatePositionAsync(entityId, position, msg.SpeedMps, msg.Heading, msg.ObservedAt, ct);

            _logger.LogDebug("Hot-path hit for {Source}:{ExternalId} → entity {EntityId}", msg.SourceType, msg.ExternalId, entityId);

            return new EntityUpdatedMessage(entityId, msg.Longitude, msg.Latitude, msg.Heading, msg.SpeedMps,
                EntityType.Vessel.ToString(), EntityStatus.Active.ToString(), msg.ObservedAt);
        }

        // Cold path: new identifier — create entity
        var entity = new TrackedEntity
        {
            Type = EntityType.Vessel,   // AIS is always a vessel
            LastKnownPosition = position,
            LastSpeedMps = msg.SpeedMps,
            LastHeading = msg.Heading,
            LastSeen = msg.ObservedAt,
            Status = EntityStatus.Active,
        };

        await _entityRepo.AddAsync(entity, ct);

        // Cache the link for future observations
        await _db.StringSetAsync(cacheKey, entity.Id.ToString(), CacheTtl);

        _logger.LogInformation("Created entity {EntityId} for {Source}:{ExternalId}", entity.Id, msg.SourceType, msg.ExternalId);

        return new EntityUpdatedMessage(entity.Id, msg.Longitude, msg.Latitude, msg.Heading, msg.SpeedMps,
            EntityType.Vessel.ToString(), EntityStatus.Active.ToString(), msg.ObservedAt);
    }
}

/// <summary>
/// BackgroundService that subscribes to Redis "observations:*" and runs CorrelationProcessor per message.
/// Publishes EntityUpdatedMessage to "entities:updated" for consumption by TrackHubService.
/// </summary>
public class CorrelationWorker : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CorrelationWorker> _logger;

    public CorrelationWorker(IConnectionMultiplexer redis, IServiceScopeFactory scopeFactory, ILogger<CorrelationWorker> logger)
    {
        _redis = redis;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = _redis.GetSubscriber();
        var channel = RedisChannel.Pattern("observations:*");

        await subscriber.SubscribeAsync(channel, async (_, message) =>
        {
            if (message.IsNull) return;

            ObservationPublishedMessage? msg;
            try { msg = JsonSerializer.Deserialize<ObservationPublishedMessage>(message!); }
            catch { return; }
            if (msg is null) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var entityRepo = scope.ServiceProvider.GetRequiredService<IEntityRepository>();
                var db = _redis.GetDatabase();
                var processor = new CorrelationProcessor(entityRepo, db,
                    scope.ServiceProvider.GetRequiredService<ILogger<CorrelationProcessor>>());

                var entityUpdate = await processor.ProcessAsync(msg, stoppingToken);
                if (entityUpdate is null) return;

                // Publish entity update for TrackHubService
                var json = JsonSerializer.Serialize(entityUpdate);
                await _redis.GetSubscriber().PublishAsync(RedisChannel.Literal("entities:updated"), json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing observation for {Source}:{ExternalId}", msg.SourceType, msg.ExternalId);
            }
        });

        _logger.LogInformation("CorrelationWorker subscribed to observations:*");
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
```

- [ ] **Step 5: Run tests — verify they pass**

```bash
dotnet test tests/SentinelMap.Workers.Tests/ --filter "Services.CorrelationWorkerTests" -v minimal
```
Expected: 2 passed.

- [ ] **Step 6: Commit**

```bash
git add src/SentinelMap.Infrastructure/Repositories/EntityRepository.cs src/SentinelMap.Domain/Interfaces/IEntityRepository.cs src/SentinelMap.Workers/Services/CorrelationWorker.cs tests/SentinelMap.Workers.Tests/Services/CorrelationWorkerTests.cs
git commit -m "feat(m2): add EntityRepository, CorrelationProcessor, and CorrelationWorker skeleton"
```

---

## Task 9: IngestionWorker BackgroundService

**Files:**
- Create: `src/SentinelMap.Workers/Services/IngestionWorker.cs`

Hosts one `ISourceConnector`. On failure, exponential backoff up to 30s. After 3 consecutive failures, circuit opens for 30s before retrying (half-open probe on each subsequent interval).

- [ ] **Step 1: Implement**

`src/SentinelMap.Workers/Services/IngestionWorker.cs`:
```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SentinelMap.Domain.Interfaces;
using SentinelMap.Infrastructure.Pipeline;

namespace SentinelMap.Workers.Services;

public class IngestionWorker : BackgroundService
{
    private readonly ISourceConnector _connector;
    private readonly IServiceScopeFactory _scopeFactory;  // IngestionPipeline is Transient — must be created per scope
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IngestionWorker starting for source {SourceId}", _connector.SourceId);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Circuit breaker — open state: wait then probe
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
        // One scope per run (not per observation) works for M2 because the pipeline is stateless
        // across observations and SaveChangesAsync is called inline per observation (no cross-obs state).
        // Known M2 limitation: for long-lived live connectors, EF change tracking will accumulate
        // over millions of observations. M3 should move to per-observation scopes or clear tracking.
        using var scope = _scopeFactory.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<IngestionPipeline>();

        await foreach (var observation in _connector.StreamAsync(ct))
        {
            await pipeline.ProcessAsync(observation, ct);
        }
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build src/SentinelMap.Workers/ 2>&1 | tail -5
```
Expected: 0 Error(s).

- [ ] **Step 3: Commit**

```bash
git add src/SentinelMap.Workers/Services/IngestionWorker.cs
git commit -m "feat(m2): add IngestionWorker with exponential backoff and circuit breaker"
```

---

## Task 10: Workers Program.cs DI Wiring

**Files:**
- Modify: `src/SentinelMap.Workers/Program.cs`

Registers Redis, the connector (selected by `SENTINELMAP_DATA_MODE` env var), IngestionPipeline, IngestionWorker, and CorrelationWorker. Also adds a health check endpoint per the spec.

- [ ] **Step 1: Implement**

Replace the contents of `src/SentinelMap.Workers/Program.cs`:

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
    ServiceLifetime.Transient);  // Transient: each scope/CorrelationWorker call gets its own context

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
// IngestionPipeline must be Transient (not Singleton) because it captures IObservationRepository
// which is Transient (holds a DbContext). A Singleton capturing a Transient is a captive dependency bug.
// IngestionWorker creates a new pipeline instance per connector run via IServiceScopeFactory.
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
            sp.GetRequiredService<ILogger<AisStreamConnector>>()),  // use DI logger, not a leaked factory

        _ => new SimulatedAisConnector()  // "Simulated" or unrecognised
    };
});

// --- Background Services ---
builder.Services.AddHostedService<IngestionWorker>();
builder.Services.AddHostedService<CorrelationWorker>();

// --- Health checks ---
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("DefaultConnection") ?? "")
    .AddRedis(redisConnection);

// ASP.NET health endpoint for Docker depends_on
builder.Services.AddHostedService<WorkerHealthHost>();

var host = builder.Build();
host.Run();

/// <summary>
/// Minimal HTTP health endpoint so Docker depends_on can probe the Workers host.
/// Runs alongside the Worker host without requiring a full ASP.NET Core setup.
/// </summary>
internal class WorkerHealthHost : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Workers host health is indirectly reported via the API host.
        // This stub satisfies future tooling without adding ASP.NET Core to Workers.
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
```

Also update `src/SentinelMap.Workers/SentinelMap.Workers.csproj` to add health check packages:

```xml
<ItemGroup>
  <PackageReference Include="AspNetCore.HealthChecks.NpgSql" Version="9.0.0" />
  <PackageReference Include="AspNetCore.HealthChecks.Redis" Version="9.0.0" />
  <PackageReference Include="Microsoft.Extensions.Hosting" Version="9.0.9" />
</ItemGroup>
```

- [ ] **Step 2: Update connection strings in worker appsettings**

Create `src/SentinelMap.Workers/appsettings.json` (if it doesn't exist).
**Credentials must match the `docker-compose.yml` db service** (`POSTGRES_USER: sentinel`, `POSTGRES_PASSWORD: sentinel_dev_password`):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=db;Port=5432;Database=sentinelmap;Username=sentinel;Password=sentinel_dev_password",
    "Redis": "redis:6379"
  },
  "SENTINELMAP_DATA_MODE": "Simulated",
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  }
}
```

- [ ] **Step 3: Verify build**

```bash
cd C:/Users/lukeb/source/repos/SentinelMap
dotnet build SentinelMap.slnx 2>&1 | tail -8
```
Expected: 0 Error(s).

- [ ] **Step 4: Commit**

```bash
git add src/SentinelMap.Workers/
git commit -m "feat(m2): wire Workers DI — Redis, connector selection, IngestionWorker + CorrelationWorker"
```

---

## Task 11: TrackHub + TrackHubService + API Updates + UserSeeder

**Files:**
- Create: `src/SentinelMap.Api/Hubs/TrackHub.cs`
- Create: `src/SentinelMap.Api/Hubs/TrackHubService.cs`
- Create: `src/SentinelMap.Api/Services/UserSeeder.cs`
- Modify: `src/SentinelMap.Api/Program.cs`

- [ ] **Step 1: Create TrackHub**

`src/SentinelMap.Api/Hubs/TrackHub.cs`:
```csharp
using Microsoft.AspNetCore.SignalR;

namespace SentinelMap.Api.Hubs;

/// <summary>
/// Real-time track data hub. Clients subscribe to receive entity position updates.
/// M2: broadcasts to all connected clients. M3+: bbox-scoped group filtering.
///
/// SignalR JWT auth: token passed via query string ?access_token=... for WebSocket connections.
/// Configured in Program.cs JwtBearerEvents.OnMessageReceived.
/// </summary>
public class TrackHub : Hub
{
    /// <summary>
    /// Client subscribes to receive all track updates within a viewport.
    /// M2: stored in connection metadata for future use; broadcasts are global.
    /// </summary>
    public Task SubscribeArea(double west, double south, double east, double north)
    {
        Context.Items["bbox"] = $"{west},{south},{east},{north}";
        return Task.CompletedTask;
    }

    public Task UnsubscribeArea()
    {
        Context.Items.Remove("bbox");
        return Task.CompletedTask;
    }

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }
}
```

- [ ] **Step 2: Create TrackHubService**

`src/SentinelMap.Api/Hubs/TrackHubService.cs`:
```csharp
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using SentinelMap.Domain.Messages;
using StackExchange.Redis;

namespace SentinelMap.Api.Hubs;

/// <summary>
/// Subscribes to Redis "entities:updated" channel.
/// Broadcasts TrackUpdate events to all connected SignalR clients.
/// Runs as a BackgroundService inside the API host.
/// </summary>
public class TrackHubService : BackgroundService
{
    private readonly IHubContext<TrackHub> _hub;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<TrackHubService> _logger;

    public TrackHubService(IHubContext<TrackHub> hub, IConnectionMultiplexer redis, ILogger<TrackHubService> logger)
    {
        _hub = hub;
        _redis = redis;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = _redis.GetSubscriber();

        await subscriber.SubscribeAsync(RedisChannel.Literal("entities:updated"), async (_, message) =>
        {
            if (message.IsNull) return;

            EntityUpdatedMessage? evt;
            try { evt = JsonSerializer.Deserialize<EntityUpdatedMessage>(message!); }
            catch { return; }
            if (evt is null) return;

            var trackUpdate = new
            {
                entityId = evt.EntityId,
                position = new[] { evt.Longitude, evt.Latitude },
                heading = evt.Heading,
                speed = evt.Speed,
                entityType = evt.EntityType,
                status = evt.Status,
                timestamp = evt.Timestamp,
            };

            try
            {
                await _hub.Clients.All.SendAsync("TrackUpdate", trackUpdate, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast TrackUpdate to SignalR clients");
            }
        });

        _logger.LogInformation("TrackHubService subscribed to entities:updated");
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
```

- [ ] **Step 3: Create UserSeeder**

`src/SentinelMap.Api/Services/UserSeeder.cs`:
```csharp
using Microsoft.AspNetCore.Identity;
using SentinelMap.Domain.Entities;
using SentinelMap.Infrastructure.Data;
using SentinelMap.Infrastructure.Identity;   // AppIdentityUser
using SentinelMap.SharedKernel.Enums;         // Roles, Classification

namespace SentinelMap.Api.Services;

public static class UserSeeder
{
    private static readonly (string Email, string Role, Classification Clearance)[] SeedUsers =
    [
        ("admin@sentinel.local",   Roles.Admin,   Classification.Secret),
        ("analyst@sentinel.local", Roles.Analyst, Classification.OfficialSensitive),
        ("viewer@sentinel.local",  Roles.Viewer,  Classification.Official),
    ];

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppIdentityUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var db = scope.ServiceProvider.GetRequiredService<SentinelMapDbContext>();

        var password = Environment.GetEnvironmentVariable("SENTINELMAP_SEED_PASSWORD") ?? "Demo123!";

        // Ensure roles exist
        foreach (var role in new[] { Roles.Admin, Roles.Analyst, Roles.Viewer })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));
        }

        foreach (var (email, role, clearance) in SeedUsers)
        {
            if (await userManager.FindByEmailAsync(email) is not null) continue;

            var identityUser = new AppIdentityUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
            };

            var result = await userManager.CreateAsync(identityUser, password);
            if (!result.Succeeded) continue;

            await userManager.AddToRoleAsync(identityUser, role);

            // Create domain User record (same ID as Identity user)
            db.DomainUsers.Add(new User
            {
                Id = identityUser.Id,
                Email = email,
                DisplayName = email.Split('@')[0],
                Role = role,
                ClearanceLevel = clearance,
            });
        }

        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 4: Update API Program.cs**

Read `src/SentinelMap.Api/Program.cs` fully first, then apply these additions.

After the existing `builder.Services.AddSingleton<AuditService>();` line, add:

```csharp
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
```

Add `using StackExchange.Redis;` and `using SentinelMap.Api.Hubs;` at the top.

In the JWT bearer configuration section, add `OnMessageReceived` event to extract token from query string for WebSocket/SignalR connections:

```csharp
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
```

After `app.MapGet("/health", ...)` add:

```csharp
app.MapHub<TrackHub>("/hubs/tracks");
```

After `await MigrateAsync(app);` add:

```csharp
await SentinelMap.Api.Services.UserSeeder.SeedAsync(app.Services);
```

Also add `appsettings.json` connection string for Redis:

```json
"Redis": "redis:6379"
```

- [ ] **Step 5: Verify build**

```bash
dotnet build SentinelMap.slnx 2>&1 | tail -8
```
Expected: 0 Error(s).

- [ ] **Step 6: Commit**

```bash
git add src/SentinelMap.Api/
git commit -m "feat(m2): add TrackHub, TrackHubService, UserSeeder, and SignalR Redis backplane"
```

---

## Task 12: PMTiles Basemap Setup

**Files:**
- Create: `scripts/download-pmtiles.sh`
- Modify: `client/src/components/map/MapContainer.tsx`

The PMTiles file is downloaded once and served as a static asset. The `pmtiles` npm package (already installed) provides a MapLibre protocol handler. Basemap style uses Protomaps' dark theme layers.

- [ ] **Step 1: Create download script**

`scripts/download-pmtiles.sh`:
```bash
#!/usr/bin/env bash
# Downloads a PMTiles file for the UK + English Channel region.
# Requires: pmtiles CLI — install with: npm install -g pmtiles
# Source: Protomaps daily planet build (public).
# Output size: ~120MB for the bounding box below.
#
# Usage: bash scripts/download-pmtiles.sh

set -euo pipefail

SOURCE="https://build.protomaps.com/20241001.pmtiles"
OUTPUT="client/public/tiles/basemap.pmtiles"
BBOX="-10,49,2,62"  # West, South, East, North — UK + English Channel + Irish Sea

mkdir -p "$(dirname "$OUTPUT")"

if [ -f "$OUTPUT" ]; then
  echo "PMTiles file already exists at $OUTPUT — delete it to re-download."
  exit 0
fi

echo "Downloading UK/English Channel PMTiles region..."
echo "Source: $SOURCE"
echo "Bbox:   $BBOX"
echo "Output: $OUTPUT"
echo ""
echo "This may take several minutes depending on connection speed."

pmtiles extract "$SOURCE" "$OUTPUT" --bbox="$BBOX"

echo ""
echo "Done. File size: $(du -sh "$OUTPUT" | cut -f1)"
echo "Add $OUTPUT to your .gitignore if it's not already there."
```

- [ ] **Step 2: Install protomaps-themes-base**

```bash
cd C:/Users/lukeb/source/repos/SentinelMap/client
npm install protomaps-themes-base
```

- [ ] **Step 3: Update MapContainer.tsx with PMTiles protocol and basemap style**

Replace `client/src/components/map/MapContainer.tsx`:

```tsx
import { useEffect, useRef } from 'react'
import maplibregl from 'maplibre-gl'
import 'maplibre-gl/dist/maplibre-gl.css'
import { Protocol } from 'pmtiles'
import { layersWithConfig } from 'protomaps-themes-base'

// Register PMTiles protocol once at module load
const protocol = new Protocol()
maplibregl.addProtocol('pmtiles', (request) => protocol.tile(request))

const PMTILES_URL = '/tiles/basemap.pmtiles'

function buildMapStyle(): maplibregl.StyleSpecification {
  return {
    version: 8,
    glyphs: 'https://protomaps.github.io/basemaps-assets/fonts/{fontstack}/{range}.pbf',
    sprite: 'https://protomaps.github.io/basemaps-assets/sprites/v4/dark',
    sources: {
      basemap: {
        type: 'vector',
        url: `pmtiles://${PMTILES_URL}`,
        attribution: '© <a href="https://openstreetmap.org">OpenStreetMap</a>',
      },
    },
    layers: [
      { id: 'background', type: 'background', paint: { 'background-color': '#0f172a' } },
      // Protomaps dark theme layers — coastlines, land, water, labels
      ...layersWithConfig('dark', 'basemap') as maplibregl.LayerSpecification[],
    ],
  }
}

interface MapContainerProps {
  onMapReady?: (map: maplibregl.Map) => void
}

export function MapContainer({ onMapReady }: MapContainerProps) {
  const mapContainerRef = useRef<HTMLDivElement>(null)
  const mapRef = useRef<maplibregl.Map | null>(null)

  useEffect(() => {
    if (!mapContainerRef.current || mapRef.current) return

    const map = new maplibregl.Map({
      container: mapContainerRef.current,
      style: buildMapStyle(),
      center: [1.0, 51.0],   // English Channel
      zoom: 7,
    })

    map.addControl(new maplibregl.NavigationControl(), 'bottom-right')
    mapRef.current = map

    map.on('load', () => {
      onMapReady?.(map)
    })

    return () => {
      mapRef.current?.remove()
      mapRef.current = null
    }
  }, [onMapReady])

  return <div ref={mapContainerRef} className="h-full w-full" />
}
```

- [ ] **Step 4: Add tiles to .gitignore**

```bash
cd C:/Users/lukeb/source/repos/SentinelMap
echo "client/public/tiles/*.pmtiles" >> .gitignore
```

- [ ] **Step 5: Verify TypeScript compiles**

```bash
cd client
npx tsc --noEmit 2>&1 | head -20
```
Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
cd ..
git add scripts/download-pmtiles.sh client/src/components/map/MapContainer.tsx client/package.json client/package-lock.json .gitignore
git commit -m "feat(m2): add PMTiles basemap with dark Protomaps theme and download script"
```

---

## Task 13: MaritimeTrackLayer

**Files:**
- Create: `client/src/components/map/icons/vessel.ts`
- Create: `client/src/components/map/MaritimeTrackLayer.tsx`

Renders vessel positions as heading-oriented SVG icons on the MapLibre map. Colour by vessel type. Uses SDF (Signed Distance Field) mode for icon recolouring without multiple sprite files.

- [ ] **Step 1: Create vessel icon**

`client/src/components/map/icons/vessel.ts`:
```typescript
/**
 * Vessel icon as an SVG data URL.
 * Simple ship silhouette: pointed bow (top), wider stern (bottom).
 * 24×32 pixels, white fill — coloured at render time via SDF icon-color.
 */
export const VESSEL_ICON_SVG = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 32" width="24" height="32">
  <polygon points="12,1 22,28 12,22 2,28" fill="white"/>
</svg>`

export const VESSEL_ICON_DATA_URL = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(VESSEL_ICON_SVG)}`
```

- [ ] **Step 2: Create MaritimeTrackLayer.tsx**

`client/src/components/map/MaritimeTrackLayer.tsx`:
```tsx
import { useEffect, useRef } from 'react'
import maplibregl from 'maplibre-gl'
import type { TrackFeature } from '../../types'
import { VESSEL_ICON_DATA_URL } from './icons/vessel'

const SOURCE_ID = 'maritime-tracks'
const LAYER_ID = 'maritime-track-symbols'
const ICON_ID = 'vessel-icon'

/** Vessel type → track colour. Red is reserved for alerts. */
const TYPE_COLOURS: Record<string, string> = {
  Cargo: '#94a3b8',      // slate-400
  Tanker: '#f59e0b',     // amber-500
  Passenger: '#2dd4bf',  // teal-400
  Fishing: '#a3e635',    // lime-400
  Unknown: '#64748b',    // slate-500
}

interface MaritimeTrackLayerProps {
  map: maplibregl.Map
  tracks: TrackFeature[]
}

export function MaritimeTrackLayer({ map, tracks }: MaritimeTrackLayerProps) {
  const iconLoaded = useRef(false)

  // Load vessel icon image once
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
    img.src = VESSEL_ICON_DATA_URL
  }, [map])

  // Add GeoJSON source + symbol layer once
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
        'icon-size': 0.8,
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
        'icon-color': [
          'match', ['get', 'vesselType'],
          'Cargo',     TYPE_COLOURS.Cargo,
          'Tanker',    TYPE_COLOURS.Tanker,
          'Passenger', TYPE_COLOURS.Passenger,
          'Fishing',   TYPE_COLOURS.Fishing,
          /* default */ TYPE_COLOURS.Unknown,
        ],
        'icon-opacity': [
          'case',
          ['==', ['get', 'status'], 'Dark'], 0.3,
          1.0,
        ],
        'text-color': '#94a3b8',
        'text-halo-color': '#0f172a',
        'text-halo-width': 1,
      },
    })

    return () => {
      if (map.getLayer(LAYER_ID)) map.removeLayer(LAYER_ID)
      if (map.getSource(SOURCE_ID)) map.removeSource(SOURCE_ID)
    }
  }, [map])

  // Update source data when tracks change
  useEffect(() => {
    const source = map.getSource(SOURCE_ID) as maplibregl.GeoJSONSource | undefined
    if (!source) return

    source.setData({
      type: 'FeatureCollection',
      features: tracks,
    })
  }, [map, tracks])

  return null
}
```

- [ ] **Step 3: Extend types**

Edit `client/src/types/index.ts` — add:

```typescript
import type { Feature, Point } from 'geojson'

export type ClassificationLevel = 'official' | 'officialSensitive' | 'secret'

export type EntityType = 'Vessel' | 'Aircraft' | 'Unknown'

export type EntityStatus = 'Active' | 'Stale' | 'Dark' | 'Lost'

export type VesselType = 'Cargo' | 'Tanker' | 'Passenger' | 'Fishing' | 'Unknown'

export interface TrackUpdate {
  entityId: string
  position: [number, number]
  heading: number | null
  speed: number | null
  entityType: EntityType
  status: EntityStatus
  timestamp: string
}

export interface TrackProperties {
  entityId: string
  heading: number | null
  speed: number | null
  entityType: EntityType
  status: EntityStatus
  vesselType: VesselType
  displayName: string
  lastUpdated: string
}

export type TrackFeature = Feature<Point, TrackProperties>
```

- [ ] **Step 4: Verify TypeScript compiles**

```bash
cd C:/Users/lukeb/source/repos/SentinelMap/client
npx tsc --noEmit 2>&1 | head -20
```
Expected: 0 errors (or only errors in files not yet updated).

- [ ] **Step 5: Commit**

```bash
cd ..
git add client/src/components/map/ client/src/types/index.ts
git commit -m "feat(m2): add MaritimeTrackLayer with heading-oriented SDF vessel icons"
```

---

## Task 14: SignalR Client Hook + MapContainer Wiring

**Files:**
- Create: `client/src/hooks/useTrackHub.ts`
- Modify: `client/src/App.tsx`
- Modify: `client/src/components/map/MapContainer.tsx`

Connects to `/hubs/tracks`, maintains track state as a `Map<entityId, TrackFeature>`, and passes the track array to `MaritimeTrackLayer`.

- [ ] **Step 1: Create useTrackHub hook**

`client/src/hooks/useTrackHub.ts`:
```typescript
import { useState, useEffect } from 'react'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import type { TrackUpdate, TrackFeature, TrackProperties } from '../types'

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
      vesselType: 'Unknown',   // enriched by M3 correlation
      displayName: '',
      lastUpdated: update.timestamp,
    } satisfies TrackProperties,
  }
}

/**
 * Connects to the SignalR TrackHub at /hubs/tracks.
 * Returns the current set of track features as a GeoJSON Feature array.
 * Handles reconnection automatically via SignalR's withAutomaticReconnect().
 */
export function useTrackHub(): TrackFeature[] {
  const [tracks, setTracks] = useState<Map<string, TrackFeature>>(new Map())

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/tracks')
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('TrackUpdate', (update: TrackUpdate) => {
      setTracks(prev => {
        const next = new Map(prev)
        next.set(update.entityId, trackUpdateToFeature(update))
        return next
      })
    })

    connection.start().catch(err => {
      console.warn('TrackHub connection failed:', err)
    })

    return () => {
      connection.stop()
    }
  }, [])

  return Array.from(tracks.values())
}
```

- [ ] **Step 2: Update MapContainer to accept map ref callback and render track layer**

Replace `client/src/components/map/MapContainer.tsx` with the version that renders `MaritimeTrackLayer`:

```tsx
import { useCallback, useEffect, useRef, useState } from 'react'
import maplibregl from 'maplibre-gl'
import 'maplibre-gl/dist/maplibre-gl.css'
import { Protocol } from 'pmtiles'
import { layersWithConfig } from 'protomaps-themes-base'
import { MaritimeTrackLayer } from './MaritimeTrackLayer'
import { useTrackHub } from '../../hooks/useTrackHub'
import type { TrackFeature } from '../../types'

const protocol = new Protocol()
maplibregl.addProtocol('pmtiles', (request) => protocol.tile(request))

const PMTILES_URL = '/tiles/basemap.pmtiles'

function buildMapStyle(): maplibregl.StyleSpecification {
  return {
    version: 8,
    glyphs: 'https://protomaps.github.io/basemaps-assets/fonts/{fontstack}/{range}.pbf',
    sprite: 'https://protomaps.github.io/basemaps-assets/sprites/v4/dark',
    sources: {
      basemap: {
        type: 'vector',
        url: `pmtiles://${PMTILES_URL}`,
        attribution: '© <a href="https://openstreetmap.org">OpenStreetMap</a>',
      },
    },
    layers: [
      { id: 'background', type: 'background', paint: { 'background-color': '#0f172a' } },
      ...layersWithConfig('dark', 'basemap') as maplibregl.LayerSpecification[],
    ],
  }
}

export function MapContainer() {
  const mapContainerRef = useRef<HTMLDivElement>(null)
  const [map, setMap] = useState<maplibregl.Map | null>(null)
  const tracks = useTrackHub()

  useEffect(() => {
    if (!mapContainerRef.current || map) return

    const m = new maplibregl.Map({
      container: mapContainerRef.current,
      style: buildMapStyle(),
      center: [1.0, 51.0],
      zoom: 7,
    })

    m.addControl(new maplibregl.NavigationControl(), 'bottom-right')

    m.on('load', () => {
      setMap(m)

      // Subscribe to all track updates (M2: global; M3+: add bbox)
      m.on('moveend', () => {
        const bounds = m.getBounds()
        // Hub subscription sent — M2 accepts all, M3 will filter server-side
        m.fire('sentinelmap:viewport', { bounds })
      })
    })

    return () => {
      m.remove()
      setMap(null)
    }
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <div ref={mapContainerRef} className="h-full w-full">
      {map && <MaritimeTrackLayer map={map} tracks={tracks} />}
    </div>
  )
}
```

- [ ] **Step 3: Verify TypeScript compiles**

```bash
cd C:/Users/lukeb/source/repos/SentinelMap/client
npx tsc --noEmit 2>&1 | head -30
```
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
cd ..
git add client/src/hooks/useTrackHub.ts client/src/components/map/MapContainer.tsx
git commit -m "feat(m2): add useTrackHub SignalR hook and wire maritime track layer into MapContainer"
```

---

## Task 15: Docker Compose + End-to-End Verification

**Files:**
- Modify: `docker-compose.yml`
- Modify: `.env.example`
- Modify: `src/SentinelMap.Api/appsettings.json`

- [ ] **Step 1: Verify docker-compose.yml env vars**

The `docker-compose.yml` `workers` service already has `SENTINELMAP_DATA_MODE=Simulated` and `ConnectionStrings__Redis=redis:6379` from M1. Verify they are present:

```bash
grep -A 5 "SENTINELMAP_DATA_MODE" docker-compose.yml
grep -A 5 "ConnectionStrings__Redis" docker-compose.yml
```

If either is missing, add them to the `workers` environment section:
```yaml
- SENTINELMAP_DATA_MODE: "Simulated"
- ConnectionStrings__Redis: "redis:6379"
- AISSTREAM_API_KEY: "${AISSTREAM_API_KEY:-}"
```

Also add to the `api` environment section if missing:
```yaml
- SENTINELMAP_DATA_MODE: "Simulated"
```

- [ ] **Step 2: Update .env.example**

Add to `.env.example`:
```env
# Data mode: Simulated (no keys needed) | Live | Hybrid
SENTINELMAP_DATA_MODE=Simulated

# Required only for Live or Hybrid AIS mode
AISSTREAM_API_KEY=your_key_here

# Seed user password (default: Demo123!)
SENTINELMAP_SEED_PASSWORD=Demo123!
```

- [ ] **Step 3: Verify API appsettings.json has Redis**

`src/SentinelMap.Api/appsettings.json` already has correct connection strings from M1. Verify both are present:

```bash
grep -A 4 "ConnectionStrings" src/SentinelMap.Api/appsettings.json
```

Expected: `DefaultConnection` with `Username=sentinel;Password=sentinel_dev_password` and `Redis: redis:6379`. If `Redis` is missing, add it. Do **not** change `DefaultConnection` credentials.

- [ ] **Step 4: Full build verification**

```bash
cd C:/Users/lukeb/source/repos/SentinelMap
dotnet build SentinelMap.slnx 2>&1 | tail -5
```
Expected: 0 Error(s).

```bash
dotnet test SentinelMap.slnx -v minimal 2>&1 | tail -10
```
Expected: All tests pass.

- [ ] **Step 5: Docker Compose stack**

```bash
docker compose down -v  # clean slate
docker compose up --build -d
```

Wait ~60s for first-time image build. Then:

```bash
docker compose ps
```
Expected: All 6 services healthy or running.

```bash
docker compose logs api --tail=20
```
Expected: Migration applied, seed users created, SignalR hub registered.

```bash
docker compose logs workers --tail=20
```
Expected: `IngestionWorker starting for source simulated-ais`, `CorrelationWorker subscribed to observations:*`, `TrackHubService subscribed to entities:updated`.

- [ ] **Step 6: Verify tracks appear in browser**

Open `http://localhost` (or `https://localhost` if Caddy TLS is configured).

Expected:
- Dark map loads (dark background if PMTiles file not downloaded, or full vector basemap if downloaded)
- Browser console shows: `TrackHub connection established` (or similar SignalR log)
- Within ~5 seconds: vessel icons appear on the map and move

To verify vessels are being created in the database:
```bash
docker compose exec db psql -U sentinel sentinelmap -c "SELECT COUNT(*) FROM entities;"
```
Expected: Count > 0 (grows over time).

```bash
docker compose exec db psql -U sentinel sentinelmap -c "SELECT id, status, last_seen FROM entities LIMIT 10;"
```
Expected: 4 rows with `status = Active` and recent `last_seen` timestamps.
Note: `display_name` will be `NULL` in M2 — the CorrelationWorker skeleton does not parse `RawData`. Display names are populated in M3 when the full correlation engine reads vessel metadata.

- [ ] **Step 7: Final commit**

```bash
git add docker-compose.yml .env.example src/SentinelMap.Api/appsettings.json src/SentinelMap.Workers/appsettings.json
git commit -m "feat(m2): Docker Compose env vars — M2 complete, vessels visible on map in Simulated mode"
```

---

## M2 Summary

After completing these tasks:

| Feature | Status |
|---------|--------|
| SimulatedAisConnector — 4 vessels in English Channel | ✓ |
| AisStreamConnector — live WebSocket (requires API key) | ✓ |
| IngestionPipeline — validate → dedup → persist → publish | ✓ |
| CorrelationWorker skeleton — hot-path cache + entity creation | ✓ |
| TrackHub + TrackHubService — Redis → SignalR → browser | ✓ |
| MaritimeTrackLayer — heading-oriented SDF vessel icons | ✓ |
| PMTiles dark basemap + download script | ✓ |
| Seed users (admin/analyst/viewer) | ✓ |
| `docker compose up` shows vessels moving, zero config | ✓ |

**M3 scope:** ADS-B connector (Airplanes.live REST polling), aviation track layer, full entity correlation engine (fuzzy matching, Jaro-Winkler, spatial radius), entity detail panel, and entity identifiers linked across sources.
