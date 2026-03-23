# SentinelMap

> OSINT aggregation and correlation platform fusing real-time maritime (AIS) and aviation (ADS-B) data into a unified Common Operating Picture.

SentinelMap is a full-stack systems engineering demonstration: a defence-grade situational awareness platform that ingests, correlates, and presents multi-source track data in real time. It demonstrates dual-source entity correlation, a classification-enforced data model, production-quality security architecture, and an air-gappable deployment — built as a portfolio artefact targeting senior systems/software engineering roles in the defence and security sector.

## Demo

Run `docker compose up`, open `http://localhost`, and log in. Within seconds you will see 12 vessels navigating the Mersey estuary and 8 aircraft operating over Liverpool — all simulated, no API keys required. Within three minutes the demo scenario fires automatically: a vessel triggers a geofence breach on the port approach zone, a watchlist-flagged aircraft generates a match alert, a vessel goes AIS-dark after exceeding the silence threshold, and a speed anomaly fires for an out-of-envelope observation. Alerts appear in the collapsible feed in real time via SignalR. No configuration, no external dependencies.

## Quick Start

### Prerequisites

- Docker Desktop (or Docker Engine + Compose v2)
- Git

### Run

```bash
git clone <repo-url>
cd SentinelMap
docker compose up
```

Open `http://localhost` and log in.

### Demo Accounts

| Email | Role | Clearance | Password |
|---|---|---|---|
| admin@sentinel.local | Admin | SECRET | `SentinelDemo123!` |
| analyst@sentinel.local | Analyst | OFFICIAL-SENSITIVE | `SentinelDemo123!` |
| viewer@sentinel.local | Viewer | OFFICIAL | `SentinelDemo123!` |

The classification banner updates per account. The Viewer account cannot see SECRET or OFFICIAL-SENSITIVE tracks.

Override the seed password via `SENTINELMAP_SEED_PASSWORD` in `.env`.

### Data Modes

| Mode | Description | Required Env Var |
|---|---|---|
| `Simulated` (default) | Deterministic simulated vessels and aircraft — no external calls | None |
| `Live` | Real AIS via AISStream.io WebSocket; real ADS-B via Airplanes.live REST | `AISSTREAM_API_KEY` for AIS |
| `Hybrid` | Live data with simulated fallback on connection failure | `AISSTREAM_API_KEY` for AIS |

Set `SENTINELMAP_DATA_MODE` in `.env`. Per-source overrides available via `SENTINELMAP_AIS_MODE` and `SENTINELMAP_ADSB_MODE`.

## Architecture

### System Diagram

```
Browser ─── Caddy (80/443) ─┬── Web (React SPA)
                             │
                             ├── API (ASP.NET Core)
                             │    ├── SignalR Hub (/hubs/tracks)
                             │    ├── REST API (/api/v1/*)
                             │    └── Identity + JWT
                             │
Redis ◄─────────────────────┤
  (pub/sub, dedup, cache)    │
                             └── Workers
PostgreSQL ◄────────────────────  ├── AIS Ingestion
  (PostGIS, partitioned)         ├── ADS-B Ingestion
                                 ├── Correlation
                                 └── Alerting
```

All inter-service communication passes through Redis pub/sub — no service-to-service HTTP. Workers publish to Redis channels; the API SignalR hub consumes via the Redis backplane and pushes to WebSocket clients.

### Data Pipeline

```
AIS/ADS-B Source → Parse → Validate → Deduplicate → Persist → Publish
                                                         │
                                                    Correlate → Entity
                                                         │
                                                   Alert Rules → Alert Feed
                                                         │
                                                   SignalR → Browser
```

Ingestion and correlation are independent pipeline stages (ADR-002). A slow correlation query cannot backpressure ingestion. A hot-path Redis cache means 90%+ of observations skip the full correlation SQL query entirely.

### Tech Stack

| Layer | Technology |
|---|---|
| Backend | .NET 9, ASP.NET Core, EF Core 9, FluentValidation |
| Frontend | React 19, TypeScript, Vite, Tailwind CSS v4, MapLibre GL JS, shadcn/ui |
| Database | PostgreSQL 16 + PostGIS 3.4 (spatial queries, partitioned observations table) |
| Cache / Messaging | Redis 7 (pub/sub, dedup, geofence membership, SignalR backplane) |
| Reverse Proxy | Caddy 2 (reverse proxy, automatic TLS, CSP headers) |
| Basemap | PMTiles + Protomaps (self-hosted vector tiles, fully air-gappable) |

## Features

### Real-Time Maritime Tracking

AIS data ingested via AISStream.io WebSocket in Live mode, or a deterministic simulator providing 12 vessels with realistic Mersey navigation patterns in Simulated mode. Vessel icons are heading-oriented SVG markers. Selecting a vessel opens an entity detail panel showing MMSI, vessel type, speed, course, last update timestamp, and classification.

### Real-Time Aviation Tracking

ADS-B data polled from Airplanes.live REST API (no key required) in Live mode, or an 8-aircraft simulator covering approach, departure, transit, and helicopter hover patterns over Liverpool. Aircraft icons are sky-blue plane markers. The same entity detail panel applies.

### Entity Correlation Engine

The correlation worker maintains a source-to-entity mapping: each AIS MMSI or ADS-B ICAO hex resolves to a canonical `Entity` record with display name, entity type, and classification. A hot-path Redis cache ensures repeated observations for the same identifier skip the database entirely. Correlation runs as a dedicated `BackgroundService` subscribing to the `observations:*` Redis channel — independent of the ingestion pipeline.

### Alerting System

Four alert types fire in real time:

- **Geofence Breach** — PostGIS `ST_Within` spatial queries with Redis set membership diffing to detect enter/exit transitions without duplicate firing.
- **Watchlist Match** — O(1) Redis hash lookup against a configurable watchlist, with per-entity debounce to suppress repeated alerts.
- **AIS Dark** — Timer-based stale vessel detection. An entity is declared dark when no AIS observation has been received within the configurable timeout (default 900 s, demo 30 s).
- **Speed Anomaly** — Type-specific thresholds: >50 kt for vessels, >600 kt for fixed-wing aircraft.

All alerts are delivered via SignalR to all connected clients within the same subscription scope. The alert feed is collapsible with severity colour coding (red for geofence/watchlist, amber for speed/dark).

### Classification System

Three-tier mock classification: `OFFICIAL`, `OFFICIAL-SENSITIVE`, `SECRET`. EF Core global query filters on `SentinelMapDbContext` ensure every API query is automatically filtered to the authenticated user's clearance level — enforcement is at the ORM layer, not the controller layer. Background workers use a separate `SystemDbContext` with filters disabled (ADR-006). A classification banner in the UI reflects the authenticated user's clearance. All data exports include a classification watermark (ADR-005).

### Security

- **Authentication:** RS256 JWT with 15-minute access tokens. Refresh token rotation with SHA256 hashing and family tracking for reuse detection.
- **Identity:** ASP.NET Core Identity with NIST 800-63B password policy. Account lockout after 5 consecutive failures.
- **Authorisation:** RBAC with three roles (Viewer, Analyst, Admin) enforced via named ASP.NET Core authorization policies.
- **Rate Limiting:** 10 requests/minute on auth endpoints, 100 requests/minute on API read endpoints.
- **Audit Logging:** Two-path logging — synchronous for security events (login, token refresh, lockout), asynchronous for operational events (track queries, alert delivery).
- **Transport:** CSP headers, CORS allowlist, Docker network isolation. Caddy terminates TLS; internal services are not exposed to the host network in production compose.

For the full STRIDE analysis see [Threat Model](docs/THREAT_MODEL.md).

## Project Structure

```
SentinelMap/
├── src/
│   ├── SentinelMap.Api/            # REST API + SignalR hub + auth
│   ├── SentinelMap.Workers/        # Background services (ingestion, correlation, alerting)
│   ├── SentinelMap.Infrastructure/ # Data access, connectors, ingestion pipeline
│   ├── SentinelMap.Domain/         # Entities, domain interfaces, message types
│   └── SentinelMap.SharedKernel/   # Enums, DTOs, shared interfaces
├── client/                         # React frontend (Vite + TypeScript)
├── tests/                          # xUnit test projects (73 tests across 3 projects)
├── docs/
│   ├── adr/                        # Architecture Decision Records (ADR-001–006)
│   ├── THREAT_MODEL.md             # STRIDE analysis, risk matrix, mitigation mapping
│   └── superpowers/                # Specs and milestone plans
├── scripts/                        # Tooling (seeder, migrations, PMTiles download)
├── docker-compose.yml              # Production-default six-service deployment
├── docker-compose.override.yml     # Dev overrides (exposed db/redis ports)
└── Caddyfile                       # Reverse proxy and CSP configuration
```

## Architecture Decision Records

| ADR | Decision | Rationale |
|---|---|---|
| [ADR-001](docs/adr/ADR-001-pmtiles-over-osm-raster.md) | PMTiles over OSM raster tiles | Self-hosted vector tiles enable fully air-gapped deployment; no tile server process required at runtime |
| [ADR-002](docs/adr/ADR-002-separate-correlation-worker.md) | Separate correlation worker over inline pipeline | Independent failure domains — slow correlation cannot backpressure ingestion; hot-path cache absorbs 90%+ of load |
| [ADR-003](docs/adr/ADR-003-shadcn-over-mui.md) | shadcn/ui over MUI | Full ownership of component source enables defence-specific aesthetic without fighting a library's defaults |
| [ADR-004](docs/adr/ADR-004-redis-backplane-for-signalr.md) | Redis backplane for SignalR | Maintains no-service-to-service-HTTP constraint; enables horizontal API scaling without coordination overhead |
| [ADR-005](docs/adr/ADR-005-classification-watermark-exports.md) | Classification watermark on exports | Classification marking must travel with exported data, mirroring real defence data handling requirements |
| [ADR-006](docs/adr/ADR-006-system-dbcontext-without-filters.md) | SystemDbContext without classification filters | Workers need unfiltered access; two DbContext types enforce the boundary at DI registration level, not convention |

## Security

See [Threat Model](docs/THREAT_MODEL.md) for the full STRIDE analysis covering 17 identified threats across 5 trust boundaries, with a risk matrix and mitigation mapping to source code locations.

## Development

### Prerequisites

- .NET 9 SDK
- Node.js 22+
- Docker Desktop

### Local Development

```bash
# Start infrastructure only
docker compose up db redis -d

# API (http://localhost:5000)
cd src/SentinelMap.Api && dotnet run

# Workers
cd src/SentinelMap.Workers && dotnet run

# Frontend (http://localhost:5173)
cd client && npm install && npm run dev
```

### Testing

```bash
dotnet test SentinelMap.slnx
# 73 tests across 3 projects
```

## License

MIT
