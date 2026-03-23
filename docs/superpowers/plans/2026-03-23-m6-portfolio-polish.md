# M6: Portfolio Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a golden-path demo seeder that showcases every feature in under 3 minutes, write a comprehensive portfolio-grade README with architecture diagrams, fix the seed password in README, and ensure `docker compose up` delivers a zero-configuration demo experience.

**Spec:** `docs/superpowers/specs/2026-03-18-sentinelmap-system-design.md` — Section 13 (Demo Scenario)

**Codebase state:** M5 complete. All features working: dual-source ingestion (AIS+ADS-B), correlation, alerting (geofence breach, watchlist match, AIS dark, speed anomaly), auth with JWT+refresh tokens, classification system, audit logging, rate limiting.

**IMPORTANT:** All work must stay within `C:\Users\lukeb\source\repos\SentinelMap`.

---

## Task 1: Demo Data Seeder

**Context:** On startup in Simulated mode, the system should pre-load a demo geofence and watchlist entry so that the golden-path scenario triggers alerts automatically. Currently, geofences and watchlists are empty — alerts only fire if someone creates them via the API.

**Files:**
- Create: `src/SentinelMap.Api/Services/DemoSeeder.cs`
- Modify: `src/SentinelMap.Api/Program.cs` (call DemoSeeder after UserSeeder)
- Modify: `src/SentinelMap.Infrastructure/Connectors/SimulatedAisConnector.cs` (add watchlisted vessel + dark period)

- [ ] **Step 1: Create DemoSeeder**

`DemoSeeder.cs` runs after `UserSeeder` in Simulated mode only. It:

1. **Creates a demo geofence** — "Liverpool Bay Restricted Zone" polygon covering the Crosby Channel / Narrows area where vessels transit. Coordinates should intersect the path of at least one simulated vessel.
   ```
   Polygon roughly: [(-3.12,53.44), (-2.98,53.44), (-2.98,53.47), (-3.12,53.47), (-3.12,53.44)]
   ```
   This covers The Narrows and Seaforth approach — MERSEY TRADER and ATLANTIC BULKER both transit through here.

2. **Creates a demo watchlist** — "Vessels of Interest" with one entry:
   - IdentifierType: "MMSI", IdentifierValue: "636092345" (PACIFIC HARMONY — the anchored vessel)
   - OR use "235009888" (MERSEY TRADER — the inbound container ship) for more dramatic effect
   - Reason: "Under investigation — suspicious cargo manifest"
   - Severity: Critical

3. **Sets `AIS_DARK_TIMEOUT_SECONDS=30`** in Simulated mode (override for demo pacing)

Only runs if the geofences/watchlists tables are empty (idempotent).

- [ ] **Step 2: Add a vessel that goes dark**

Edit `SimulatedAisConnector.cs` — add a new vessel or modify an existing transit vessel to simulate an AIS dark period:

After completing its transit path, one vessel should stop emitting for ~35 seconds (longer than the 30s demo timeout), then resume. This can be done by adding a `DarkPeriodTicksRemaining` counter in the `VesselState` class.

Pick MERSEY TRADER (the inbound container ship) — when it reaches Seaforth, it pauses and goes dark for 35 seconds, then resumes transmitting.

- [ ] **Step 3: Wire DemoSeeder into Program.cs**

After `UserSeeder.SeedAsync(app.Services)`:
```csharp
if (Environment.GetEnvironmentVariable("SENTINELMAP_DATA_MODE")?.ToLowerInvariant() != "live")
{
    await DemoSeeder.SeedAsync(app.Services);
}
```

- [ ] **Step 4: Set AIS dark timeout for simulated mode**

In Workers `Program.cs` or via docker-compose environment, set `AIS_DARK_TIMEOUT_SECONDS=30` when in Simulated mode. Or pass it via `docker-compose.yml` environment section for the workers service.

- [ ] **Step 5: Build and test**

- [ ] **Step 6: Commit**

---

## Task 2: Comprehensive README

**Context:** The current README is minimal. Rewrite it as a portfolio-grade document that a defence technology reviewer would read.

**Files:**
- Rewrite: `README.md`

- [ ] **Step 1: Write comprehensive README**

Structure:

```markdown
# SentinelMap

> OSINT aggregation and correlation platform fusing real-time maritime (AIS) and aviation (ADS-B) data into a unified Common Operating Picture.

[One-paragraph elevator pitch targeting defence tech reviewers]

## Demo

[3-5 sentences describing what happens when you run `docker compose up`]
[Mention: 12 vessels, 8 aircraft, automatic alerts, zero configuration]

## Quick Start

### Prerequisites
- Docker Desktop
- Git

### Run
docker compose up
Open http://localhost
Login: admin@sentinel.local / SentinelDemo123!

### Data Modes
[Table: Simulated (default), Live, Hybrid with env vars]

## Architecture

### System Overview
[Text-based architecture diagram showing all 6 services and data flow]

### Data Pipeline
[Raw Source → Parse → Validate → Deduplicate → Persist → Publish → Correlate → Alert]

### Tech Stack
[Table with layers]

## Features

### Real-Time Maritime Tracking (AIS)
[Brief description + what the reviewer sees]

### Real-Time Aviation Tracking (ADS-B)
[Brief description]

### Entity Correlation Engine
[Hot-path cache, speed-scaled radius, entity resolution]

### Alerting System
[4 alert types, rule evaluation pipeline, real-time SignalR push]

### Classification System
[Three levels, EF Core query filters, classification banner]

### Security
[JWT RS256, refresh token rotation with family tracking, rate limiting, RBAC, audit logging]

## Project Structure
[Clean tree showing key directories]

## API Reference
[Key endpoint groups with examples]

## Architecture Decision Records
[List all 6 ADRs with one-line summaries]

## Security
[Link to threat model, mention STRIDE analysis]

## Development

### Prerequisites
- .NET 9 SDK
- Node.js 22
- Docker Desktop

### Local Development
[dotnet run, npm run dev, docker compose for infra]

### Testing
[dotnet test — X tests]

## License
MIT
```

Fix the password from `Demo123!` to `SentinelDemo123!`.

- [ ] **Step 2: Commit**

---

## Task 3: .env.example

**Context:** The README references `cp .env.example .env` but the file may not exist.

**Files:**
- Create: `.env.example`

- [ ] **Step 1: Create .env.example**

```env
# SentinelMap Configuration

# Data mode: Simulated (default), Live, Hybrid
SENTINELMAP_DATA_MODE=Simulated

# Per-source overrides (optional)
# SENTINELMAP_AIS_MODE=live
# SENTINELMAP_ADSB_MODE=live

# Required for Live AIS mode
# AISSTREAM_API_KEY=your-key-here

# Seed user password (min 12 chars)
# SENTINELMAP_SEED_PASSWORD=SentinelDemo123!

# AIS dark detection timeout (seconds, default 900, 30 for demo)
# AIS_DARK_TIMEOUT_SECONDS=30
```

- [ ] **Step 2: Commit**

---

## Task 4: Docker Compose E2E + Final Verification

- [ ] **Step 1: Full rebuild and test**
- [ ] **Step 2: Verify golden path demo**
  - Login → COP loads with vessels + aircraft
  - Within 30s: a vessel enters the geofence → Geofence Breach alert
  - Within 30s: watchlisted vessel detected → Watchlist Match alert
  - After ~60s: MERSEY TRADER goes dark → AIS Dark alert fires after 30s timeout
  - Alert feed shows all alerts with severity colours
  - Click alert → map flies to entity
  - Entity detail panel shows alert history
- [ ] **Step 3: Fix any issues**
- [ ] **Step 4: Final commit**
