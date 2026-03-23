# M9: Advanced Correlation + Platform Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement advanced correlation (Jaro-Winkler name matching, spatio-temporal cross-source linking), transponder swap detection, correlation link alerts, GeoJSON/CSV export with classification watermark, admin user management, system status endpoint, and architecture diagrams.

**IMPORTANT:** All work must stay within `C:\Users\lukeb\source\repos\SentinelMap`.

---

## Task 1: Jaro-Winkler Name Matching + Correlation Rules

**Context:** The current CorrelationProcessor does direct ID matching only (hot-path cache by sourceType:externalId). Add fuzzy name matching for entities without a clean cache hit, using Jaro-Winkler similarity with name normalisation.

**Files:**
- Create: `src/SentinelMap.Infrastructure/Correlation/JaroWinkler.cs` (pure algorithm)
- Create: `src/SentinelMap.Infrastructure/Correlation/NameNormaliser.cs`
- Create: `src/SentinelMap.Infrastructure/Correlation/ICorrelationRule.cs`
- Create: `src/SentinelMap.Infrastructure/Correlation/DirectIdMatchRule.cs`
- Create: `src/SentinelMap.Infrastructure/Correlation/NameFuzzyMatchRule.cs`
- Create: `src/SentinelMap.Infrastructure/Correlation/SpatioTemporalRule.cs`
- Modify: `src/SentinelMap.Workers/Services/CorrelationWorker.cs` (use rules on cold path)
- Modify: `src/SentinelMap.Domain/Interfaces/IEntityRepository.cs` (add FindCandidates)
- Create: `tests/SentinelMap.Infrastructure.Tests/Correlation/` (TDD)

- [ ] **Step 1: Implement Jaro-Winkler algorithm** (pure, no dependencies)
- [ ] **Step 2: Name normaliser** — strip prefixes (MV, MT, HMS, SS), collapse whitespace, uppercase
- [ ] **Step 3: ICorrelationRule interface** — `Task<CorrelationScore?> EvaluateAsync(observation, candidate)`
- [ ] **Step 4: DirectIdMatchRule** — exact MMSI/ICAO match → confidence 0.95
- [ ] **Step 5: NameFuzzyMatchRule** — Jaro-Winkler ≥ 0.75 → confidence 0.5–0.85 scaled by score
- [ ] **Step 6: SpatioTemporalRule** — speed-scaled radius, PostGIS ST_DWithin → confidence 0.3–0.7
- [ ] **Step 7: Add FindCandidates to EntityRepository** — query entities seen in last 24h within radius
- [ ] **Step 8: Update CorrelationProcessor cold path** — on cache miss, run correlation rules against candidates, merge if confidence > 0.6
- [ ] **Step 9: Tests** — JaroWinkler accuracy, NameNormaliser, each rule
- [ ] **Step 10: Commit**

---

## Task 2: Transponder Swap + Correlation Link Alerts

**Context:** Two new alert types. Transponder swap: same MMSI from divergent positions simultaneously. Correlation link: new source linked to an existing entity (especially watchlisted).

**Files:**
- Create: `src/SentinelMap.Infrastructure/Alerting/TransponderSwapRule.cs`
- Create: `src/SentinelMap.Infrastructure/Alerting/CorrelationLinkRule.cs`
- Modify: `src/SentinelMap.Workers/Program.cs` (register rules)

- [ ] **Step 1: TransponderSwapRule** — check if entity's last position is > 50km from current position with < 5 min elapsed (impossible speed)
- [ ] **Step 2: CorrelationLinkRule** — fires when a new identifier is linked to an existing entity (from CorrelationProcessor). Check if the entity is watchlisted → Critical, otherwise Low.
- [ ] **Step 3: Register in Workers DI**
- [ ] **Step 4: Tests + commit**

---

## Task 3: Export with Classification Watermark (ADR-005)

**Context:** Export entity tracks and alerts as CSV or GeoJSON, with the classification level watermarked into the export file. The classification marking is inseparable from the exported data.

**Files:**
- Create: `src/SentinelMap.Api/Endpoints/ExportEndpoints.cs`
- Create: `src/SentinelMap.Infrastructure/Services/ExportService.cs`
- Create: `client/src/components/map/ExportButton.tsx`

- [ ] **Step 1: ExportService** — generates CSV or GeoJSON from entity/observation data. Prepends classification header: `"CLASSIFICATION: OFFICIAL"` (or user's level). GeoJSON includes classification in properties.
- [ ] **Step 2: ExportEndpoints** — `POST /api/v1/export` accepts format (csv/geojson), entity IDs, date range. Returns the file with correct content-type and `Content-Disposition` header.
- [ ] **Step 3: ExportButton** — button in entity detail panel and toolbar. Downloads the file.
- [ ] **Step 4: Commit**

---

## Task 4: Admin User Management + System Status

**Context:** Admin endpoints for user CRUD and a system status endpoint showing source health, track counts, alert stats.

**Files:**
- Create: `src/SentinelMap.Api/Endpoints/AdminEndpoints.cs`
- Create: `src/SentinelMap.Api/Endpoints/SystemEndpoints.cs`

- [ ] **Step 1: AdminEndpoints**
```
GET /api/v1/admin/users — list all users (AdminAccess)
PATCH /api/v1/admin/users/{id}/role — change role (AdminAccess)
```

- [ ] **Step 2: SystemEndpoints**
```
GET /api/v1/system/status — source health, track counts, alert stats (ViewerAccess)
```
Returns:
```json
{
  "sources": {
    "ais": { "status": "healthy", "entityCount": 1849 },
    "adsb": { "status": "healthy", "entityCount": 107 }
  },
  "alerts": { "active": 7, "total": 42 },
  "geofences": { "active": 3 },
  "uptime": "2h 15m"
}
```

- [ ] **Step 3: Wire StatusBar to system status endpoint** — poll every 30s
- [ ] **Step 4: Commit**

---

## Task 5: Architecture Diagrams

**Context:** Create Mermaid architecture diagrams embedded in the README for visual documentation.

**Files:**
- Modify: `README.md` (replace ASCII diagrams with Mermaid)
- Create: `docs/architecture/` directory with diagram source files

- [ ] **Step 1: System architecture diagram** (Mermaid C4 or flowchart)
- [ ] **Step 2: Data pipeline diagram** (Mermaid sequence/flow)
- [ ] **Step 3: Security architecture diagram** (trust boundaries)
- [ ] **Step 4: Commit**

---

## Task 6: Docker E2E + Final Verification

- [ ] Build, test, Docker rebuild
- [ ] Verify correlation rules fire for fuzzy name matches
- [ ] Verify transponder swap detection
- [ ] Verify export downloads with classification watermark
- [ ] Verify admin user management
- [ ] Verify system status endpoint
- [ ] Fix issues, final commit
