# M8: Map Intelligence Layer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add static and dynamic intelligence layers to the map — AIS base stations, aids to navigation, safety broadcast messages, airports, military installations, and user-placed custom structures (POIs). Turn the COP into a mini command center.

**IMPORTANT:** All work must stay within `C:\Users\lukeb\source\repos\SentinelMap`.

---

## Task 1: AIS Base Stations + Aids to Navigation (Backend)

**Context:** AISStream provides `BaseStationReport` (message type 4) and `AidsToNavigationReport` (message type 21). Parse these to show AIS infrastructure on the map. Store as a separate lightweight data structure (not as tracked entities — they're fixed infrastructure).

**Files:**
- Modify: `src/SentinelMap.Infrastructure/Connectors/AisStreamConnector.cs` (add message types to subscription filter, parse new types)
- Create: `src/SentinelMap.Domain/Entities/MapFeature.cs` (generic map feature entity for infrastructure)
- Create: `src/SentinelMap.Infrastructure/Data/Configurations/MapFeatureConfiguration.cs`
- Modify: `src/SentinelMap.Infrastructure/Data/SystemDbContext.cs` (add DbSet)
- Modify: `src/SentinelMap.Infrastructure/Data/SentinelMapDbContext.cs` (add DbSet)
- Create: `src/SentinelMap.Api/Endpoints/MapFeatureEndpoints.cs` (GET endpoint)

- [ ] **Step 1: Create MapFeature entity**

```csharp
public class MapFeature
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FeatureType { get; set; } = string.Empty;  // "AisBaseStation", "AidToNavigation", "Airport", "MilitaryBase", "CustomStructure"
    public string Name { get; set; } = string.Empty;
    public Point Position { get; set; } = null!;
    public string? Icon { get; set; }           // icon identifier for frontend
    public string? Color { get; set; }
    public string? Details { get; set; }        // JSONB metadata
    public string Source { get; set; } = string.Empty;  // "ais", "static", "user"
    public bool IsActive { get; set; } = true;
    public Guid? CreatedBy { get; set; }        // null for system-created
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

- [ ] **Step 2: Add EF configuration + migration**

Table: `map_features`. GIST index on position.

- [ ] **Step 3: Update AisStreamConnector**

Add to FilterMessageTypes: `"BaseStationReport"`, `"AidsToNavigationReport"`, `"SafetyBroadcastMessage"`.

Parse `BaseStationReport` → create/update `MapFeature` with type "AisBaseStation", position from message.

Parse `AidsToNavigationReport` → create/update `MapFeature` with type "AidToNavigation", name from message.

Parse `SafetyBroadcastMessage` → don't store as MapFeature. Instead, create an alert (AlertType could be a new "SafetyBroadcast" or use existing info channel). Store as a special alert with the safety text.

For base stations and aids: since these are fixed infrastructure, upsert by MMSI to avoid duplicates. Use the observation pipeline for safety messages but a separate path for infrastructure features.

Actually, simpler approach: don't route these through the observation pipeline. Parse them in the connector and store directly. The connector already has access to the parsed message — add a callback or secondary channel for infrastructure features.

**Simplest approach:** Add a static `Dictionary<string, MapFeature>` in the connector that deduplicates by MMSI. Periodically flush new features to the database via a separate service. Or even simpler: expose a REST endpoint that the frontend calls to get infrastructure, and populate on first encounter.

**Pragmatic approach for M8:** Parse base stations and aids to navigation in the AisStreamConnector. Instead of going through the observation pipeline, publish to a Redis channel `infrastructure:ais`. A new lightweight service or the API itself subscribes and upserts to the `map_features` table.

Actually, **simplest of all**: Parse them in the connector, yield them as Observations with a special SourceType like "AIS_INFRA", and let the ingestion pipeline persist them. Then the API reads from a dedicated query. But this muddies the observation table.

**Final decision:** Parse in AisStreamConnector. For BaseStationReport and AidsToNavigationReport, don't yield as Observations. Instead, store them in-memory in the connector (deduplicated by MMSI). Expose a static method or make the connector injectable so the API can read the infrastructure data. OR — just write them to the map_features table directly from the connector using a scoped DbContext.

**Best approach for clean architecture:** Parse in AisStreamConnector → publish to Redis channel `map-features:update` → a small MapFeatureService in the API subscribes and upserts to DB → frontend fetches via REST.

- [ ] **Step 4: Create MapFeature API endpoint**

```csharp
GET /api/v1/map-features?type=AisBaseStation,AidToNavigation
```

Returns all active map features, optionally filtered by type.

- [ ] **Step 5: Add SafetyBroadcastMessage handling**

Parse safety messages → publish to Redis `alerts:safety` → AlertHubService broadcasts as a special alert type. Add `SafetyBroadcast` to AlertType enum. Display in alert feed with blue severity indicator.

- [ ] **Step 6: Build, test, commit**

---

## Task 2: Static Data Layers (Airports + Military Bases)

**Context:** Pre-load UK airports and military installations as static map features. These are seeded on startup from embedded data (not from external APIs).

**Files:**
- Create: `src/SentinelMap.Api/Services/StaticFeatureSeeder.cs`
- Modify: `src/SentinelMap.Api/Program.cs` (call seeder)

- [ ] **Step 1: Create StaticFeatureSeeder**

Seed known UK airports and military bases. Data is hardcoded (small, well-known list):

**Airports** (type = "Airport", source = "static", icon = "airport"):
- Liverpool John Lennon (EGGP): -2.8497, 53.3336
- Manchester (EGCC): -2.2750, 53.3537
- Hawarden (EGNR): -2.9778, 53.1781
- Blackpool (EGNH): -3.0286, 53.7717
- Leeds Bradford (EGNM): -1.6606, 53.8659
- Isle of Man (EGNS): -4.6239, 54.0833
- Belfast City (EGAC): -5.8725, 54.6181
- Belfast International (EGAA): -6.2158, 54.6575
- Dublin (EIDW): -6.2701, 53.4213
- London Heathrow (EGLL): -0.4614, 51.4700
- London Gatwick (EGKK): -0.1903, 51.1481
- Birmingham (EGBB): -1.7480, 52.4539
- Edinburgh (EGPH): -3.3725, 55.9500
- Glasgow (EGPF): -4.4331, 55.8719
- Cardiff (EGFF): -3.3433, 51.3967
- Bristol (EGGD): -2.7191, 51.3827
- East Midlands (EGNX): -1.3283, 52.8311
- Newcastle (EGNT): -1.6917, 55.0372
- Aberdeen (EGPD): -2.1978, 57.2019
- Inverness (EGPE): -4.0475, 57.5425

**Military installations** (type = "MilitaryBase", source = "static", icon = "military"):
- RAF Woodvale: -3.0556, 53.5814
- RAF Valley (Anglesey): -4.5353, 53.2481
- HMNB Clyde (Faslane): -4.8186, 56.0667
- Cammell Laird Shipyard: -3.0167, 53.3750
- BAE Systems Barrow: -3.2264, 54.1244
- RAF Lossiemouth: -3.3439, 57.7053
- RAF Coningsby: -0.1664, 53.0931
- RNAS Culdrose: -5.2558, 50.0861
- HMNB Portsmouth: -1.1081, 50.7989
- HMNB Devonport: -4.1872, 50.3800
- RAF Brize Norton: -1.5836, 51.7500
- RAF Lakenheath: 0.5608, 52.4094
- RAF Mildenhall: 0.4864, 52.3611
- Aldermaston AWE: -1.1667, 51.3667
- MOD Boscombe Down: -1.7481, 51.1522

Only seed if no features with source="static" exist (idempotent).

- [ ] **Step 2: Build, test, commit**

---

## Task 3: Custom Map Structures (User-Placed POIs)

**Context:** Allow users to place custom structures/POIs on the map — like a mini command center setup. Click to place, configure name, type, colour, icon. Persists via the API.

**Files:**
- Modify: `src/SentinelMap.Api/Endpoints/MapFeatureEndpoints.cs` (add POST/PUT/DELETE)
- Create: `client/src/components/map/MapIntelligenceLayer.tsx` (renders all map features)
- Create: `client/src/components/map/StructurePlacer.tsx` (click-to-place interaction)
- Create: `client/src/components/map/StructureConfigPanel.tsx` (configure placed structure)
- Create: `client/src/components/map/icons/` (airport, military, base-station, buoy, custom icons)

- [ ] **Step 1: API CRUD for map features**

```
POST /api/v1/map-features — create custom structure (AnalystAccess)
PUT /api/v1/map-features/{id} — update (AnalystAccess)
DELETE /api/v1/map-features/{id} — delete user-created only (AnalystAccess)
```

Request DTO:
```csharp
record CreateMapFeatureRequest(
    string Name,
    string FeatureType,  // "CustomStructure", "Checkpoint", "Observation Post", etc.
    double Longitude,
    double Latitude,
    string? Icon,
    string? Color,
    string? Details);  // JSON metadata
```

- [ ] **Step 2: Create map feature icons**

SVG data URL icons (SDF-compatible like vessel/aircraft):
- `airport.ts` — simplified runway/terminal symbol
- `military.ts` — star/shield symbol
- `base-station.ts` — antenna/tower symbol
- `buoy.ts` — diamond/circle symbol for aids to navigation
- `custom.ts` — pin/marker symbol for user structures

- [ ] **Step 3: Create MapIntelligenceLayer**

Renders all map features grouped by type. Each type gets its own symbol layer with distinct icon and colour:

| Type | Icon | Default Colour |
|------|------|---------------|
| AisBaseStation | antenna | `#8b5cf6` (purple) |
| AidToNavigation | diamond | `#06b6d4` (cyan) |
| Airport | runway | `#f97316` (orange) |
| MilitaryBase | star | `#ef4444` (red) |
| CustomStructure | pin | user-selected |

Click on any feature → detail popup or panel.

- [ ] **Step 4: Create StructurePlacer**

Click-to-place mode (activated by a toolbar button "ADD STRUCTURE"):
- Map cursor changes to crosshair
- Click on map → place a temporary marker
- Opens StructureConfigPanel

- [ ] **Step 5: Create StructureConfigPanel**

Right-side panel with:
- Name input
- Type dropdown: "Command Post", "Observation Point", "Checkpoint", "Relay Station", "Custom"
- Colour picker (same 6 presets as geofences)
- Notes/details text area
- "PLACE STRUCTURE" / "CANCEL" buttons

- [ ] **Step 6: Wire into MapContainer and App.tsx**

- Fetch map features on mount
- Add "ADD STRUCTURE" button to toolbar
- Render MapIntelligenceLayer
- Layer visibility toggles in toolbar

- [ ] **Step 7: Build, test, commit**

---

## Task 4: Safety Broadcast Display

**Context:** Parse AIS SafetyBroadcastMessage and display as alerts in the feed. These are maritime safety warnings (weather, navigation hazards, etc).

**Files:**
- Modify: `src/SentinelMap.SharedKernel/Enums/AlertType.cs` (add SafetyBroadcast)
- Modify: AIS connector or create a separate handler

- [ ] **Step 1: Add SafetyBroadcast to AlertType enum**

- [ ] **Step 2: Parse safety messages and create alerts**

In the AIS connector, when a SafetyBroadcastMessage is received, publish a special observation or directly create an alert with the safety text as the summary.

- [ ] **Step 3: Display in alert feed with distinct styling**

Safety broadcasts shown with a blue/cyan indicator, distinct from security alerts.

- [ ] **Step 4: Build, test, commit**

---

## Task 5: Frontend Layer Controls + Toggle Panel

**Context:** With multiple layers (vessels, aircraft, geofences, base stations, airports, military, custom structures, trails), users need a layer visibility control panel.

**Files:**
- Create: `client/src/components/map/LayerControlPanel.tsx`
- Modify: `client/src/components/map/MapContainer.tsx`

- [ ] **Step 1: Create LayerControlPanel**

Small expandable panel (top-left, below existing toolbar) with toggle switches for each layer:
- Vessels (on by default)
- Aircraft (on by default)
- Trails (off by default)
- Geofences (on by default)
- Base Stations (on by default)
- Aids to Navigation (off by default — can be noisy)
- Airports (on by default)
- Military (on by default)
- Custom Structures (on by default)

Defence-themed toggle switches or checkboxes.

- [ ] **Step 2: Wire visibility to each layer component**

- [ ] **Step 3: Build, test, commit**

---

## Task 6: Docker E2E Verification

- [ ] Build, test, Docker rebuild
- [ ] Verify AIS base stations appear on map
- [ ] Verify airports and military bases render
- [ ] Verify custom structure placement works
- [ ] Verify safety broadcasts appear in alert feed
- [ ] Verify layer toggles work
- [ ] Fix issues, final commit
