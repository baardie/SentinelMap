# SentinelMap System Design Specification

**Date**: 2026-03-18
**Status**: Draft
**Author**: Brainstorming session — collaborative design

---

## 1. Overview

SentinelMap is an OSINT aggregation and correlation platform that fuses real-time maritime (AIS) and aviation (ADS-B) data into a unified Common Operating Picture (COP). It targets defence technology reviewers as a portfolio-grade demonstration of full-stack systems engineering.

**Design philosophy**: Two data sources done excellently over five done thinly. AIS + ADS-B give real-time positional data from two domains — the correlation engine demonstrates entity fusion across sources, which is the portfolio differentiator.

**Data sources (v1)**:
- **AIS**: AISStream.io (WebSocket streaming, free API key)
- **ADS-B**: Airplanes.live (REST polling, no auth required)

Both behind `ISourceConnector` so adding OpenSky, AISHub, or any other provider is a config swap, not a rewrite.

---

## 2. Project Structure

```
SentinelMap/
├── src/
│   ├── SentinelMap.Api/            # ASP.NET Core API + SignalR hub
│   ├── SentinelMap.Workers/        # Background services (ingestion, correlation, alerting)
│   ├── SentinelMap.Domain/         # Entities, correlation rules, interfaces, enums
│   ├── SentinelMap.Infrastructure/ # PostgreSQL repos, Redis, external API clients
│   └── SentinelMap.SharedKernel/   # Cross-cutting: audit logging, auth, DTOs
├── client/                         # React app (Vite + TypeScript)
├── docs/                           # Architecture diagrams, threat model, ADRs
├── scripts/                        # Demo seeder, DB migrations, PMTiles download
├── docker-compose.yml
├── docker-compose.override.yml     # Dev overrides (exposed ports for db/redis)
├── SentinelMap.sln
├── README.md
└── LICENSE
```

**Project references**:
```
SentinelMap.Api         → Domain, Infrastructure, SharedKernel
SentinelMap.Workers     → Domain, Infrastructure, SharedKernel
SentinelMap.Domain      → SharedKernel (minimal: value objects, enums)
SentinelMap.Infrastructure → Domain, SharedKernel
SentinelMap.SharedKernel → (no project references)
```

---

## 3. Tech Stack

| Layer | Technology | Version |
|-------|-----------|---------|
| Backend runtime | .NET | 9 |
| Backend framework | ASP.NET Core | 9 |
| Frontend framework | React | 19.2.4 |
| Frontend language | TypeScript | latest |
| Frontend build | Vite | latest |
| UI components | shadcn/ui + Tailwind CSS | latest |
| Map | MapLibre GL JS + PMTiles (Protomaps) | latest |
| Database | PostgreSQL + PostGIS | 16 + 3.4 |
| Cache / Pub-sub | Redis | 7 |
| Auth | ASP.NET Core Identity + custom RBAC | - |
| Reverse proxy | Caddy | 2 |
| Containerisation | Docker Compose | - |

---

## 4. System Architecture

### 4.1 Docker Compose Topology

Six services on an internal `sentinel` bridge network:

| Service | Image | Purpose | External Ports |
|---------|-------|---------|---------------|
| `api` | SentinelMap.Api | REST API + SignalR hub | None (internal) |
| `workers` | SentinelMap.Workers | Ingestion, Correlation, Alerting | None (internal) |
| `web` | client/ build served by Caddy | React SPA | None (internal) |
| `db` | postgis/postgis:16 | PostgreSQL + PostGIS | None (dev: 5432 via override) |
| `redis` | redis:7-alpine | Pub/sub, caching, dedup | None (dev: 6379 via override) |
| `caddy` | caddy:2-alpine | Reverse proxy, TLS | 443, 80 |

`docker-compose.override.yml` exposes db (5432) and redis (6379) ports for local development tooling. Base compose keeps them internal — production defaults, dev convenience via override. This signals awareness of the distinction.

### 4.2 Caddy Routes

- `/api/*` -> `api:5000`
- `/hubs/*` -> `api:5000` (WebSocket upgrade for SignalR)
- `/*` -> `web:80` (React SPA, fallback to `index.html`)

### 4.3 Inter-Service Communication

**No service-to-service HTTP calls.** All coordination via Redis pub/sub and shared PostgreSQL.

```
External APIs → Workers (Ingestion) → PostgreSQL + Redis publish (observations:*)
                                                          ↓
                                        Workers (Correlation) subscribes
                                                          ↓
                                        PostgreSQL + Redis publish (entities:updated)
                                                          ↓
                                        Workers (Alerting) subscribes
                                                          ↓
                                        PostgreSQL + Redis publish:
                                          alerts:* (new alerts)
                                          entities:updated (status transitions, e.g. AIS Dark)
                                                          ↓
                                        API (SignalR Redis backplane) subscribes
                                                          ↓
                                        Client WebSocket
```

### 4.4 Worker Resilience

Each `BackgroundService` in the Workers host has its own health check and independent reconnection logic with exponential backoff. If the correlation worker's Redis subscription dies, it logs degraded health status and attempts reconnection independently — it does not take down the ingestion worker.

The Workers host exposes a `/health` endpoint reporting per-worker status (`healthy`, `degraded`, `stopped`).

---

## 5. Data Ingestion Pipeline

### 5.1 Source Connector Interface

```csharp
public interface ISourceConnector
{
    string SourceId { get; }     // "aisstream", "adsb-airplaneslive"
    string SourceType { get; }   // "AIS", "ADSB"
    IAsyncEnumerable<RawObservation> StreamAsync(CancellationToken ct);
}
```

Single method. The `BackgroundService` that hosts each connector handles start/stop lifecycle, health reporting, and error wrapping. The connector is only responsible for producing observations. A contributor writing a new connector has one method to implement.

### 5.2 Connector Implementations

- **`AisStreamConnector`** — WebSocket to AISStream.io. Persistent connection, auto-reconnects with exponential backoff. Parses AIS message types 1-3 (position), 5 (static/voyage), 18/19 (Class B). Yields via `IAsyncEnumerable`.
- **`AdsbLiveConnector`** — REST polling to Airplanes.live `/v2/point/{lat}/{lon}/{radius}`. Configurable poll interval (default 5s). No auth. Internally loops with `Task.Delay` between polls, yields results.

### 5.3 Pipeline Stages

```
Raw Message → Parse → Validate → Deduplicate → Normalise → Persist → Publish
```

1. **Parse**: Connector-specific JSON -> `RawObservation` DTO
2. **Validate**: FluentValidation — lat/lon bounds, required fields, timestamp sanity (not future, not >24h stale)
3. **Deduplicate**: Redis SET with composite key `{source}:{id}:{lat_4dp}:{lon_4dp}:{ts_bucket}`. 60s TTL buckets. Position-aware: stationary vessels sending identical reports within the same window pass through only once, but genuine position changes always pass. Truncating lat/lon to 4 decimal places (~11m precision) prevents micro-jitter from defeating dedup.
4. **Normalise**: Map to `Observation` domain entity — WGS84 coordinates, UTC timestamps, source-agnostic field names
5. **Persist**: Bulk insert to PostgreSQL via EF Core (batched `SaveChangesAsync`)
6. **Publish**: Redis pub/sub channel `observations:{sourceType}` — lightweight event with observation ID + entity ID + position

### 5.4 Enrichment (async, after persist)

- **Vessel enrichment**: MMSI prefix -> flag state lookup (static CSV table). Vessel type from AIS message type 5.
- **Aircraft enrichment**: ICAO hex -> registration/type lookup from OpenSky aircraft database CSV (loaded at startup, refreshed daily).

### 5.5 Error Handling

- Circuit breaker per connector: 3 failures -> 30s open -> half-open probe
- Bounded in-memory buffer (1000 observations) if PostgreSQL is slow — backpressure drops oldest
- **Backpressure assumption**: drop-oldest is position-safe because newer positions supersede older ones for the same entity. This assumption is documented in code for future contributors who might route non-positional observations through the same pipeline.
- All errors logged with structured context (source, message type, raw payload hash)

### 5.6 Data Modes

Configured via `SENTINELMAP_DATA_MODE` environment variable:

| Mode | Behaviour | API Keys Required |
|------|-----------|-------------------|
| `Simulated` | Static historical data + synthetic live tracks along realistic shipping lanes and flight paths. Also overrides alert thresholds for demo pacing (e.g. `AIS_DARK_TIMEOUT=30s` instead of 15min) so the golden-path scenario plays out in under three minutes. | None |
| `Live` | Real connectors to AISStream + Airplanes.live | `AISSTREAM_API_KEY` (ADS-B: no key) |
| `Hybrid` | Per-source override: `SENTINELMAP_AIS_MODE=simulated`, `SENTINELMAP_ADSB_MODE=live` | Only for live sources |

---

## 6. Entity Correlation Engine

### 6.1 Architecture

Dedicated `CorrelationWorker` (`BackgroundService`) subscribes to Redis channel `observations:*`. Processes new observations independently of ingestion — ingestion and correlation scale and fail independently.

### 6.2 Hot-Path Cache (Critical Performance Optimisation)

Before running the full correlation pipeline, check if the source ID is already linked:

```csharp
// Hot path: already-linked source ID → skip correlation
var existingLink = await _cache.GetEntityForSourceId(observation.SourceId, observation.ExternalId);
if (existingLink != null)
{
    await _entityRepo.UpdatePosition(existingLink.EntityId, observation);
    return;
}
```

Redis cache maps `{source}:{externalId}` -> `entityId`. The full pipeline only runs on first-seen identifiers — a tiny fraction of total traffic. Without this, every AIS message (thousands per minute) runs the full correlation pipeline for an entity resolved on the first message.

### 6.3 Correlation Flow

```
New Observation (first-seen identifier only)
  → Extract identifiers (MMSI, ICAO hex, callsign, name, IMO)
  → Stage 1: Direct ID Match
      → Exact match on MMSI, ICAO, IMO → confidence 0.95
      → NO spatial or temporal filtering — MMSI match is MMSI match regardless of when/where
      → If match found → link observation to existing entity, update cache, done
  → Stage 2: Fuzzy + Spatial (only if no direct match)
      → Candidate set: entities seen in last 24h AND within 50km of observation
      → Name fuzzy match (Jaro-Winkler, threshold 0.75) → confidence 0.5–0.85
      → Spatio-temporal proximity (PostGIS ST_DWithin) → confidence 0.3–0.7
      → Aggregate via noisy-OR: 1 - ∏(1 - cᵢ)
  → Decision:
      → >= 0.6 → Auto-merge into existing entity
      → 0.3–0.6 → Queue for analyst review
      → < 0.3 → Create new entity
```

### 6.4 Two-Path Candidate Retrieval

```csharp
// Direct ID: no spatial/temporal filter, just index lookup
var directMatch = await _identifierRepo.FindEntityByIdentifier(type, value);

// Fuzzy + spatial: filtered candidate set
var candidates = await _entityRepo.FindCandidates(
    seenSince: TimeSpan.FromHours(24),
    withinMetres: 50_000,
    position: observation.Position);
```

Direct match short-circuits for 90%+ of observations. The expensive spatial query and fuzzy matching only runs for the minority without a clean identifier.

### 6.5 Name Normalisation (Pre-Comparison)

Before Jaro-Winkler comparison: strip common prefixes (MV, MT, HMS), collapse whitespace, uppercase everything. Threshold of 0.75 (not 0.85) because vessel names in AIS are manually entered by crew — truncated, abbreviated, typos common. "EVER GIVEN" / "EVERGIVEN" / "EVER GIVN" should all match. False negatives in a demo are invisible; false positives are at least visible and correctable.

### 6.6 Speed-Scaled Spatial Radius

```csharp
var radiusMetres = Math.Max(
    minRadius,
    candidate.LastSpeedMps * timeWindowSeconds * 1.2  // 20% buffer
);
```

Static radii miss fast-moving correlations or produce false positives on slow ones. A vessel at 20kt covers ~10km in 15min; an aircraft at 450kt covers ~120km. One-liner, domain-aware, stands out in code review.

### 6.7 Correlation Rules

```csharp
public interface ICorrelationRule
{
    string RuleId { get; }
    Task<CorrelationScore?> EvaluateAsync(Observation observation, Entity candidate);
}
```

| Rule | Input | Confidence | Notes |
|------|-------|-----------|-------|
| DirectIdMatch | MMSI, ICAO, IMO | 0.95 | Exact match, fast path, no spatial filter |
| NameFuzzyMatch | Display name, callsign | 0.5–0.85 | Jaro-Winkler with normalisation, threshold 0.75 |
| SpatioTemporalProximity | Position + timestamp | 0.3–0.7 | Speed-scaled radius, PostGIS ST_DWithin |

### 6.8 Entity Merge / Split

- **Merge**: Target entity absorbs all identifiers and observations from source entity. Source entity soft-deleted. Recorded in `entity_merges` table with confidence, rule scores, merged_by. Audit event records both IDs.
- **Split**: Analyst selects which observations belong to each resulting entity. New entity created, observations reassigned. Reverse-references `entity_merges` to identify which merge to undo.

### 6.9 Analyst Review Queue

Correlations in the 0.3–0.6 confidence band land in `correlation_reviews` table with status `Pending`. Analysts approve (merge), reject (keep separate), or split (undo previous merge). All actions audit-logged.

---

## 7. Geospatial Visualisation (Frontend)

### 7.1 Tech Stack

- React 19.2.4 + TypeScript + Vite (vanilla SPA, no Next.js)
- MapLibre GL JS with PMTiles dark basemap (self-hosted, air-gappable)
- shadcn/ui + Tailwind CSS with defence theme overrides
- SignalR client (`@microsoft/signalr`) for real-time track updates

### 7.2 Defence Theme Overrides

Applied via `client/src/styles/theme.ts`:

- **Border radius**: 2px globally — sharp corners feel operational, not consumer
- **Typography**: Geist Mono for identifiers (MMSI, ICAO hex, coordinates, timestamps, callsigns). Geist Sans for UI chrome.
- **Colour palette**: slate/zinc for chrome, track-type accent colours for map features, **red reserved exclusively for alerts**. No decorative gradients, no rounded pill shapes.
- **Density**: Tighter padding than shadcn defaults — operational interfaces show more information per pixel.

### 7.3 Map Architecture

```
MapContainer (MapLibre GL JS instance)
├── BasemapLayer (PMTiles dark vector tiles)
├── MaritimeTrackLayer (vessel positions, heading-oriented SVG sprites)
├── AviationTrackLayer (aircraft positions, heading-oriented SVG sprites)
├── TrackHistoryLayer (polyline trails, fade by age)
├── GeofenceLayer (user-drawn/imported polygons)
├── AlertMarkerLayer (pulsing red indicators on alert locations)
├── ClusterLayer (aggregation at zoom < 10)
└── HeatmapLayer (density visualisation, toggleable)
```

Each layer is a composable React component wrapping a MapLibre source + layer pair. Independently toggleable via `LayerControl` panel.

### 7.4 Real-Time Data Flow

```
SignalR Hub (/hubs/tracks)
  → Client subscribes to viewport area (bounding box)
  → Server sends: TrackUpdate, TrackRemove, AlertTriggered, EntityUpdated
  → Client updates GeoJSON source via diffing (not full replace)
  → MapLibre re-renders affected features only
```

### 7.5 Viewport Subscription with Hysteresis

Debounce map move at 300ms **plus** a minimum displacement threshold — only resubscribe if the viewport centre has moved more than 10% of current viewport width since last subscription. Slow continuous pans don't flood the SignalR hub; deliberate jumps trigger promptly.

```typescript
const shouldResubscribe = (prev: BBox, next: BBox): boolean => {
  const viewportWidth = next.east - next.west;
  const centreDeltaLng = Math.abs(
    (next.east + next.west) / 2 - (prev.east + prev.west) / 2
  );
  const centreDeltaLat = Math.abs(
    (next.north + next.south) / 2 - (prev.north + prev.south) / 2
  );
  return centreDeltaLng > viewportWidth * 0.1
      || centreDeltaLat > viewportWidth * 0.1;
};
```

**Buffer zone**: 20% of viewport width on each edge. Entities approaching the visible area are already tracked before they appear — vessels sail into view smoothly, not popping from nothing.

### 7.6 Track Rendering

- **SVG sprites**: simplified ship outline (vessel), simplified plane outline (aircraft), oriented by `heading` via `icon-rotate`
- **Colour by type**: cargo (slate blue), tanker (amber), passenger (teal), aircraft (sky blue), unknown (grey)
- **Staleness**: opacity fades 1.0 -> 0.3 over configurable window (default 5min). After 10min with no update, track removed from map (entity remains in DB as `Stale`).
- **Speed vector**: optional line extending from track icon showing projected position
- **Clustering**: at zoom < 10, MapLibre built-in clustering. Circles sized by count, coloured by dominant type. Click to zoom in.
- **Track history**: toggleable polyline trail per entity. Last 100 positions, line opacity fading with age. Douglas-Peucker simplification server-side for tracks >100 points.

### 7.7 Performance

Tested with 2,000 concurrent tracks (realistic for UK bounding box from free AIS + ADS-B feeds):
- GeoJSON source diffing (update only changed features)
- Viewport culling (render only visible + buffer)
- RequestAnimationFrame batching for position updates arriving faster than frame rate

### 7.8 UI Layout

```
+-------------------------------------------------------------+
| OFFICIAL                                       SentinelMap   |  <- Classification banner
+-------------------------------------------------------------+
| TopBar: Search | Data Mode | System Status | User | Alerts  |
+--------+----------------------------------------------------+
|        |                                                    |
| Side   |              Map View                              |
| Panel  |         (full remaining space)                     |
|        |                                                    |
| Layer  |                                                    |
| Toggle |                                                    |
|        |                                                    |
| Entity |                                                    |
| List   |                                                    |
|        +----------------------------------------------------+
|        | Bottom Bar: Alert Feed (collapsible)               |
+--------+----------------------------------------------------+
| Status Bar: Connection | Track Count | Last Update          |
+-------------------------------------------------------------+
```

**Classification banner**: Full-width bar above TopBar, always visible, colour-coded by highest classification of currently displayed data. Green (OFFICIAL), amber (OFFICIAL-SENSITIVE), red (SECRET). First thing a defence reviewer looks for.

### 7.9 Entity Detail Panel

Slide-out from right on track click:
- **Current status**: position, speed, heading, source, last update timestamp
- **Identity**: all linked identifiers (MMSI, ICAO, callsign, name, flag state)
- **Correlation reasoning** (collapsible, defaults collapsed): each rule that fired, individual scores, combined noisy-OR result. Explainable reasoning chain for analyst trust.
- **Track history**: mini-timeline of positions
- **Linked alerts**: active/historical alerts for this entity
- **Actions** (Analyst+): Add to watchlist, Create geofence around, Export track

### 7.10 Entity Search

TopBar typeahead: search by name, MMSI, ICAO hex, callsign. Debounced 200ms, queries `entity_identifiers` table. Select result -> map flies to entity, opens detail panel.

---

## 8. Alerting & Monitoring

### 8.1 Pipeline Position

Alerting subscribes to `entities:updated` (downstream of correlation, not raw observations). This ensures every alert evaluates against a fully resolved entity with identifiers and confidence scores. No timing race with correlation.

**All alert evaluation happens in one place** — the alerting worker. Including watchlist matching (moved from ingestion). One codepath to debug, audit, and monitor.

### 8.2 Alert Types (v1)

| Alert Type | Detection Method | Severity | Trigger |
|------------|-----------------|----------|---------|
| Geofence Breach | PostGIS `ST_Within` + Redis membership diff | High | State transition (enter/exit) |
| Watchlist Match | Redis hash O(1) lookup | Critical | Any observation matching a watchlisted identifier |
| AIS Dark | Timer-based query (60s interval) | Medium | Active vessel with no signal for configured timeout |
| Speed Anomaly | Speed exceeds type-specific threshold | Medium | Per-observation check |
| Transponder Swap | Same MMSI from divergent positions | High | Per-observation check |
| Correlation Link | Correlation worker creates new entity link | Low | New source linked to existing entity (especially watchlisted) |

**Not in v1**: Route Deviation, Altitude Anomaly (require historical pattern baselines).

### 8.3 Alert Rule Interface

```csharp
public interface IAlertRule
{
    string RuleId { get; }
    AlertType Type { get; }
    Task<AlertTrigger?> EvaluateAsync(Observation observation, Entity entity);
}
```

Rules registered via DI, evaluated in parallel per observation.

### 8.4 Geofence Membership State

Per-entity geofence membership tracked in Redis SET at `geofence:membership:{entityId}`:

```csharp
var currentFences = await _geofenceRepo.FindContaining(position);   // PostGIS
var previousFences = await _redis.SetMembersAsync($"geofence:membership:{entityId}");

var entered = currentFences.Except(previousFences);
var exited = previousFences.Except(currentFences);

foreach (var fenceId in entered) { /* fire entry alert */ }
foreach (var fenceId in exited)  { /* fire exit alert */ }

await _redis.SetReplaceAsync($"geofence:membership:{entityId}", currentFences);
```

Transition detection only — no alerts on every position update inside a fence.

### 8.5 AIS Dark Detection

- Timer-based: every 60s, query entities where `Type=Vessel AND Status=Active AND LastSeen < now - darkTimeout`
- **Startup grace period**: skip dark evaluation for first 2 minutes after worker boot, or filter to entities with `LastSeen > workerBootTime`. Prevents alert storm on container restart when stale vessels appear dark simultaneously.
- Transition to `Status=Dark`, fire alert. On reappearance, fire "Dark Period Ended" informational alert with duration.

### 8.6 Alert Lifecycle

```
Triggered → Acknowledged → Resolved | Dismissed
```

All state transitions are audit-logged. Dismissals require a reason (feeds rule tuning).

### 8.7 Notification Channels

| Channel | Implementation | Rate Limiting |
|---------|---------------|--------------|
| In-App | SignalR `AlertTriggered` event -> bottom bar feed + TopBar badge | N/A |
| Email | MailKit SMTP, per-user subscriptions by type/severity | 10/min per user, digest for bursts |
| Webhook | HMAC-SHA256 signed POST, retry 10s/60s/300s, auto-disable after 10 failures | N/A |

### 8.8 Alert Feed UI

Bottom bar, collapsible. Chronological list with severity colour coding. Click alert -> map flies to entity, opens detail panel with alert highlighted.

---

## 9. Security & Access Control

### 9.1 Authentication

ASP.NET Core Identity handles the plumbing:
- Users/roles in PostgreSQL (Identity tables)
- JWT bearer tokens: RS256, 15-minute access, 7-day refresh
- Refresh tokens with family tracking — reuse detected -> entire family revoked
- Account lockout: 5 failures -> 15-minute lockout
- Password policy: 12+ chars, no complexity rules (NIST 800-63B: length > complexity)

### 9.2 Authorization Pipeline (Program.cs)

Clean, deliberate, named policies:

```csharp
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("ViewerAccess", p => p.RequireRole("Viewer", "Analyst", "Admin"))
    .AddPolicy("AnalystAccess", p => p.RequireRole("Analyst", "Admin"))
    .AddPolicy("AdminAccess", p => p.RequireRole("Admin"))
    .AddPolicy("ClassifiedAccess", p => p.AddRequirements(new ClassificationRequirement()))
    .AddPolicy("AuditWrite", p => p.AddRequirements(new AuditWriteRequirement()));

builder.Services.AddScoped<IAuthorizationHandler, ClassificationAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, AuditWriteAuthorizationHandler>();
```

Endpoints use named policies, not scattered `[Authorize]` attributes:

```csharp
app.MapGet("/api/v1/entities", GetEntities)
    .RequireAuthorization("ViewerAccess", "ClassifiedAccess");
```

### 9.3 RBAC Roles

| Role | Permissions |
|------|------------|
| Viewer | Read-only: map, entities, alerts, own profile |
| Analyst | Viewer + geofences, watchlists, alert management, correlation review, export |
| Admin | Analyst + user management, system config, webhooks, audit log access |

### 9.4 Mock Classification System

Three levels: `OFFICIAL`, `OFFICIAL-SENSITIVE`, `SECRET`.

- Each entity/alert/geofence has a `Classification` property (default `OFFICIAL`)
- Each user has a `ClearanceLevel` (set by admin)
- **API filtering**: EF Core global query filter on `SentinelMapDbContext` — user only sees data at or below their clearance. Scoped via `IUserContext` from `IHttpContextAccessor`.
- **Workers**: `SystemDbContext` without classification filtering. Background processes run at system level; filtering applies at the human interface boundary only. *(See ADR-006.)*
- **UI**: Classification banner reflects highest classification of currently displayed data.

### 9.5 Audit Logging

**Two-path writes** for different reliability guarantees:

```csharp
public interface IAuditService
{
    // Synchronous — blocks until persisted. Use for security events.
    Task WriteSecurityEventAsync(AuditEvent evt);

    // Async via bounded channel — fire and forget. Use for operational events.
    void WriteOperationalEvent(AuditEvent evt);
}
```

- **Security events** (auth failures, role changes, clearance modifications): synchronous inline — the slight latency hit is worth the persistence guarantee. Losing these audit records is a problem.
- **Operational events** (entity viewed, alert acknowledged, geofence created): async bounded channel. If the container crashes mid-flush, these events are lost — acceptable because the operation itself is already persisted.

This distinction demonstrates understanding of audit reliability guarantees.

### 9.6 Sessions UI

User profile includes a "Sessions" tab showing active refresh token families: parsed user-agent (readable device info), last used timestamp, "Revoke" button. Admin sees this for all users.

### 9.7 Infrastructure Security

- **HTTPS**: Caddy auto-provisions TLS (Let's Encrypt production, self-signed dev)
- **Docker networking**: internal `sentinel` bridge. Only Caddy exposes 80/443 externally.
- **CORS**: origin allowlist in API config (default: Caddy host only)
- **Input validation**: FluentValidation on all API request DTOs
- **CSP headers**: Caddy adds `Content-Security-Policy`, `X-Frame-Options`, `X-Content-Type-Options`
- **Rate limiting**: ASP.NET Core middleware. Auth: 5/min, API reads: 100/min, API writes: 30/min.

### 9.8 Seed Users

| Username | Role | Clearance | Password |
|----------|------|-----------|----------|
| `admin@sentinel.local` | Admin | SECRET | `SENTINELMAP_SEED_PASSWORD` env var, default `Demo123!` |
| `analyst@sentinel.local` | Analyst | OFFICIAL-SENSITIVE | Same |
| `viewer@sentinel.local` | Viewer | OFFICIAL | Same |

`SENTINELMAP_SEED_PASSWORD` environment variable overrides the default. Signals awareness that default credentials are a deployment risk.

---

## 10. API & Extensibility

### 10.1 REST API (`/api/v1/`)

Minimal API endpoints (not controllers). Route mapping reads like documentation in `Program.cs`.

| Group | Key Endpoints | Auth Policy |
|-------|--------------|-------------|
| Auth | `POST /auth/login`, `POST /auth/refresh`, `POST /auth/revoke` | Public (login) |
| Entities | `GET /entities`, `GET /entities/{id}`, `GET /entities/{id}/track` | ViewerAccess + ClassifiedAccess |
| Alerts | `GET /alerts`, `PATCH /alerts/{id}/acknowledge`, `PATCH /alerts/{id}/resolve` | ViewerAccess (read), AnalystAccess (mutate) |
| Geofences | `GET /geofences`, `POST /geofences`, `PUT /geofences/{id}`, `DELETE /geofences/{id}` | ViewerAccess (read), AnalystAccess (mutate) |
| Watchlists | `GET /watchlists`, `POST /watchlists/{id}/entries`, `DELETE` | AnalystAccess |
| Correlations | `GET /correlations/pending`, `POST .../approve`, `POST .../reject` | AnalystAccess |
| Admin | `GET /admin/users`, `POST /admin/users`, `PATCH /admin/users/{id}/role` | AdminAccess |
| Webhooks | CRUD | AdminAccess |
| Export | `POST /export` (async job), `GET /export/{jobId}` | AnalystAccess |
| System | `GET /system/status` | ViewerAccess |
| Health | `GET /health` | Public |

### 10.2 API Conventions

- **Pagination**: cursor-based (`?cursor={lastId}&limit=50`). No offset pagination.
- **Errors**: RFC 7807 Problem Details (`application/problem+json`)
- **Filtering**: explicit query parameters per resource, no generic query language
- **Timestamps**: UTC, ISO 8601, `DateTimeOffset` in C#

### 10.3 System Status Endpoint

`GET /api/v1/system/status` returns operational metadata:

```json
{
  "sources": {
    "ais": { "status": "healthy", "lastMessage": "2026-03-18T14:29:58Z", "obsLast1h": 12847 },
    "adsb": { "status": "healthy", "lastMessage": "2026-03-18T14:29:55Z", "obsLast1h": 8432 }
  },
  "correlation": { "queueDepth": 3, "processedLast1h": 21279 },
  "alerts": { "active": 7, "triggeredLast1h": 12 },
  "clients": { "connected": 2 }
}
```

Feeds a status indicator in the UI. Demonstrates operational awareness beyond a health check.

### 10.4 SignalR Hub (`/hubs/tracks`)

```csharp
public class TrackHub : Hub
{
    // Client → Server
    Task SubscribeArea(BoundingBox bbox);
    Task UnsubscribeArea();
    Task SubscribeEntity(Guid entityId);
    Task UnsubscribeEntity(Guid entityId);

    // Server → Client (via Redis backplane)
    // TrackUpdate { EntityId, Position, Heading, Speed, EntityType, Timestamp }
    // TrackRemove { EntityId, Reason }
    // AlertTriggered { AlertId, Type, Severity, EntityId, Summary }
    // EntityUpdated { EntityId, ChangeType }
}
```

Redis backplane enables workers to publish and API to subscribe without service-to-service HTTP. *(See ADR-004.)*

### 10.5 Webhook System

- HMAC-SHA256 signed: `X-Sentinel-Signature: sha256={hex_digest}`
- Webhook secrets stored plaintext — required for HMAC computation. Mitigated by DB encryption at rest and restricted access. Never exposed via API responses.
- Retry: exponential backoff (10s, 60s, 300s)
- Auto-disable after 10 consecutive failures, admin notified
- Delivery log: `webhook_deliveries` table

### 10.6 Data Export

- Formats: GeoJSON, KML, CSV
- Small (<1000 entities): synchronous. Large: async job, download via `GET /export/{jobId}`, auto-cleanup 1hr.
- Classification filter enforced — user exports only data at or below their clearance
- **Classification watermark on exported files**: metadata field/header in GeoJSON/CSV, document description in KML. Marking travels with data. *(See ADR-005.)*

### 10.7 OpenAPI Documentation

Auto-generated via Swashbuckle/NSwag. Available at `/api/docs` in all environments, locked behind `AdminAccess` in production (not disabled). Endpoints are useless without a valid JWT; a reviewer visiting a deployed instance should see well-documented schemas.

### 10.8 Plugin Interface (Future, Not v1)

`ISourceConnector` interface and DI registration pattern make it trivial to add new sources from external assemblies. Documented in README as extension point.

---

## 11. Database Schema

### 11.1 PostgreSQL 16 + PostGIS 3.4

**Migration strategy**: EF Core Migrations in `src/SentinelMap.Infrastructure/Migrations/`. Applied at API startup before `app.Run()` — health endpoint unavailable until migrations complete. Workers use `depends_on: api: condition: service_healthy`.

Raw SQL for PostGIS-specific DDL (spatial indexes, partition creation).

### 11.2 Core Tables

```sql
-- Users (domain projection of Identity's AspNetUsers)
-- Identity handles auth; this table handles domain concerns.
-- Populated on Identity user creation. All FK references point here, not at Identity tables.
CREATE TABLE users (
    id              UUID PRIMARY KEY,  -- same as AspNetUsers.Id
    email           TEXT NOT NULL,
    display_name    TEXT,
    role            TEXT NOT NULL DEFAULT 'Viewer',
    clearance_level TEXT NOT NULL DEFAULT 'Official',
    is_active       BOOLEAN DEFAULT true,
    created_at      TIMESTAMPTZ DEFAULT now()
);

-- Entities
CREATE TABLE entities (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    type                TEXT NOT NULL,    -- 'Vessel', 'Aircraft', 'Unknown'
    display_name        TEXT,
    last_known_position GEOMETRY(Point, 4326),
    last_speed_mps      DOUBLE PRECISION,
    last_heading        DOUBLE PRECISION,
    last_seen           TIMESTAMPTZ,
    status              TEXT NOT NULL DEFAULT 'Active',  -- Active, Stale, Dark, Lost
    classification      TEXT NOT NULL DEFAULT 'Official',
    created_at          TIMESTAMPTZ DEFAULT now(),
    updated_at          TIMESTAMPTZ DEFAULT now()
);
CREATE INDEX idx_entities_position ON entities USING GIST (last_known_position);
CREATE INDEX idx_entities_status ON entities (status, last_seen);

-- Entity Identifiers
CREATE TABLE entity_identifiers (
    id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    entity_id        UUID NOT NULL REFERENCES entities(id),
    identifier_type  TEXT NOT NULL,    -- 'MMSI', 'ICAO', 'IMO', 'Callsign', 'Name'
    identifier_value TEXT NOT NULL,
    source           TEXT NOT NULL,
    first_seen       TIMESTAMPTZ DEFAULT now(),
    last_seen        TIMESTAMPTZ DEFAULT now(),
    UNIQUE (identifier_type, identifier_value)
);

-- Observations (partitioned daily)
CREATE TABLE observations (
    id           BIGINT GENERATED ALWAYS AS IDENTITY,
    entity_id    UUID REFERENCES entities(id),  -- nullable until correlated
    source_type  TEXT NOT NULL,
    external_id  TEXT NOT NULL,
    position     GEOMETRY(Point, 4326),
    speed_mps    DOUBLE PRECISION,
    heading      DOUBLE PRECISION,
    raw_data     JSONB,
    observed_at  TIMESTAMPTZ NOT NULL,
    ingested_at  TIMESTAMPTZ DEFAULT now(),
    PRIMARY KEY (id, observed_at)
) PARTITION BY RANGE (observed_at);

CREATE INDEX idx_observations_position ON observations USING GIST (position);
CREATE INDEX idx_observations_entity ON observations (entity_id, observed_at DESC);
CREATE INDEX idx_observations_uncorrelated
    ON observations (source_type, external_id)
    WHERE entity_id IS NULL;
-- Partial index: if growing rather than near-zero, correlation worker is behind.
-- Exposed as diagnostic on system status endpoint.

-- Entity Merges
CREATE TABLE entity_merges (
    id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    source_entity_id UUID NOT NULL,
    target_entity_id UUID NOT NULL REFERENCES entities(id),
    confidence       DOUBLE PRECISION,
    rule_scores      JSONB,
    merged_by        TEXT NOT NULL,    -- 'system' or user ID
    merged_at        TIMESTAMPTZ DEFAULT now()
);

-- Correlation Reviews
CREATE TABLE correlation_reviews (
    id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    source_entity_id  UUID NOT NULL REFERENCES entities(id),
    target_entity_id  UUID NOT NULL REFERENCES entities(id),
    combined_confidence DOUBLE PRECISION,
    rule_scores       JSONB,
    status            TEXT NOT NULL DEFAULT 'Pending',  -- Pending, Approved, Rejected
    reviewed_by       UUID,
    reviewed_at       TIMESTAMPTZ,
    created_at        TIMESTAMPTZ DEFAULT now()
);

-- Geofences
CREATE TABLE geofences (
    id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name           TEXT NOT NULL,
    geometry       GEOMETRY(Polygon, 4326) NOT NULL,
    fence_type     TEXT NOT NULL DEFAULT 'Both',  -- Entry, Exit, Both
    classification TEXT NOT NULL DEFAULT 'Official',
    created_by     UUID NOT NULL,
    is_active      BOOLEAN DEFAULT true,
    created_at     TIMESTAMPTZ DEFAULT now(),
    updated_at     TIMESTAMPTZ DEFAULT now()
);
CREATE INDEX idx_geofences_geometry ON geofences USING GIST (geometry);

-- Watchlists
CREATE TABLE watchlists (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name        TEXT NOT NULL,
    description TEXT,
    created_by  UUID NOT NULL,
    created_at  TIMESTAMPTZ DEFAULT now()
);

CREATE TABLE watchlist_entries (
    id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    watchlist_id     UUID NOT NULL REFERENCES watchlists(id),
    identifier_type  TEXT NOT NULL,
    identifier_value TEXT NOT NULL,
    reason           TEXT,
    severity         TEXT NOT NULL DEFAULT 'High',
    added_by         UUID NOT NULL,
    added_at         TIMESTAMPTZ DEFAULT now()
);

-- Alerts
CREATE TABLE alerts (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    type            TEXT NOT NULL,
    severity        TEXT NOT NULL,
    entity_id       UUID REFERENCES entities(id),
    geofence_id     UUID REFERENCES geofences(id),
    summary         TEXT NOT NULL,
    details         JSONB,
    status          TEXT NOT NULL DEFAULT 'Triggered',
    acknowledged_by UUID,
    acknowledged_at TIMESTAMPTZ,
    resolved_by     UUID,
    resolved_at     TIMESTAMPTZ,
    classification  TEXT NOT NULL DEFAULT 'Official',
    created_at      TIMESTAMPTZ DEFAULT now()
);
CREATE INDEX idx_alerts_feed ON alerts (status, severity, created_at DESC);

-- Webhook Endpoints
CREATE TABLE webhook_endpoints (
    id                   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    url                  TEXT NOT NULL,
    secret               TEXT NOT NULL,  -- plaintext, required for HMAC. See ADR.
    event_filter         JSONB,
    is_active            BOOLEAN DEFAULT true,
    consecutive_failures INT DEFAULT 0,
    created_by           UUID NOT NULL
);

-- Webhook Deliveries
CREATE TABLE webhook_deliveries (
    id             BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    endpoint_id    UUID NOT NULL REFERENCES webhook_endpoints(id),
    alert_id       UUID NOT NULL REFERENCES alerts(id),
    status         TEXT NOT NULL DEFAULT 'Pending',
    response_code  INT,
    latency_ms     INT,
    attempt_count  INT DEFAULT 0,
    last_attempt_at TIMESTAMPTZ
);

-- Audit Events (partitioned monthly, INSERT only)
CREATE TABLE audit_events (
    id            BIGINT GENERATED ALWAYS AS IDENTITY,
    timestamp     TIMESTAMPTZ NOT NULL DEFAULT now(),
    event_type    TEXT NOT NULL,  -- 'Security', 'Operational'
    user_id       UUID,
    action        TEXT NOT NULL,
    resource_type TEXT NOT NULL,
    resource_id   UUID,
    details       JSONB,
    ip_address    INET,
    PRIMARY KEY (id, timestamp)
) PARTITION BY RANGE (timestamp);

GRANT INSERT ON audit_events TO sentinelmap_app;
REVOKE UPDATE, DELETE ON audit_events FROM sentinelmap_app;

-- Export Jobs
CREATE TABLE export_jobs (
    id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    requested_by   UUID NOT NULL,
    format         TEXT NOT NULL,  -- 'GeoJSON', 'KML', 'CSV'
    parameters     JSONB,
    classification TEXT NOT NULL,  -- watermark for exported file
    status         TEXT NOT NULL DEFAULT 'Queued',
    file_path      TEXT,
    expires_at     TIMESTAMPTZ,
    created_at     TIMESTAMPTZ DEFAULT now()
);
```

### 11.3 Retention

| Table | Strategy | Window |
|-------|----------|--------|
| observations | Drop daily partitions | 30 days (configurable). Driven partly by `raw_data` storage. |
| audit_events | Archive monthly partitions | 1 year minimum. Never deleted. |
| entities | Soft-delete via status | Never hard-deleted |
| export_jobs | Scheduled cleanup | 1 hour after creation |

**Partition management**: A scheduled task in the Workers host creates daily observation partitions 7 days ahead and monthly audit partitions 2 months ahead. Old observation partitions beyond the retention window are dropped by the same task. This avoids runtime partition-creation failures when a new day/month boundary is crossed.

---

## 12. Architecture Decision Records

Deliverables in `docs/adr/`. Each 150-200 words: context, decision, consequences.

| ADR | Title | Summary |
|-----|-------|---------|
| ADR-001 | PMTiles over OSM raster tiles | Air-gapped vector basemap via single static file. No tile server process, dark styling via MapLibre, works offline. |
| ADR-002 | Separate correlation worker over inline pipeline | Ingestion and correlation fail independently. Pipeline ordering ensures observations persist even if correlation is degraded. |
| ADR-003 | shadcn/ui over MUI | Defence aesthetic (owned source code, sharp corners, monospace identifiers). Full control over design system without vendor visual identity. |
| ADR-004 | Redis backplane for SignalR | Workers publish to Redis, API subscribes via backplane. Maintains "no service-to-service HTTP" constraint. |
| ADR-005 | Classification watermark on exports | Marking is inseparable from exported data. Mirrors defence data handling requirements. |
| ADR-006 | SystemDbContext without query filters | Background workers run at system level. Classification filtering applies at the human interface boundary only. |

---

## 13. Demo Scenario (Golden Path)

Scripted scenario that plays out when a reviewer runs `docker compose up` with simulated data mode. No configuration, no API keys required.

| Time | Event | What the reviewer sees |
|------|-------|----------------------|
| T+0s | Three cargo vessels appear in the English Channel, one tanker in the Irish Sea approaching Liverpool. Two commercial aircraft on airways overhead. | Populated COP with moving tracks, correct symbology and heading orientation. |
| T+30s | A watchlisted vessel (MMSI pre-loaded in demo watchlist) enters the area from the south. | **Watchlist Match alert** fires. Alert appears in feed, map marker pulses red. |
| T+60s | The watchlisted vessel enters a pre-loaded geofence covering the Dover Strait. | **Geofence Breach alert** fires. Entity detail panel shows two alerts. |
| T+90s | An aircraft begins loitering near the watchlisted vessel — circular pattern within 5nm. | **Correlation Link alert** fires. Spatio-temporal correlation links them. Entity graph shows the relationship. |
| T+120s | The watchlisted vessel's AIS signal drops. | After configured timeout (30s for demo), **AIS Dark alert** fires. Entity status -> Dark, track fades on map. |
| T+150s | Vessel reappears. | **Dark Period Ended** informational alert. Track resumes, status -> Active. |

In under three minutes: every alert type fires, correlation links entities across sources, classification banner is visible, entity detail shows correlation reasoning, geofence system works. Zero configuration.

**Implementation note**: The simulated data mode seeder must produce these specific scripted events — not just generic synthetic tracks. The golden-path scenario requires timed event injection (watchlist entry, geofence approach, loitering pattern, signal drop/resume) coordinated with pre-loaded watchlists and geofences. This is more complex than basic track simulation and should be scoped accordingly in M6.

---

## 14. Deliverables Summary

### Documents
- `docs/adr/ADR-001.md` through `ADR-006.md`
- `docs/THREAT_MODEL.md` — STRIDE analysis, attack surface diagram, risk matrix
- `README.md` — setup, architecture overview, demo instructions

### Implementation Milestones

| Milestone | Focus |
|-----------|-------|
| M1: Foundation | Project scaffold, Docker Compose, DB schema + migrations, auth skeleton, empty map with PMTiles, ADRs, README structure |
| M2: First Source | AIS ingestion via AISStream WebSocket, maritime track rendering, simulated data mode |
| M3: Second Source + Correlation | ADS-B via Airplanes.live, entity correlation engine, entity detail panel |
| M4: Alerting | Geofences, watchlists, anomaly rules, alert feed, notification channels |
| M5: Security Polish | Classification system, full audit logging, sessions UI, threat model |
| M6: Portfolio Polish | Demo seeder (golden path scenario), final README, architecture diagrams, screen recordings |

**Documentation cadence**: ADRs, threat model, and README are not deferred to M6. Write ADRs as decisions are implemented (M1–M4). Draft the threat model alongside security work (M5). Maintain the README from M1 onwards. M6 is for final polish and the demo seeder — if M3 runs long, documentation isn't what gets cut.
