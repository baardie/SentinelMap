# SentinelMap — OSINT Aggregation Platform

## Product Requirements Document

**Author:** Luke Baard
**Version:** 1.0
**Date:** March 2026
**Status:** Draft
**Repository:** `github.com/baardie/sentinelmap`

---

## 1. Executive Summary

SentinelMap is a self-hosted, open-source intelligence (OSINT) aggregation platform that ingests data from publicly available sources — maritime AIS feeds, ADS-B aircraft transponders, news APIs, social media, and government datasets — correlates entities across those sources, and renders a unified geospatial picture in real time.

The platform is designed as a portfolio-grade demonstration of the kind of data fusion, situational awareness, and secure system design that defence and national security organisations build at scale. It targets a single-operator or small-team deployment, prioritising clarity of architecture, auditability, and extensibility over enterprise scale.

---

## 2. Problem Statement

Open-source intelligence data is abundant but fragmented. An analyst tracking a vessel of interest must switch between MarineTraffic, FlightRadar24, news feeds, and social platforms — manually correlating identities and timelines across tabs. There is no lightweight, self-hosted tool that fuses these feeds into a single operating picture with alerting and entity correlation.

---

## 3. Target Users

| Persona | Description |
|---|---|
| **Portfolio Reviewer** | A defence tech hiring manager or technical interviewer evaluating engineering depth, domain awareness, and system design skills. |
| **Independent Analyst** | A journalist, researcher, or hobbyist who monitors maritime, aviation, or geopolitical activity using public data. |
| **Small Security Team** | A 2–5 person team in a consultancy or NGO needing a lightweight COP (Common Operating Picture) without enterprise licensing. |

---

## 4. Core Principles

1. **Defence-domain authenticity** — Use real terminology (COP, tracks, entities, fused picture) and design patterns from C4ISR systems, not generic dashboard conventions.
2. **Data fusion over data display** — The value is correlation across sources, not just rendering pins on a map.
3. **Audit-first** — Every data point, query, and user action is logged with provenance. Defence reviewers will look for this.
4. **Pluggable ingestion** — Adding a new data source should require implementing a single interface, not rewiring the system.
5. **Self-hosted and air-gappable** — No hard dependencies on cloud services. All external API calls go through a configurable proxy layer.

---

## 5. Tech Stack

| Layer | Technology | Rationale |
|---|---|---|
| **Backend API** | C# / .NET 9, ASP.NET Core | Primary stack — REST + SignalR for real-time push |
| **Frontend** | React 18 + TypeScript | SPA with MapLibre GL JS for geospatial rendering |
| **Database** | PostgreSQL 16 + PostGIS | Spatial queries, entity storage, audit log |
| **Cache / Pub-Sub** | Redis | Real-time track updates, ingestion queue buffering |
| **Background Workers** | .NET BackgroundService / Hosted Services | Source polling, enrichment pipelines, alerting |
| **Containerisation** | Docker Compose | Single-command deployment, dev parity |
| **Reverse Proxy** | Caddy | Auto TLS, simple config, proven in your infra |
| **CI/CD** | GitHub Actions | Build, test, container publish |

---

## 6. High-Level Architecture

```
┌─────────────────────────────────────────────────────────┐
│                     React Frontend                       │
│  ┌──────────┐  ┌───────────┐  ┌───────────┐            │
│  │ Map View │  │ Entity    │  │ Alert     │            │
│  │ (MapLibre)│  │ Explorer  │  │ Manager   │            │
│  └────┬─────┘  └─────┬─────┘  └─────┬─────┘            │
│       └───────────────┼──────────────┘                   │
│                       │ REST + SignalR (WSS)             │
└───────────────────────┼─────────────────────────────────┘
                        │
┌───────────────────────┼─────────────────────────────────┐
│                  API Gateway (.NET)                       │
│  ┌────────────┐ ┌──────────┐ ┌───────────┐ ┌─────────┐ │
│  │ Track API  │ │Entity API│ │ Alert API │ │Auth/RBAC│ │
│  └──────┬─────┘ └────┬─────┘ └─────┬─────┘ └─────────┘ │
│         └─────────────┼─────────────┘                    │
│                       │                                  │
│  ┌────────────────────┴────────────────────────┐        │
│  │         Correlation Engine (Domain)          │        │
│  └────────────────────┬────────────────────────┘        │
│                       │                                  │
│  ┌──────────┐  ┌──────┴──────┐  ┌──────────────┐       │
│  │ PostgreSQL│  │   Redis     │  │ Audit Logger │       │
│  │ + PostGIS │  │ Pub/Sub     │  │              │       │
│  └──────────┘  └─────────────┘  └──────────────┘       │
└─────────────────────────────────────────────────────────┘
                        │
┌───────────────────────┼─────────────────────────────────┐
│              Ingestion Workers (.NET)                     │
│  ┌─────┐ ┌───────┐ ┌──────┐ ┌──────┐ ┌──────────┐     │
│  │ AIS │ │ADS-B  │ │ News │ │Social│ │ Gov Data │     │
│  │Feed │ │Feed   │ │ API  │ │Media │ │ (CH etc) │     │
│  └─────┘ └───────┘ └──────┘ └──────┘ └──────────┘     │
└─────────────────────────────────────────────────────────┘
```

---

## 7. Feature Specifications (Sub-Documents)

Each feature area has a dedicated specification file in the `specs/` directory:

| # | Spec File | Scope |
|---|---|---|
| 1 | [specs/01-data-ingestion.md](specs/01-data-ingestion.md) | Source connectors, polling architecture, ingestion pipeline, data normalisation |
| 2 | [specs/02-geospatial-visualisation.md](specs/02-geospatial-visualisation.md) | Map rendering, track display, layers, timeline playback, COP layout |
| 3 | [specs/03-entity-correlation.md](specs/03-entity-correlation.md) | Cross-source entity resolution, identity graph, confidence scoring |
| 4 | [specs/04-alerting-monitoring.md](specs/04-alerting-monitoring.md) | Geofencing, watchlists, anomaly triggers, notification channels |
| 5 | [specs/05-security-access-control.md](specs/05-security-access-control.md) | Authentication, RBAC, audit logging, mock classification handling |
| 6 | [specs/06-api-extensibility.md](specs/06-api-extensibility.md) | Public REST API, webhook system, custom source plugin interface |

---

## 8. Data Model (Conceptual)

### Core Entities

- **Track** — A time-series of positional reports for a single platform (vessel, aircraft, ground asset). Keyed by source-specific ID (MMSI, ICAO hex, etc.).
- **Entity** — A fused identity that may link multiple tracks and non-positional references (news mentions, social posts). The correlation engine creates and maintains these.
- **Observation** — A single data point from any source: position report, news article, social post, government filing. Always tagged with source, timestamp, and confidence.
- **Alert** — A triggered rule (geofence breach, watchlist match, anomaly) linked to one or more entities.
- **AuditEvent** — An immutable log entry for every user action, API call, and system event.

### Simplified ERD

```
Entity (1) ──────< (M) Track
Entity (1) ──────< (M) Observation
Track  (1) ──────< (M) PositionReport
Alert  (M) ──────> (1) Entity
AuditEvent ─────── standalone, append-only
```

---

## 9. Deployment Strategy

### Development
```bash
docker compose up
```
Single command spins up API, frontend dev server, PostgreSQL, Redis. Hot reload on both .NET and React.

### Production (Self-Hosted)
- Docker Compose on a Hetzner VPS (your existing infra pattern)
- Caddy reverse proxy with automatic HTTPS
- PostgreSQL with daily pg_dump backups to object storage
- Environment-based config for API keys (no secrets in repo)

### CI/CD
- GitHub Actions: build → test → container push → optional deploy via SSH
- Branch protection on `main`, PR-based workflow

---

## 10. Milestones

| Phase | Scope | Target |
|---|---|---|
| **M1 — Foundation** | Project scaffold, Docker Compose, PostgreSQL + PostGIS schema, auth skeleton, empty map view | Week 1–2 |
| **M2 — First Source** | AIS ingestion worker, maritime track rendering on map, basic track history | Week 3–4 |
| **M3 — Second Source + Correlation** | ADS-B ingestion, entity correlation engine (link vessel to nearby aircraft), entity detail panel | Week 5–6 |
| **M4 — Alerting** | Geofence creation UI, watchlist management, alert feed, email/webhook notifications | Week 7–8 |
| **M5 — News + Social** | News API ingestion, social media connector, observation timeline on entity panel | Week 9–10 |
| **M6 — Polish + Portfolio** | README, architecture diagrams, threat model, demo data seeder, screen recordings, blog post | Week 11–12 |

---

## 11. Success Criteria (Portfolio Lens)

A defence tech interviewer reviewing this repository should be able to:

1. **Understand the domain** — README explains the OSINT/C4ISR context in accessible terms.
2. **Run it locally in under 5 minutes** — `docker compose up` with a demo data seeder.
3. **See real architecture** — Clean separation of ingestion, domain, and presentation layers.
4. **Find the security story** — RBAC, audit log, mock classifications, threat model in docs.
5. **Evaluate code quality** — Tests, CI pipeline, consistent patterns, no shortcuts in the domain layer.
6. **See extensibility** — Adding a new source is implementing `ISourceConnector` and registering it.

---

## 12. Out of Scope (v1)

- Machine learning / NLP-based entity extraction (future consideration)
- Real-time collaboration / multi-user cursors
- Mobile-native clients
- Classified data handling (this is a mock classification system for demonstration)
- Commercial API subscriptions (all sources should have free tiers or open alternatives)

---

## 13. Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| AIS/ADS-B free API rate limits | Gaps in track data | Implement graceful degradation, cache aggressively, support multiple provider failover |
| PostGIS query performance at scale | Slow map rendering | Spatial indexing, track decimation for zoom levels, materialized views for hot queries |
| Scope creep into ML/NLP | Delays M1–M6 delivery | Hard boundary — v1 uses rule-based correlation only |
| API key exposure | Security incident | `.env` files in `.gitignore`, Docker secrets, no keys in CI logs |

---

*Sub-specifications follow in the `specs/` directory.*
