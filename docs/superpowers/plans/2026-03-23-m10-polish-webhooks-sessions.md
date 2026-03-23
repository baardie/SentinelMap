# M10: Polish, Webhooks, Sessions, Correlation Review, Route Deviation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement task-by-task.

**Goal:** UI polish (toast notifications, loading states, error handling), webhook notifications (HMAC-SHA256), admin sessions UI, correlation review queue for analyst approval, and route deviation alerts.

**IMPORTANT:** All work must stay within `C:\Users\lukeb\source\repos\SentinelMap`.

---

## Task 1: Polish Pass — Toast Notifications + Error Handling + Loading States

**Files:**
- Create: `client/src/components/ui/toast.tsx` (lightweight toast component)
- Create: `client/src/contexts/ToastContext.tsx` (toast state management)
- Modify: `client/src/App.tsx` (wrap in ToastProvider)
- Modify: `client/src/components/map/MapContainer.tsx` (replace silent catches with toasts)
- Modify: `client/src/components/map/EntityDetailPanel.tsx` (loading states for actions)
- Modify: `client/src/components/layout/TopBar.tsx` (clean up placeholder text)

- [ ] Toast component: slide-in from top-right, auto-dismiss after 3s, severity variants (success/error/info)
- [ ] ToastContext: `useToast()` hook returns `{ showToast(message, severity) }`
- [ ] Wire all silent catch blocks to show error toasts
- [ ] Add success toasts for geofence creation, structure placement, watchlist add
- [ ] Loading spinner states for API calls
- [ ] Commit

---

## Task 2: Webhook Notifications

**Files:**
- Create: `src/SentinelMap.Domain/Entities/WebhookEndpoint.cs`
- Create: `src/SentinelMap.Domain/Entities/WebhookDelivery.cs`
- Create: `src/SentinelMap.Infrastructure/Data/Configurations/WebhookEndpointConfiguration.cs`
- Create: `src/SentinelMap.Infrastructure/Data/Configurations/WebhookDeliveryConfiguration.cs`
- Create: `src/SentinelMap.Infrastructure/Services/WebhookService.cs`
- Create: `src/SentinelMap.Api/Endpoints/WebhookEndpoints.cs`
- Modify: DbContexts (add DbSets), Workers Program.cs, API Program.cs
- Generate migration

### WebhookEndpoint entity (from spec):
```
id UUID, url TEXT, secret TEXT (plaintext for HMAC), event_filter JSONB,
is_active BOOLEAN, consecutive_failures INT, created_by UUID
```

### WebhookDelivery entity:
```
id BIGINT, endpoint_id UUID FK, alert_id UUID FK, status TEXT,
response_code INT, latency_ms INT, attempt_count INT, last_attempt_at TIMESTAMPTZ
```

### WebhookService:
- Subscribes to `alerts:triggered` in Workers
- For each alert, find matching webhook endpoints (filter by event_filter)
- POST to webhook URL with HMAC-SHA256 signature in `X-Signature-256` header
- Retry: 10s, 60s, 300s delays
- Auto-disable after 10 consecutive failures
- Track deliveries in webhook_deliveries table

### API Endpoints:
```
GET /api/v1/webhooks (AdminAccess)
POST /api/v1/webhooks (AdminAccess)
PUT /api/v1/webhooks/{id} (AdminAccess)
DELETE /api/v1/webhooks/{id} (AdminAccess)
POST /api/v1/webhooks/{id}/test (AdminAccess) — send test payload
```

- [ ] Create entities + configs + migration
- [ ] Implement WebhookService with HMAC signing + retry
- [ ] Create API endpoints
- [ ] Commit

---

## Task 3: Sessions UI

**Files:**
- Create: `src/SentinelMap.Api/Endpoints/SessionEndpoints.cs`
- Create: `client/src/pages/SessionsPage.tsx`
- Modify: `client/src/App.tsx` (add sessions view toggle)

### API Endpoints:
```
GET /api/v1/sessions (ViewerAccess) — own sessions
GET /api/v1/admin/sessions (AdminAccess) — all users' sessions
DELETE /api/v1/sessions/{familyId} (ViewerAccess) — revoke own session
DELETE /api/v1/admin/sessions/{familyId} (AdminAccess) — force revoke any
```

Query RefreshTokens grouped by FamilyId, showing:
- Device info (parsed user-agent)
- Last used timestamp
- Created timestamp
- Active/Revoked status

### Frontend:
- Sessions panel accessible from TopBar user menu or a dedicated view
- Table showing active sessions with device info, last used, and "REVOKE" button
- Admin view shows all users' sessions
- Defence-themed table styling

- [ ] Create API endpoints
- [ ] Create SessionsPage
- [ ] Wire into layout
- [ ] Commit

---

## Task 4: Correlation Review Queue

**Files:**
- Create: `src/SentinelMap.Domain/Entities/CorrelationReview.cs`
- Create: `src/SentinelMap.Infrastructure/Data/Configurations/CorrelationReviewConfiguration.cs`
- Create: `src/SentinelMap.Api/Endpoints/CorrelationEndpoints.cs`
- Create: `client/src/components/correlation/CorrelationReviewPanel.tsx`
- Modify: `src/SentinelMap.Workers/Services/CorrelationWorker.cs` (create review for low-confidence matches)
- Modify: DbContexts, Program.cs files

### CorrelationReview entity:
```
id UUID, source_entity_id UUID, target_entity_id UUID,
confidence DOUBLE, rule_scores JSONB, status TEXT (Pending/Approved/Rejected),
reviewed_by UUID, reviewed_at TIMESTAMPTZ, created_at TIMESTAMPTZ
```

### Logic:
- CorrelationProcessor: when best confidence is 0.3–0.6, create a CorrelationReview instead of auto-merging
- Auto-merge above 0.6 (existing behaviour)
- Below 0.3 → create new entity (existing behaviour)

### API Endpoints:
```
GET /api/v1/correlations/pending (AnalystAccess)
POST /api/v1/correlations/{id}/approve (AnalystAccess) — merge entities
POST /api/v1/correlations/{id}/reject (AnalystAccess) — keep separate
```

### Frontend:
- Review panel showing: source entity, target entity, confidence score, rule breakdown
- Map highlights both entities when reviewing
- "APPROVE" (merge) / "REJECT" (keep separate) buttons
- Badge on TopBar showing pending review count

- [ ] Create entity + config + migration
- [ ] Update CorrelationProcessor
- [ ] Create API endpoints
- [ ] Create frontend review panel
- [ ] Commit

---

## Task 5: Route Deviation Alerts

**Files:**
- Create: `src/SentinelMap.Infrastructure/Alerting/RouteDeviationRule.cs`
- Create: `src/SentinelMap.Infrastructure/Services/RouteBaselineService.cs`

### How it works:
- Build a historical baseline per entity: track the typical route corridor as a buffered linestring
- When a vessel deviates from its established corridor by more than a threshold (e.g. 5km), fire an alert
- Baseline needs at least 10 position updates to be considered valid

### Implementation:
- Store route baseline in Redis: `route:baseline:{entityId}` as a list of positions
- On each entity update, append position to baseline (cap at 500 points)
- Calculate a "corridor" — average heading over last 10 positions
- If current heading deviates by > 45° from the rolling average AND entity has > 20 baseline points, fire alert
- Debounce: one alert per entity per hour

### Simpler approach for v1:
Track rolling average heading. If current heading deviates by > 60° from the average of the last 20 headings, and the entity has been tracked for > 5 minutes, fire a route deviation alert. No spatial corridor needed — just heading analysis.

- [ ] Implement RouteDeviationRule
- [ ] Register in Workers DI
- [ ] Add RouteDeviation to AlertType enum
- [ ] Commit

---

## Task 6: Docker E2E
- [ ] Full build + test + Docker rebuild
- [ ] Verify toasts appear on geofence create/error
- [ ] Verify webhook test delivery works
- [ ] Verify sessions page shows active sessions
- [ ] Verify correlation review queue for low-confidence matches
- [ ] Final commit
