# M7: UI Polish + Geofence Creator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Polish the COP UI with track history trails, entity search/filter, clustering at low zoom, staleness opacity fade, a live StatusBar, and — most importantly — an interactive geofence creation tool with drag-and-drop polygon/circle drawing, custom radius (n miles), custom colour, and UI-driven alert configuration.

**Tech Stack:** MapLibre GL JS, `@mapbox/maplibre-gl-draw` (or custom drawing via MapLibre events), React 19, Tailwind v4.

**IMPORTANT:** All work must stay within `C:\Users\lukeb\source\repos\SentinelMap`.

---

## Task 1: Track History Trails

**Context:** Show the last 100 positions as a fading polyline trail behind each entity. The trail fades from current opacity to transparent over time.

**Files:**
- Modify: `client/src/hooks/useTrackHub.ts` (accumulate position history per entity)
- Create: `client/src/components/map/TrackHistoryLayer.tsx`
- Modify: `client/src/types/index.ts` (add history to track data)
- Modify: `client/src/components/map/MapContainer.tsx` (add TrackHistoryLayer)

- [ ] **Step 1:** In `useTrackHub`, maintain a `Map<string, [number, number][]>` of position history per entityId. On each `TrackUpdate`, append the new position, cap at 100 entries. Return `trackHistory` alongside `tracks` and `alerts`.

- [ ] **Step 2:** Create `TrackHistoryLayer` — renders a GeoJSON LineString source per entity with a line layer. Use `line-gradient` or opacity stepping to fade older positions. Colour matches entity type (slate for vessels, sky blue for aircraft).

- [ ] **Step 3:** Add a toggle button in the TopBar or MapContainer to show/hide trail history.

- [ ] **Step 4:** Build and commit.

---

## Task 2: Staleness Opacity + Clustering

**Context:** Entities fade from opacity 1.0 to 0.3 over 5 minutes with no update. At zoom < 10, cluster nearby tracks.

**Files:**
- Modify: `client/src/components/map/MaritimeTrackLayer.tsx`
- Modify: `client/src/components/map/AviationTrackLayer.tsx`
- Modify: `client/src/hooks/useTrackHub.ts` (calculate staleness)

- [ ] **Step 1:** In `useTrackHub`, add a `staleness` property to `TrackProperties` — calculated as `(now - lastUpdated) / (5 * 60 * 1000)` clamped to [0, 1]. Update on a 10-second interval timer.

- [ ] **Step 2:** In both track layers, use `icon-opacity` expression: `['interpolate', ['linear'], ['get', 'staleness'], 0, 1.0, 1, 0.3]`.

- [ ] **Step 3:** Add clustering to both track sources: `cluster: true, clusterMaxZoom: 9, clusterRadius: 50`. Add a circle layer for clusters with a count label.

- [ ] **Step 4:** Build and commit.

---

## Task 3: Entity Search/Filter + StatusBar

**Context:** Add a search input to the TopBar that filters entities by name/MMSI/ICAO hex. Wire the StatusBar to show live connection status, track count, and source stats.

**Files:**
- Modify: `client/src/components/layout/TopBar.tsx` (add search input + filter state)
- Modify: `client/src/components/layout/StatusBar.tsx` (wire to live data)
- Modify: `client/src/App.tsx` (pass filtered tracks)

- [ ] **Step 1:** Add a search input to TopBar — `bg-slate-800 border-slate-600`, mono font, "SEARCH ENTITIES..." placeholder. Lift filter state to App.tsx or use a callback.

- [ ] **Step 2:** Filter tracks in App.tsx by display name or entityId containing the search term. Pass filtered tracks to MapContainer.

- [ ] **Step 3:** Wire StatusBar: show SignalR connection state (from useTrackHub), total track count (vessels | aircraft), and data mode (Simulated/Live).

- [ ] **Step 4:** Build and commit.

---

## Task 4: Geofence Drawing Tool

**Context:** Interactive geofence creation directly on the map. Two modes: (1) polygon draw — click to place vertices, double-click to close; (2) circle — click centre, drag to set radius in nautical miles. User sets name, colour, fence type, and optional alert configuration before saving.

**Files:**
- Install: `@mapbox/mapbox-gl-draw` (works with MapLibre) or implement custom drawing
- Create: `client/src/components/map/GeofenceDrawer.tsx` (drawing interaction)
- Create: `client/src/components/map/GeofenceConfigPanel.tsx` (name, colour, radius, alert config)
- Modify: `client/src/components/map/MapContainer.tsx` (integrate drawer)
- Modify: `client/src/components/map/GeofenceLayer.tsx` (render with custom colours)
- Modify: `client/src/types/index.ts` (extend GeofenceData with colour)

Since `@mapbox/mapbox-gl-draw` has compatibility issues with MapLibre v5, implement a lightweight custom drawing solution using MapLibre's native event handlers.

- [ ] **Step 1: Install turf.js for spatial calculations**

```bash
cd client && npm install @turf/turf
```

- [ ] **Step 2: Create GeofenceDrawer component**

Two drawing modes:
- **Polygon mode**: Click to add vertices (shown as dots). Line follows cursor. Double-click or click first vertex to close. Renders the polygon with the selected colour.
- **Circle mode**: Click to set centre. Move mouse to see radius preview (circle drawn with turf.circle). Click again to confirm. Display radius in nautical miles.

Use MapLibre event handlers (`click`, `mousemove`, `dblclick`) to capture points. Render preview using a temporary GeoJSON source.

Include a floating toolbar: "Draw Polygon" button, "Draw Circle" button, "Cancel" button.

- [ ] **Step 3: Create GeofenceConfigPanel**

Slide-in panel (from right, similar to EntityDetailPanel) with:
- Name input (required)
- Colour picker (preset swatches: amber, red, blue, green, purple + custom hex)
- Fence type: Entry / Exit / Both (radio buttons)
- Radius display (for circle mode, editable — recalculates circle)
- "CREATE GEOFENCE" button
- "CANCEL" button

On create: `POST /api/v1/geofences` with the geometry + metadata.

- [ ] **Step 4: Extend GeofenceData with colour**

Add `color` field to `GeofenceData` type and `Geofence` domain entity. Or store colour in the geofence's `details` JSONB field.

Simpler approach: Store colour as a property on the frontend only for now (the API stores the polygon, the UI remembers colours in a local map). Or add a `color` column to the geofences table.

For portfolio impact, add a `metadata` JSONB column to geofences (or use the existing structure) and store `{ "color": "#f59e0b" }`.

- [ ] **Step 5: Update GeofenceLayer to use per-geofence colours**

Instead of a flat amber colour, use `['get', 'color']` from feature properties for fill and line colours. Default to amber if not set.

- [ ] **Step 6: Load existing geofences from API**

In App.tsx, fetch `GET /api/v1/geofences` on mount (using `apiFetch`). Pass to MapContainer → GeofenceLayer.

- [ ] **Step 7: Build and commit**

---

## Task 5: EntityDetailPanel Actions

**Context:** Add action buttons to the entity detail panel: "Add to Watchlist", "Create Geofence Around", "View Track History".

**Files:**
- Modify: `client/src/components/map/EntityDetailPanel.tsx`
- Modify: `client/src/components/map/MapContainer.tsx` (action callbacks)

- [ ] **Step 1:** Add buttons: "ADD TO WATCHLIST" (calls watchlist API), "CREATE GEOFENCE" (triggers GeofenceDrawer in circle mode centred on entity), "TRACK HISTORY" (toggles trail for this entity).

- [ ] **Step 2:** Style: defence theme buttons, mono uppercase, slate background.

- [ ] **Step 3:** Build and commit.

---

## Task 6: Fix vesselType/aircraftType Pipeline

**Context:** The `vesselType` and `aircraftType` fields are always `'Unknown'` in the UI because the TrackUpdate SignalR DTO doesn't carry them. Fix the pipeline to pass these through.

**Files:**
- Modify: `src/SentinelMap.Api/Hubs/TrackHubService.cs` (add vesselType/aircraftType to broadcast)
- Modify: `src/SentinelMap.Domain/Messages/EntityUpdatedMessage.cs` (add fields)
- Modify: `src/SentinelMap.Workers/Services/CorrelationWorker.cs` (extract and pass fields)
- Modify: `client/src/hooks/useTrackHub.ts` (map fields)
- Modify: `client/src/types/index.ts` (add to TrackUpdate)

- [ ] **Step 1:** Add `VesselType` and `AircraftType` (both `string?`) to `EntityUpdatedMessage`.

- [ ] **Step 2:** In `CorrelationProcessor`, extract `vesselType`/`aircraftType` from the observation's `RawData` JSON and pass through the message.

- [ ] **Step 3:** In `TrackHubService`, include `vesselType` and `aircraftType` in the SignalR broadcast.

- [ ] **Step 4:** In `useTrackHub`, map these fields instead of hardcoding `'Unknown'`.

- [ ] **Step 5:** Build, test, commit.

---

## Task 7: Docker Compose + E2E

- [ ] Build, test, Docker rebuild
- [ ] Verify: track history trails visible
- [ ] Verify: search filters entities
- [ ] Verify: clustering at low zoom
- [ ] Verify: staleness opacity fading
- [ ] Verify: geofence drawing (polygon + circle)
- [ ] Verify: geofence saved via API and appears on map
- [ ] Verify: vessel type colours correct (not all Unknown)
- [ ] Verify: entity detail panel actions work
- [ ] Fix any issues, final commit
