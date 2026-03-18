# Spec 04 — Alerting & Monitoring

**Parent:** [PRD.md](../PRD.md)
**Status:** Draft

---

## 1. Overview

The alerting system monitors the entity stream and fires notifications when predefined conditions are met. It supports geofencing, watchlist matching, anomaly detection (rule-based), and status change triggers. Alerts are surfaced in the UI, pushed via SignalR, and optionally dispatched to external channels (email, webhook).

---

## 2. Alert Types

### 2.1 Geofence Breach

**Trigger:** An entity's position enters or exits a user-defined geographic boundary.

**Geofence definition:**

```csharp
public class Geofence
{
    public Guid Id { get; set; }
    public string Name { get; set; }                    // "Suez Canal North", "UK EEZ"
    public GeofenceShape Shape { get; set; }            // Polygon or Circle
    public GeoJSON Geometry { get; set; }               // PostGIS-compatible geometry
    public double? RadiusMetres { get; set; }           // For circle geofences
    public GeofenceTrigger Trigger { get; set; }        // Enter, Exit, Both
    public List<EntityType>? FilterEntityTypes { get; set; }  // null = all types
    public bool Active { get; set; } = true;
}
```

**Evaluation:** On every position update, the alerting engine runs:

```sql
SELECT g.id, g.name, g.trigger
FROM geofences g
WHERE g.active = true
  AND ST_Within(
        ST_SetSRID(ST_MakePoint(@lon, @lat), 4326)::geography,
        g.geometry::geography
      )
  AND (g.filter_entity_types IS NULL OR @entityType = ANY(g.filter_entity_types));
```

Compared against the entity's previous geofence membership (stored in Redis as a SET per entity). If membership changed → fire alert.

**UI for creation:**
- Draw polygon on map (click vertices, double-click to close).
- Draw circle on map (click centre, drag radius).
- Import GeoJSON from file.
- Named presets: UK EEZ, English Channel, major shipping lanes, airspace sectors.

### 2.2 Watchlist Match

**Trigger:** A new observation matches an entity on a user-maintained watchlist.

**Watchlist entry:**

```csharp
public class WatchlistEntry
{
    public Guid Id { get; set; }
    public Guid WatchlistId { get; set; }
    public string MatchType { get; set; }      // "MMSI", "IMO", "ICAO24", "Name", "CompanyNumber"
    public string MatchValue { get; set; }     // The identifier to watch for
    public string? Notes { get; set; }         // Analyst notes on why this is watched
    public AlertPriority Priority { get; set; } // Low, Medium, High, Critical
}
```

**Evaluation:** On every ingested observation, check if any identifier matches an active watchlist entry. Uses a Redis hash for O(1) lookup:

```
HGET watchlist:mmsi "353136000"  →  { watchlistId, priority, notes }
HGET watchlist:icao24 "4CA529"  →  { watchlistId, priority, notes }
```

### 2.3 Anomaly Detection (Rule-Based)

**Trigger:** An entity exhibits behaviour that deviates from expected patterns.

| Anomaly | Rule | Threshold |
|---|---|---|
| **AIS Dark** | Vessel was transmitting AIS, then stopped for > N minutes, then resumed | Default: 60 minutes gap |
| **Speed Anomaly** | Vessel/aircraft speed exceeds type-specific maximum or drops to 0 unexpectedly | Configurable per vessel type |
| **Route Deviation** | Vessel's current heading diverges > 45° from its declared destination bearing for > 30 minutes | Requires destination field from AIS |
| **Altitude Anomaly** | Aircraft altitude drops below safe minimums for non-landing phase | < 1000ft when > 10nm from airport |
| **Transponder Swap** | An entity's callsign or MMSI changes unexpectedly | Any change in authoritative ID |

**Implementation:** Each anomaly is an `IAnomalyRule` implementation that receives entity state updates and evaluates against the rule's logic. State machine per entity, stored in Redis:

```csharp
public interface IAnomalyRule
{
    string RuleId { get; }
    AnomalyType Type { get; }
    AlertPriority DefaultPriority { get; }
    
    AnomalyResult Evaluate(EntityState currentState, EntityState? previousState);
}
```

### 2.4 Status Change

**Trigger:** An entity's correlation status changes (new entity created, entities merged, confidence changed significantly, new source linked).

These are lower-priority informational alerts to keep analysts aware of the system's evolving picture.

---

## 3. Alert Model

```csharp
public class Alert
{
    public Guid Id { get; set; }
    public AlertType Type { get; set; }            // GeofenceBreach, WatchlistMatch, Anomaly, StatusChange
    public AlertPriority Priority { get; set; }    // Low, Medium, High, Critical
    public AlertStatus Status { get; set; }        // Active, Acknowledged, Resolved, Dismissed
    public Guid EntityId { get; set; }
    public Guid? GeofenceId { get; set; }          // For geofence alerts
    public Guid? WatchlistEntryId { get; set; }    // For watchlist alerts
    public string? AnomalyRuleId { get; set; }     // For anomaly alerts
    public string Title { get; set; }              // Human-readable summary
    public string Description { get; set; }        // Detailed context
    public DateTimeOffset TriggeredAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public Guid? AcknowledgedBy { get; set; }
    public Dictionary<string, string> Context { get; set; }  // Rule-specific data
}
```

---

## 4. Alert Lifecycle

```
[Triggered]  ──Analyst clicks "Acknowledge"──▶  [Acknowledged]
    │                                                  │
    │                                                  ├──Condition clears──▶ [Resolved]
    │                                                  │
    └──Analyst clicks "Dismiss"──────────────▶  [Dismissed]
```

- **Triggered:** Initial state. Appears in the alert feed with priority colouring. Pulsing marker on map.
- **Acknowledged:** Analyst has seen it. Still visible but visual urgency reduced.
- **Resolved:** The triggering condition is no longer true (entity left geofence, AIS resumed, etc.). Auto-resolved by the system.
- **Dismissed:** Analyst determined it was a false positive. Logged for tuning.

---

## 5. Notification Channels

| Channel | Transport | Config |
|---|---|---|
| **In-App** | SignalR push to connected clients | Always enabled |
| **Email** | SMTP (configurable, e.g. Resend or self-hosted) | Per-user opt-in, configurable priority threshold |
| **Webhook** | HTTP POST to configurable URL | Per-alert-type, includes full alert payload as JSON |

**Webhook payload:**

```json
{
  "alertId": "...",
  "type": "GeofenceBreach",
  "priority": "High",
  "entity": {
    "id": "...",
    "name": "MV Suspicious Cargo",
    "type": "Vessel"
  },
  "geofence": {
    "id": "...",
    "name": "UK EEZ"
  },
  "triggeredAt": "2026-03-18T14:30:00Z",
  "context": {
    "lat": "51.1234",
    "lon": "-1.5678",
    "direction": "entered"
  }
}
```

---

## 6. Alert Feed UI

```
┌──────────────────────────────────────────────────────┐
│  ALERTS                        Filter: [All ▾] [🔍]  │
├──────────────────────────────────────────────────────┤
│  🔴 CRITICAL  14:30  Watchlist Match                 │
│     "MV Pacific Star" (MMSI: 353136000) detected     │
│     Watchlist: "Sanctioned Vessels"                   │
│     [Acknowledge] [Dismiss] [View Entity]            │
│                                                      │
│  🟠 HIGH  14:22  Geofence Breach                     │
│     "Unknown Aircraft" entered "Restricted Airspace"  │
│     Position: 51.47°N, 0.45°W at FL120               │
│     [Acknowledge] [Dismiss] [View Entity]            │
│                                                      │
│  🟡 MEDIUM  14:10  AIS Dark                          │
│     "MV Northern Star" — AIS silent for 72 minutes   │
│     Last position: 54.12°N, 1.23°W                   │
│     [Acknowledge] [Dismiss] [View Entity]            │
│                                                      │
│  🔵 LOW  13:58  Status Change                        │
│     New entity created: "RAF Typhoon 22"             │
│     Source: ADS-B (ICAO24: 43C6E1)                   │
│     [Dismiss]                                        │
└──────────────────────────────────────────────────────┘
```

**Filters:** By type, priority, status, entity, time range.
**Sound:** Optional audio alert for Critical priority (configurable, off by default).

---

## 7. Database Schema

```sql
CREATE TABLE geofences (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name                TEXT NOT NULL,
    shape               TEXT NOT NULL CHECK (shape IN ('polygon', 'circle')),
    geometry            GEOGRAPHY(Geometry, 4326) NOT NULL,
    radius_metres       DOUBLE PRECISION,
    trigger_type        TEXT NOT NULL CHECK (trigger_type IN ('enter', 'exit', 'both')),
    filter_entity_types TEXT[],
    active              BOOLEAN DEFAULT true,
    created_by          UUID REFERENCES users(id),
    created_at          TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE watchlists (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name        TEXT NOT NULL,
    description TEXT,
    created_by  UUID REFERENCES users(id),
    created_at  TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE watchlist_entries (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    watchlist_id    UUID NOT NULL REFERENCES watchlists(id) ON DELETE CASCADE,
    match_type      TEXT NOT NULL,
    match_value     TEXT NOT NULL,
    notes           TEXT,
    priority        TEXT DEFAULT 'Medium',
    UNIQUE(watchlist_id, match_type, match_value)
);

CREATE TABLE alerts (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    alert_type          TEXT NOT NULL,
    priority            TEXT NOT NULL,
    status              TEXT NOT NULL DEFAULT 'active',
    entity_id           UUID NOT NULL REFERENCES entities(id),
    geofence_id         UUID REFERENCES geofences(id),
    watchlist_entry_id  UUID REFERENCES watchlist_entries(id),
    anomaly_rule_id     TEXT,
    title               TEXT NOT NULL,
    description         TEXT NOT NULL,
    context             JSONB DEFAULT '{}',
    triggered_at        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    acknowledged_at     TIMESTAMPTZ,
    acknowledged_by     UUID REFERENCES users(id),
    resolved_at         TIMESTAMPTZ
);

CREATE INDEX idx_geofences_spatial ON geofences USING GIST (geometry);
CREATE INDEX idx_alerts_status ON alerts(status, priority);
CREATE INDEX idx_alerts_entity ON alerts(entity_id);
CREATE INDEX idx_watchlist_lookup ON watchlist_entries(match_type, match_value);
```

---

## 8. Performance

| Metric | Target |
|---|---|
| Geofence evaluation per position update | < 5ms (with spatial index) |
| Watchlist lookup per observation | < 1ms (Redis hash) |
| Alert delivery (trigger → UI) | < 200ms |
| Alert delivery (trigger → webhook) | < 2 seconds |
| Concurrent active geofences | 500+ |
| Concurrent watchlist entries | 10,000+ |
