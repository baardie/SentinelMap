# Spec 06 — API & Extensibility

**Parent:** [PRD.md](../PRD.md)
**Status:** Draft

---

## 1. Overview

SentinelMap exposes a documented REST API for all functionality, a real-time SignalR hub for live data, a webhook system for outbound event delivery, and a plugin interface for custom source connectors. The system is designed to be operated headlessly via API if needed — the React frontend is a consumer of the same API, not a privileged client.

---

## 2. REST API Design

### 2.1 Conventions

| Aspect | Convention |
|---|---|
| **Base URL** | `/api/v1/` |
| **Auth** | Bearer JWT in `Authorization` header |
| **Format** | JSON (request and response) |
| **Pagination** | Cursor-based: `?cursor={lastId}&limit=50` (default 50, max 200) |
| **Filtering** | Query string parameters: `?type=Vessel&source=ais` |
| **Sorting** | `?sort=timestamp&order=desc` |
| **Errors** | RFC 7807 Problem Details: `{ type, title, status, detail, instance }` |
| **Versioning** | URL path (`/v1/`). Breaking changes → new version. |
| **Rate Limiting** | 120 req/min per user. `X-RateLimit-Remaining` header. 429 with `Retry-After`. |

### 2.2 Endpoint Catalogue

#### Entities

| Method | Path | Description | Role |
|---|---|---|---|
| `GET` | `/entities` | List entities (paginated, filterable by type, source, name search) | Viewer |
| `GET` | `/entities/{id}` | Full entity detail with identifiers, observation summary | Viewer |
| `GET` | `/entities/{id}/observations` | All linked observations (paginated) | Viewer |
| `GET` | `/entities/{id}/graph?depth=2` | Relationship graph | Viewer |
| `PUT` | `/entities/{id}/annotations` | Add/update analyst notes on entity | Analyst |
| `POST` | `/entities/{id}/merge` | Manually merge with another entity | Analyst |
| `POST` | `/entities/{id}/split` | Split entity into two | Analyst |

#### Tracks

| Method | Path | Description | Role |
|---|---|---|---|
| `GET` | `/tracks/live?bbox={bbox}` | All current tracks in bounding box | Viewer |
| `GET` | `/tracks/snapshot?timestamp={ts}&bbox={bbox}` | Historical positions at timestamp | Viewer |
| `GET` | `/tracks/{entityId}/history?from={ts}&to={ts}` | Track polyline for entity | Viewer |
| `GET` | `/tracks/export?entityIds={ids}&format=geojson` | Export tracks as GeoJSON or KML | Analyst |

#### Alerts

| Method | Path | Description | Role |
|---|---|---|---|
| `GET` | `/alerts` | List alerts (filterable by type, priority, status) | Viewer |
| `GET` | `/alerts/{id}` | Alert detail | Viewer |
| `PATCH` | `/alerts/{id}/acknowledge` | Acknowledge an alert | Analyst |
| `PATCH` | `/alerts/{id}/dismiss` | Dismiss an alert | Analyst |

#### Geofences

| Method | Path | Description | Role |
|---|---|---|---|
| `GET` | `/geofences` | List all geofences | Viewer |
| `POST` | `/geofences` | Create geofence | Analyst |
| `PUT` | `/geofences/{id}` | Update geofence | Analyst |
| `DELETE` | `/geofences/{id}` | Deactivate geofence (soft delete) | Analyst |

#### Watchlists

| Method | Path | Description | Role |
|---|---|---|---|
| `GET` | `/watchlists` | List all watchlists | Viewer |
| `POST` | `/watchlists` | Create watchlist | Analyst |
| `POST` | `/watchlists/{id}/entries` | Add entry to watchlist | Analyst |
| `DELETE` | `/watchlists/{id}/entries/{entryId}` | Remove entry | Analyst |

#### Correlation Reviews

| Method | Path | Description | Role |
|---|---|---|---|
| `GET` | `/correlations/reviews?status=pending` | Pending review queue | Analyst |
| `POST` | `/correlations/reviews/{id}/confirm` | Confirm a correlation | Analyst |
| `POST` | `/correlations/reviews/{id}/reject` | Reject a correlation | Analyst |

#### Admin

| Method | Path | Description | Role |
|---|---|---|---|
| `GET` | `/admin/users` | List users | Admin |
| `POST` | `/admin/users` | Create user | Admin |
| `PATCH` | `/admin/users/{id}` | Update role/clearance/status | Admin |
| `GET` | `/admin/audit` | Query audit log | Admin |
| `GET` | `/admin/sources` | List source connector statuses | Admin |
| `PATCH` | `/admin/sources/{sourceId}` | Enable/disable a source | Admin |
| `GET` | `/admin/health` | System health (DB, Redis, connectors) | Admin |

#### System

| Method | Path | Description | Auth |
|---|---|---|---|
| `GET` | `/health` | Liveness probe (200 OK) | None |
| `GET` | `/health/ready` | Readiness probe (checks DB + Redis) | None |
| `GET` | `/metrics` | Prometheus metrics endpoint | None (IP allowlist) |

---

## 3. SignalR Hub

**Hub URL:** `/hubs/tracks`

**Authentication:** Bearer JWT passed as query parameter (SignalR convention for WebSocket auth):

```typescript
const connection = new signalR.HubConnectionBuilder()
  .withUrl("/hubs/tracks", { accessTokenFactory: () => getToken() })
  .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
  .build();
```

### Events (Server → Client)

| Event | Payload | Description |
|---|---|---|
| `TrackUpdate` | `{ entityId, lat, lon, heading, speed, altitude?, source, timestamp }` | Position update for a tracked entity |
| `TrackRemove` | `{ entityId, reason }` | Entity track gone stale |
| `AlertTriggered` | `{ alertId, type, priority, entityId, title }` | New alert fired |
| `AlertResolved` | `{ alertId }` | Alert auto-resolved |
| `EntityUpdated` | `{ entityId, changeType }` | Entity merged, split, or re-correlated |

### Methods (Client → Server)

| Method | Parameters | Description |
|---|---|---|
| `SubscribeArea` | `{ bbox, entityTypes? }` | Only receive updates within bounding box |
| `UnsubscribeArea` | `{}` | Stop area filtering (receive all) |
| `SubscribeEntity` | `{ entityId }` | Receive all updates for a specific entity |
| `UnsubscribeEntity` | `{ entityId }` | Stop entity-specific subscription |

**Backpressure:** If a client is slow to consume messages, the server drops the oldest undelivered TrackUpdates (they'll be superseded by newer ones anyway). Alerts are never dropped.

---

## 4. Webhook System

### 4.1 Configuration

Webhooks are configured via the API or admin UI:

```json
{
  "url": "https://my-siem.example.com/ingest",
  "secret": "whsec_...",
  "events": ["alert.triggered", "alert.resolved", "entity.merged"],
  "active": true,
  "headers": {
    "X-Custom-Header": "value"
  }
}
```

### 4.2 Delivery

- HTTP POST with JSON body.
- `X-Signature-256` header: HMAC-SHA256 of the body using the webhook secret, for verification.
- Retry on failure: 3 attempts with exponential backoff (10s, 60s, 300s).
- After 3 failures, webhook marked as failing. After 10 consecutive failures, auto-disabled with admin notification.
- Delivery log stored for 7 days (success/failure, response code, latency).

### 4.3 Payload

```json
{
  "id": "evt_...",
  "type": "alert.triggered",
  "timestamp": "2026-03-18T14:30:00Z",
  "data": {
    "alertId": "...",
    "type": "GeofenceBreach",
    "priority": "High",
    "entity": { "id": "...", "name": "MV Pacific Star", "type": "Vessel" },
    "geofence": { "id": "...", "name": "UK EEZ" }
  }
}
```

---

## 5. Custom Source Plugin Interface

### 5.1 Plugin Architecture

Custom sources are implemented as .NET class libraries that implement `ISourceConnector` (defined in Spec 01). They're loaded at startup from a configured plugin directory.

**Directory structure:**

```
/plugins/
  my-custom-source/
    MyCustomSource.dll
    appsettings.plugin.json
```

**Registration:** On startup, the host scans `/plugins/`, loads assemblies, discovers `ISourceConnector` implementations via reflection, and registers them in the DI container alongside built-in connectors.

```csharp
// Program.cs
builder.Services.AddSourceConnectors();        // Built-in connectors
builder.Services.AddPluginConnectors("/plugins");  // External plugins
```

### 5.2 Plugin SDK

A NuGet package `SentinelMap.SourceSdk` containing:

- `ISourceConnector` interface
- `RawObservation` DTO
- `SourceType` and `ObservationType` enums
- Helper classes for HTTP polling, WebSocket streaming, and retry logic
- Configuration binding utilities

This allows contributors to build connectors without pulling the entire SentinelMap codebase.

### 5.3 Example Plugin Ideas (Community)

| Source | Data | Notes |
|---|---|---|
| Satellite imagery metadata | Sentinel-2 scene footprints | Copernicus Open Access Hub API |
| Weather overlays | Wind, wave, visibility layers | Open-Meteo API |
| Earthquake / natural events | USGS earthquake feed | GeoJSON feed, trivial connector |
| Sanctions lists | OFAC, EU sanctions | Cross-reference against entity names |
| Port data | Vessel arrivals/departures | UN/LOCODE + public port APIs |

---

## 6. Data Export

### 6.1 Formats

| Format | Use Case | Endpoint |
|---|---|---|
| **GeoJSON** | Track lines, entity positions — importable into QGIS, Google Earth | `/api/tracks/export?format=geojson` |
| **KML** | Google Earth compatibility | `/api/tracks/export?format=kml` |
| **CSV** | Tabular entity/alert data for spreadsheet analysis | `/api/entities/export?format=csv`, `/api/alerts/export?format=csv` |
| **JSON** | Raw API data for programmatic consumption | All endpoints return JSON natively |

### 6.2 Bulk Export

For large exports (>10k entities or >30 days of track data), the export runs asynchronously:

```
POST /api/exports  { type: "tracks", entityIds: [...], from, to, format: "geojson" }
    → 202 { exportId, status: "processing" }

GET /api/exports/{exportId}
    → 200 { status: "complete", downloadUrl: "/api/exports/{exportId}/download" }
```

Export files are stored temporarily (24h) then auto-deleted.

---

## 7. OpenAPI Documentation

The API is fully documented via Swagger/OpenAPI 3.0, auto-generated from ASP.NET Core attributes and XML comments:

- **Swagger UI** available at `/swagger` in development.
- **OpenAPI spec** downloadable at `/swagger/v1/swagger.json`.
- Every endpoint includes: description, request/response schemas, example payloads, authentication requirements, and error responses.

The spec is committed to the repo at `docs/openapi.json` and kept in sync via CI.

---

## 8. API Client Libraries (Future)

Post-v1, auto-generate typed client libraries from the OpenAPI spec:

- **TypeScript** (for the React frontend — replaces hand-written fetch calls)
- **Python** (for data science / Jupyter notebook integration)
- **C#** (for .NET consumers / plugin development)

Generated via `openapi-generator` in CI.
