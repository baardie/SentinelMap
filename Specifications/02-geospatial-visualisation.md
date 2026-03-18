# Spec 02 — Geospatial Visualisation & Common Operating Picture

**Parent:** [PRD.md](../PRD.md)
**Status:** Draft

---

## 1. Overview

The map view is the primary interface of SentinelMap. It renders a Common Operating Picture (COP) — a real-time fused view of all tracked entities across sources, with layered overlays, temporal playback, and interactive entity inspection. This spec covers the frontend map implementation and the APIs that feed it.

---

## 2. Map Engine

**Library:** MapLibre GL JS (open-source fork of Mapbox GL JS, no API key required for self-hosted tiles).

**Tile Source (default):** OpenStreetMap raster tiles via a self-hosted tile proxy, or Protomaps for vector tiles. No commercial tile provider dependency.

**Why MapLibre over Leaflet:** WebGL-based rendering handles tens of thousands of simultaneous track points without DOM thrashing. Defence-grade COPs need smooth rendering at density; Leaflet's DOM-per-marker model breaks above ~5k points.

---

## 3. Layer Architecture

The map uses a composable layer system. Each layer can be toggled independently via the Layer Control panel.

| Layer | Source | Symbol | Default |
|---|---|---|---|
| **Maritime Tracks** | AIS ingestion | Vessel icon (oriented by heading), colour-coded by type | ON |
| **Aviation Tracks** | ADS-B ingestion | Aircraft icon (oriented by heading), altitude-coded colour ramp | ON |
| **News Pins** | News ingestion (geo-tagged) | Newspaper icon, clustered at low zoom | OFF |
| **Social Pins** | Social media ingestion (geo-tagged) | Speech bubble icon, clustered at low zoom | OFF |
| **Geofences** | User-defined (see Spec 04) | Polygon/circle outlines, semi-transparent fill | ON |
| **Alert Markers** | Active alerts | Pulsing red ring on affected entity | ON |
| **Track History** | Selected entity trail | Dashed line with time-stamped waypoints | ON (selection only) |
| **Heatmap** | All positional data | Density heatmap overlay | OFF |

---

## 4. Track Rendering

### 4.1 Real-Time Updates

Tracks update via SignalR (WebSocket transport, fallback to SSE/long-polling):

```
Backend SignalR Hub
    │
    ├── "TrackUpdate" event
    │   { entityId, lat, lon, heading, speed, altitude?, source, timestamp }
    │
    └── "TrackRemove" event  (stale track, no update in configurable window)
        { entityId, reason }
```

**Frontend flow:**
1. SignalR client receives `TrackUpdate`.
2. Update the GeoJSON feature in the MapLibre source (keyed by `entityId`).
3. MapLibre re-renders only the changed feature — no full layer redraw.
4. Smooth interpolation between positions using `requestAnimationFrame` to avoid jumpy movement.

### 4.2 Track Symbology

Vessel and aircraft icons use SVG sprites loaded into MapLibre's sprite sheet at init.

| Entity Type | Icon | Colour Logic | Rotation |
|---|---|---|---|
| Cargo vessel | Ship silhouette | Green | COG (course over ground) |
| Tanker | Ship silhouette (wider) | Blue | COG |
| Military vessel | Ship silhouette (angular) | Red | COG |
| Fishing vessel | Ship silhouette (small) | Orange | COG |
| Unknown vessel | Circle | Grey | None |
| Commercial aircraft | Plane silhouette | White → Yellow (by altitude) | Heading |
| Military aircraft | Plane silhouette (angular) | Red | Heading |
| Helicopter | Helicopter silhouette | Cyan | Heading |

Symbology follows MIL-STD-2525D conventions loosely — not full compliance, but recognisable to a defence audience.

### 4.3 Clustering

At zoom levels < 8, dense areas cluster into count-aggregated circles:
- Cluster circle size scales with count.
- Click to zoom in and expand.
- Clusters are per-layer (maritime and aviation cluster independently).

### 4.4 Track Staleness

Tracks that haven't received an update within a configurable window (default: 10 minutes for AIS, 2 minutes for ADS-B) are:
1. Visually faded (50% opacity).
2. After 2× the staleness window, removed from the map (but retained in the database).
3. A `TrackRemove` event is emitted for any active alerts.

---

## 5. Entity Interaction

### 5.1 Click → Entity Detail Panel

Clicking a track opens a slide-out panel on the right:

```
┌──────────────────────────────────────┐
│  ENTITY: EVER GIVEN                  │
│  Type: Container Ship  |  Flag: PA   │
│  MMSI: 353136000  |  IMO: 9811000    │
├──────────────────────────────────────┤
│  CURRENT STATUS                      │
│  Position: 31.38°N, 32.37°E         │
│  Speed: 12.4 kts  |  Heading: 172°  │
│  Last Update: 14 seconds ago         │
├──────────────────────────────────────┤
│  LINKED OBSERVATIONS                 │
│  ▸ AIS Track (live)                  │
│  ▸ 3 news articles                   │
│  ▸ 2 Reddit posts                    │
│  ▸ 1 Companies House filing (owner)  │
├──────────────────────────────────────┤
│  TRACK HISTORY          [Playback ▶] │
│  ┄┄┄┄┄●━━━━━●━━━━━●━━━━━●━━━━━●    │
│  -24h  -18h  -12h   -6h   now       │
├──────────────────────────────────────┤
│  ALERTS                              │
│  ⚠ Entered geofence "Suez North"    │
│    Triggered: 2h ago                 │
├──────────────────────────────────────┤
│  ACTIONS                             │
│  [Add to Watchlist] [Create Alert]   │
│  [Export Track] [View Raw Data]      │
└──────────────────────────────────────┘
```

### 5.2 Hover → Tooltip

Hovering shows a minimal tooltip: entity name, type, speed, last update time. No panel opening.

### 5.3 Multi-Select

Shift+click or lasso select to select multiple entities. Bulk actions: add all to watchlist, export tracks, compare timelines.

---

## 6. Timeline Playback

A timeline scrubber at the bottom of the map allows historical playback:

```
|◀  ◀◀  ▶  ▶▶  ▶|     1x  2x  5x  10x
[━━━━━━━━━━━━━━━━━━━━━━━━━━━━━●━━━━━━━━]
2026-03-17 00:00              NOW
```

**Behaviour:**
- Dragging the scrubber queries the Track History API for all entity positions at that timestamp.
- Playback animates forward at 1×/2×/5×/10× speed, advancing the scrubber and updating the map.
- Real-time mode (default) — scrubber pinned to "NOW", live updates flowing.
- Historical mode — scrubber detached, only showing data from the selected time window.

**API endpoint:**

```
GET /api/tracks/snapshot?timestamp=2026-03-17T14:30:00Z&bbox=49,-11,61,2
```

Returns all entity positions closest to the requested timestamp within the bounding box. Uses PostGIS `ST_Within` and a time-windowed query.

---

## 7. Map Controls & UI Chrome

### 7.1 Top Bar

```
[🔍 Search entities...] [Layer Control ☰] [Alerts 🔔 3]  [👤 User]
```

- **Search:** Typeahead across entity names, MMSI, ICAO hex, callsign. Selecting an entity centres the map and opens the detail panel.
- **Layer Control:** Toggle individual layers on/off. Collapsible panel.
- **Alert Badge:** Count of unacknowledged alerts. Click to open alert feed panel.

### 7.2 Bottom Bar

```
[Timeline Scrubber                                    ] [Zoom: 6] [Entities: 1,247]
```

- **Zoom level** and **entity count** (visible on map) displayed for situational awareness.

### 7.3 Minimap

Optional inset minimap (top-left) showing the current viewport extent on a world view. Common in defence COP tools.

---

## 8. Performance Targets

| Metric | Target |
|---|---|
| Initial map load (cold) | < 2 seconds |
| Track update render latency | < 100ms from SignalR receipt to pixel update |
| Simultaneous rendered tracks | 10,000+ without frame drop below 30fps |
| Timeline playback query | < 500ms for 1-hour window, 10k entity bbox |
| Cluster re-calculation | < 50ms on zoom change |

### Performance Strategies

- **Viewport culling:** Only request and render entities within the current map bounding box + 20% buffer.
- **Level-of-detail:** At low zoom, show clusters. At medium zoom, show simplified icons. At high zoom, show full symbology with labels.
- **Track decimation:** Historical track lines simplified with Douglas-Peucker algorithm server-side, detail level keyed to requested zoom.
- **GeoJSON source diffing:** Only update changed features in the MapLibre source, never replace the entire dataset.

---

## 9. Responsive Behaviour

| Viewport | Layout |
|---|---|
| **Desktop (>1200px)** | Full map with side panel (entity detail), bottom timeline bar |
| **Tablet (768–1200px)** | Full map, entity detail as bottom sheet (half height) |
| **Mobile (<768px)** | Full map, entity detail as full-screen overlay, simplified layer control |

The primary design target is desktop — this is an analyst workstation tool. Tablet/mobile are functional but not optimised.

---

## 10. Accessibility

- All interactive elements keyboard-navigable (Tab through layers, Enter to toggle).
- Screen reader announcements for alert events.
- Colour-blind safe palette option (deuteranopia-friendly symbology swap).
- High-contrast mode for low-light environments (dark basemap, bright track colours).

---

## 11. API Endpoints (Map-Specific)

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/tracks/live?bbox={bbox}` | All current tracks within bounding box |
| `GET` | `/api/tracks/snapshot?timestamp={ts}&bbox={bbox}` | Historical positions at timestamp |
| `GET` | `/api/tracks/{entityId}/history?from={ts}&to={ts}` | Track line for a single entity |
| `GET` | `/api/tracks/{entityId}/history?from={ts}&to={ts}&zoom={z}` | Decimated track line for zoom level |
| `GET` | `/api/layers/geofences` | All geofence polygons for overlay |
| `SignalR` | `/hubs/tracks` | Real-time track update stream |
