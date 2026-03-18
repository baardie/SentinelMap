# SentinelMap

OSINT aggregation and correlation platform — fusing real-time maritime (AIS) and aviation (ADS-B) data into a unified Common Operating Picture.

## Quick Start

```bash
git clone <repo-url>
cd SentinelMap
cp .env.example .env
docker compose up
```

Open `http://localhost` in your browser.

### Demo Accounts

| Username | Role | Clearance |
|----------|------|-----------|
| admin@sentinel.local | Admin | SECRET |
| analyst@sentinel.local | Analyst | OFFICIAL-SENSITIVE |
| viewer@sentinel.local | Viewer | OFFICIAL |

Default password: `Demo123!` (override via `SENTINELMAP_SEED_PASSWORD`)

## Architecture

Six-service Docker Compose deployment:

- **api** — ASP.NET Core REST API + SignalR hub
- **workers** — Background services (ingestion, correlation, alerting)
- **web** — React SPA (Vite + TypeScript)
- **db** — PostgreSQL 16 + PostGIS 3.4
- **redis** — Pub/sub, caching, deduplication
- **caddy** — Reverse proxy, TLS termination

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Backend | .NET 9, ASP.NET Core, EF Core |
| Frontend | React 19, TypeScript, Vite, shadcn/ui, MapLibre GL JS |
| Database | PostgreSQL 16 + PostGIS |
| Cache | Redis 7 |
| Proxy | Caddy 2 |

## Project Structure

```
src/                    .NET backend projects
client/                 React frontend
docs/                   ADRs, threat model, specs
scripts/                Tooling (seeder, migrations, PMTiles download)
docker-compose.yml      Production-default compose
docker-compose.override.yml  Dev overrides (exposed db/redis ports)
```

## Documentation

- [System Design Spec](docs/superpowers/specs/2026-03-18-sentinelmap-system-design.md)
- [Architecture Decision Records](docs/adr/)

## License

MIT
