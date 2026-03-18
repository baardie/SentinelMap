# Spec 01 — Data Ingestion & Source Connectors

**Parent:** [PRD.md](../PRD.md)
**Status:** Draft

---

## 1. Overview

The ingestion layer is responsible for pulling data from external public sources, normalising it into a common internal format, and publishing it to the correlation engine. Every source is implemented as a pluggable connector behind a shared interface, making the system trivially extensible.

---

## 2. Connector Interface

All source connectors implement a single .NET interface:

```csharp
public interface ISourceConnector
{
    string SourceId { get; }              // e.g. "ais-aisstream", "adsb-opensky"
    SourceType SourceType { get; }        // Maritime, Aviation, News, Social, Government
    TimeSpan PollingInterval { get; }     // How often to poll (ignored for streaming sources)
    
    Task<IReadOnlyList<RawObservation>> PollAsync(CancellationToken ct);
    
    // Optional: for WebSocket/SSE streaming sources
    IAsyncEnumerable<RawObservation>? StreamAsync(CancellationToken ct) => null;
}
```

### RawObservation (Normalised Ingestion DTO)

```csharp
public record RawObservation
{
    public required string SourceId { get; init; }
    public required string ExternalId { get; init; }    // MMSI, ICAO hex, article URL, etc.
    public required DateTimeOffset Timestamp { get; init; }
    public required ObservationType Type { get; init; }  // Position, Article, SocialPost, Filing
    
    // Positional (nullable for non-geo observations)
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? Altitude { get; init; }       // Feet for aviation, null for maritime
    public double? SpeedKnots { get; init; }
    public double? HeadingDegrees { get; init; }
    
    // Content (nullable for positional-only observations)
    public string? Title { get; init; }
    public string? Body { get; init; }
    public string? Url { get; init; }
    
    // Arbitrary source-specific metadata
    public Dictionary<string, string> Metadata { get; init; } = new();
    
    // Confidence in the data point (0.0–1.0)
    public double Confidence { get; init; } = 1.0;
}
```

---

## 3. Source Connectors (v1)

### 3.1 Maritime — AIS

**Provider:** AISStream.io (free WebSocket API) with fallback to UN Global Fishing Watch API.

| Field | Detail |
|---|---|
| **Protocol** | WebSocket (streaming) |
| **Auth** | API key (free tier) |
| **Rate Limit** | ~1 msg/sec on free tier |
| **Key Fields** | MMSI, vessel name, lat/lon, SOG, COG, ship type, destination, ETA |
| **Polling Interval** | N/A (streaming) |

**Implementation Notes:**
- Connect via `ClientWebSocket`, deserialise JSON frames.
- Map MMSI → `ExternalId`, ship type → `Metadata["vessel_type"]`.
- Handle reconnection with exponential backoff (max 60s).
- Deduplicate by MMSI + timestamp (AIS can send duplicate position reports).
- Store raw AIS message type in metadata for auditability.

### 3.2 Aviation — ADS-B

**Provider:** OpenSky Network REST API (free, no key required for anonymous access).

| Field | Detail |
|---|---|
| **Protocol** | REST (polling) |
| **Auth** | Anonymous (rate-limited) or registered (higher limits) |
| **Rate Limit** | Anonymous: 10 req/min. Registered: 1 req/5sec |
| **Key Fields** | ICAO24 hex, callsign, lat/lon, altitude, velocity, heading, on_ground |
| **Polling Interval** | 15 seconds (registered) / 60 seconds (anonymous) |

**Implementation Notes:**
- Use `/api/states/all` endpoint with optional bounding box filter.
- Map ICAO24 → `ExternalId`, callsign → `Metadata["callsign"]`.
- Filter `on_ground = true` tracks to reduce noise (configurable).
- Altitude in metres from API → convert to feet for aviation convention, store both.

### 3.3 News

**Provider:** NewsAPI.org (free developer tier, 100 req/day) with fallback to GNews API.

| Field | Detail |
|---|---|
| **Protocol** | REST (polling) |
| **Auth** | API key |
| **Rate Limit** | 100 requests/day (NewsAPI free tier) |
| **Key Fields** | Title, description, URL, source name, published date |
| **Polling Interval** | 15 minutes |

**Implementation Notes:**
- Query with configurable keyword sets (e.g. "maritime security", "military aircraft", "defence").
- Article URL → `ExternalId` (deduplicate on URL).
- No geolocation by default — geo-tagging is handled by the enrichment pipeline (see §5).
- Store full article snippet in `Body`, link in `Url`.

### 3.4 Social Media

**Provider:** Reddit API (free, OAuth) for r/OSINT, r/geopolitics, r/ADSB, etc. Twitter/X API if available.

| Field | Detail |
|---|---|
| **Protocol** | REST (polling) |
| **Auth** | OAuth2 (Reddit), Bearer token (X) |
| **Rate Limit** | Reddit: 100 req/min. X: varies by tier |
| **Key Fields** | Post title, body, subreddit/account, permalink, score |
| **Polling Interval** | 5 minutes |

**Implementation Notes:**
- Monitor configurable subreddit list and keyword searches.
- Reddit permalink → `ExternalId`.
- Score/upvotes → feed into `Confidence` weighting.
- Respect API ToS — no scraping, proper user-agent headers.

### 3.5 Government Data — UK Companies House

**Provider:** Companies House REST API (free, API key required).

| Field | Detail |
|---|---|
| **Protocol** | REST (polling) |
| **Auth** | API key (free) |
| **Rate Limit** | 600 req/5min |
| **Key Fields** | Company number, name, registered address, officers, filing history, SIC codes |
| **Polling Interval** | On-demand (search-triggered, not continuous polling) |

**Implementation Notes:**
- Primarily used for entity enrichment — when a vessel owner or company is identified, pull Companies House data to flesh out the entity profile.
- SIC codes relevant to defence/maritime/aviation flagged automatically.
- Integrates with your existing CompanyLens work — potential shared module.

---

## 4. Ingestion Pipeline

```
Source Connector
    │
    ▼
RawObservation
    │
    ▼
┌─────────────────────┐
│  Validation Layer    │  ← Schema validation, bounds checking (lat -90..90, etc.)
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│  Deduplication       │  ← Redis SET check: source_id + external_id + timestamp hash
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│  Normalisation       │  ← Unit conversion, timezone normalisation (all UTC),
└──────────┬──────────┘    coordinate system validation
           │
           ▼
┌─────────────────────┐
│  Persistence         │  ← Write to PostgreSQL (observations table + PostGIS geometry)
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│  Redis Publish       │  ← Publish to channel for real-time consumers
└─────────────────────┘    (correlation engine, SignalR hub, alerting)
```

---

## 5. Enrichment Pipeline

Runs asynchronously after ingestion, decorating observations with derived data:

| Enrichment | Input | Output | Source |
|---|---|---|---|
| **Geo-tagging** | News article text | Lat/lon if location mentioned | Simple NER with regex patterns for known locations + a gazetteer lookup. No ML in v1. |
| **Vessel Enrichment** | MMSI | Flag state, vessel type, owner, IMO number | ITU MMSI prefix table (bundled), MarineTraffic (if API available) |
| **Aircraft Enrichment** | ICAO24 hex | Registration, aircraft type, operator | OpenSky aircraft database (CSV, bundled) |
| **Company Enrichment** | Company name/number | Officers, SIC codes, filing status | Companies House API |

---

## 6. Configuration

All source connectors are configured via `appsettings.json` with environment variable overrides:

```json
{
  "Ingestion": {
    "Sources": {
      "AIS": {
        "Enabled": true,
        "ApiKey": "${AIS_API_KEY}",
        "ReconnectMaxDelaySecs": 60,
        "BoundingBox": { "MinLat": 49.0, "MaxLat": 61.0, "MinLon": -11.0, "MaxLon": 2.0 }
      },
      "ADSB": {
        "Enabled": true,
        "PollingIntervalSecs": 15,
        "Username": "${OPENSKY_USERNAME}",
        "Password": "${OPENSKY_PASSWORD}",
        "FilterOnGround": true
      },
      "News": {
        "Enabled": true,
        "ApiKey": "${NEWS_API_KEY}",
        "Keywords": ["maritime security", "military aircraft", "defence contract", "naval exercise"],
        "PollingIntervalMins": 15
      }
    },
    "Pipeline": {
      "DeduplicationWindowMins": 60,
      "MaxBatchSize": 500,
      "RetryPolicy": { "MaxRetries": 3, "BackoffMultiplier": 2.0 }
    }
  }
}
```

---

## 7. Error Handling & Resilience

| Scenario | Behaviour |
|---|---|
| Source API unreachable | Exponential backoff (1s → 2s → 4s → ... → max 60s), log warning, emit health check degraded status |
| Rate limit hit (429) | Respect `Retry-After` header, fall back to reduced polling interval |
| Malformed data from source | Log and skip individual observation, increment `ingestion_errors_total` metric, never crash the worker |
| Redis unavailable | Buffer observations in memory (bounded queue, 10k max), flush when reconnected |
| PostgreSQL unavailable | Circuit breaker pattern — after 5 consecutive failures, pause ingestion for 30s, retry |

---

## 8. Observability

Each connector emits structured logs and metrics:

- `ingestion_observations_total{source, type}` — counter of observations ingested
- `ingestion_errors_total{source, error_type}` — counter of failures
- `ingestion_latency_seconds{source}` — histogram of end-to-end ingestion time
- `source_health_status{source}` — gauge (0 = down, 1 = degraded, 2 = healthy)

Exposed via `/metrics` endpoint (Prometheus format) for optional Grafana dashboarding.

---

## 9. Testing Strategy

| Level | Scope |
|---|---|
| **Unit** | Each connector's parsing/normalisation logic with fixture data (recorded API responses) |
| **Integration** | Full pipeline with a test PostgreSQL + Redis via Docker Compose test profile |
| **Contract** | Validate external API response shapes against recorded fixtures — detect breaking changes |
| **Load** | Simulate 1,000 observations/sec through the pipeline to verify backpressure handling |
