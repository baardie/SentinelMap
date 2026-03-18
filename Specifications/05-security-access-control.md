# Spec 05 — Security & Access Control

**Parent:** [PRD.md](../PRD.md)
**Status:** Draft

---

## 1. Overview

Defence tech reviewers will scrutinise the security model before anything else. This spec covers authentication, role-based access control, a mock classification system, comprehensive audit logging, and infrastructure hardening. The goal is not to build a certified system — it's to demonstrate that the developer understands defence security patterns and can implement them cleanly.

---

## 2. Authentication

### 2.1 Method

ASP.NET Core Identity with JWT bearer tokens. No external identity provider dependency (self-hosted principle), but the architecture supports swapping in OIDC/SAML later.

**Flow:**

```
POST /api/auth/login  { email, password }
    → 200 { accessToken (15min), refreshToken (7d) }

POST /api/auth/refresh  { refreshToken }
    → 200 { accessToken, refreshToken }

POST /api/auth/logout  { refreshToken }
    → 204 (revoke refresh token)
```

### 2.2 Token Structure

```json
{
  "sub": "user-uuid",
  "email": "analyst@sentinelmap.local",
  "role": "Analyst",
  "clearance": "SECRET",
  "iat": 1742300000,
  "exp": 1742300900
}
```

### 2.3 Password Policy

- Minimum 12 characters.
- bcrypt hashing (cost factor 12).
- Breached password check against a local copy of the top 100k breached passwords (bundled, no API call).
- Account lockout after 5 failed attempts (15-minute lockout window).

### 2.4 Session Management

- Refresh tokens stored in PostgreSQL with device fingerprint (user-agent hash).
- Max 3 concurrent sessions per user. Oldest evicted on new login.
- Refresh token rotation — each use issues a new refresh token and invalidates the old one.
- All sessions revocable by admin.

---

## 3. Role-Based Access Control (RBAC)

### 3.1 Roles

| Role | Description | Permissions |
|---|---|---|
| **Viewer** | Read-only access to the COP. Can view the map, entities, and alerts. Cannot create geofences, watchlists, or modify anything. | `map:read`, `entity:read`, `alert:read` |
| **Analyst** | Full operational access. Can create geofences, manage watchlists, acknowledge alerts, annotate entities, trigger manual correlation actions. | All Viewer permissions + `geofence:write`, `watchlist:write`, `alert:manage`, `entity:annotate`, `correlation:review` |
| **Admin** | System administration. User management, source connector configuration, system settings. Cannot be assigned to external/guest accounts. | All Analyst permissions + `user:manage`, `source:configure`, `system:configure`, `audit:read` |

### 3.2 Permission Enforcement

Permissions enforced at the API layer via ASP.NET Core authorization policies:

```csharp
[Authorize(Policy = "RequireAnalyst")]
[HttpPost("api/geofences")]
public async Task<IActionResult> CreateGeofence(CreateGeofenceRequest request) { ... }

// Policy registration
services.AddAuthorization(options =>
{
    options.AddPolicy("RequireViewer", p => p.RequireRole("Viewer", "Analyst", "Admin"));
    options.AddPolicy("RequireAnalyst", p => p.RequireRole("Analyst", "Admin"));
    options.AddPolicy("RequireAdmin", p => p.RequireRole("Admin"));
});
```

### 3.3 User Management (Admin Only)

- Create/deactivate users (no deletion — deactivated users retained for audit trail).
- Assign/change roles.
- Reset passwords (triggers forced password change on next login).
- View all active sessions, force logout.

---

## 4. Mock Classification System

This is a **demonstration feature** — it models how classified systems handle data at different sensitivity levels without actually processing classified data.

### 4.1 Classification Levels

| Level | Colour | Description |
|---|---|---|
| **OFFICIAL** | Green | Default. All OSINT data is OFFICIAL. |
| **OFFICIAL-SENSITIVE** | Amber | Analyst annotations, correlation assessments, watchlist contents. |
| **SECRET** | Red | Mock level. Simulates restricted data — only visible to users with `clearance >= SECRET`. |

### 4.2 Clearance Assignment

Each user has a `clearance` field: `OFFICIAL`, `OFFICIAL-SENSITIVE`, or `SECRET`.

**Enforcement:** A middleware checks the data classification on every API response and strips any data above the user's clearance level:

```csharp
public class ClassificationFilterMiddleware
{
    public async Task InvokeAsync(HttpContext context)
    {
        // After endpoint execution, inspect the response body
        // Strip or redact any fields where classification > user clearance
    }
}
```

### 4.3 Classification Marking

Every entity, observation, alert, and annotation carries a `classification` field. The UI renders a banner at the top of the page:

```
┌─────────────────────────────────────────────────────────┐
│  🟢 OFFICIAL                                SentinelMap │
└─────────────────────────────────────────────────────────┘
```

Or for users with elevated clearance:

```
┌─────────────────────────────────────────────────────────┐
│  🟡 OFFICIAL-SENSITIVE                      SentinelMap │
└─────────────────────────────────────────────────────────┘
```

The classification banner shows the **highest classification of any data currently displayed** — this is standard defence UI practice.

### 4.4 Demo Data Seeder

The seeder creates mock SECRET-classified entities and observations to demonstrate the filtering. A Viewer-role user sees nothing; an Analyst with SECRET clearance sees the full picture. This is a powerful demo for interviewers.

---

## 5. Audit Logging

### 5.1 Principle

Every action is logged. Every query is logged. Every system event is logged. The audit log is append-only and immutable — no updates, no deletes. This is non-negotiable in defence systems and one of the first things a reviewer will check.

### 5.2 Audit Event Model

```csharp
public class AuditEvent
{
    public long SequenceId { get; set; }           // Auto-incrementing, gap-free
    public Guid EventId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string EventType { get; set; }          // "user.login", "entity.viewed", "alert.acknowledged", etc.
    public string ActorType { get; set; }          // "user", "system", "connector"
    public Guid? ActorUserId { get; set; }
    public string? ActorIp { get; set; }
    public string? ActorUserAgent { get; set; }
    public string ResourceType { get; set; }       // "entity", "alert", "geofence", "user", etc.
    public Guid? ResourceId { get; set; }
    public string? Detail { get; set; }            // JSON with event-specific context
    public string Classification { get; set; }     // Classification of the accessed resource
}
```

### 5.3 What Gets Logged

| Category | Events |
|---|---|
| **Authentication** | Login success/failure, logout, token refresh, session eviction, password change, account lockout |
| **Entity Access** | Entity viewed, entity detail panel opened, track history queried, entity exported |
| **Operational Actions** | Geofence created/modified/deleted, watchlist entry added/removed, alert acknowledged/dismissed |
| **Correlation** | Entity merged (auto), entity merged (manual), entity split, correlation review decision |
| **Administration** | User created/deactivated, role changed, clearance changed, source connector toggled |
| **System** | Connector started/stopped/errored, ingestion pipeline health changes, database migrations |

### 5.4 Storage

```sql
CREATE TABLE audit_events (
    sequence_id     BIGSERIAL PRIMARY KEY,
    event_id        UUID NOT NULL DEFAULT gen_random_uuid(),
    timestamp       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    event_type      TEXT NOT NULL,
    actor_type      TEXT NOT NULL,
    actor_user_id   UUID,
    actor_ip        INET,
    actor_user_agent TEXT,
    resource_type   TEXT NOT NULL,
    resource_id     UUID,
    detail          JSONB,
    classification  TEXT DEFAULT 'OFFICIAL'
);

-- Append-only: no UPDATE or DELETE granted to the application role
-- Only SELECT and INSERT
REVOKE UPDATE, DELETE ON audit_events FROM app_user;

-- Partition by month for manageable table sizes
CREATE TABLE audit_events_2026_03 PARTITION OF audit_events
    FOR VALUES FROM ('2026-03-01') TO ('2026-04-01');

CREATE INDEX idx_audit_timestamp ON audit_events(timestamp);
CREATE INDEX idx_audit_actor ON audit_events(actor_user_id);
CREATE INDEX idx_audit_resource ON audit_events(resource_type, resource_id);
CREATE INDEX idx_audit_type ON audit_events(event_type);
```

### 5.5 Audit Log Viewer (Admin Only)

A dedicated page in the admin panel with filtering by time range, event type, actor, and resource. Supports export to CSV for compliance.

---

## 6. Infrastructure Security

### 6.1 Network

- All traffic over HTTPS (Caddy auto-TLS).
- Inter-container communication over Docker internal network (not exposed).
- PostgreSQL and Redis not exposed on host — only accessible via Docker network.
- Caddy rate limiting: 60 req/min per IP on auth endpoints.

### 6.2 Secrets Management

- All API keys, database credentials, and JWT signing keys in `.env` file (gitignored).
- Docker Compose interpolates from `.env`.
- Production: Docker secrets or environment variables from the host (no secrets baked into images).
- JWT signing key: RS256 with a 2048-bit RSA key pair. Private key never leaves the server.

### 6.3 Dependencies

- Dependabot enabled on the repository for automated CVE scanning.
- `dotnet list package --vulnerable` in CI pipeline — build fails on known critical CVEs.
- npm audit in CI for frontend dependencies.

### 6.4 CORS

- Strict origin allowlist (the frontend origin only).
- No wildcard origins.
- Credentials mode enabled for JWT cookies if applicable.

### 6.5 Input Validation

- All API inputs validated with FluentValidation.
- SQL injection prevention via parameterised queries only (EF Core + Dapper).
- XSS prevention via React's default escaping + CSP headers.
- File upload disabled in v1 (no user-uploaded content attack surface).

---

## 7. Threat Model

Included in the repository as `docs/THREAT_MODEL.md`. Covers:

1. **Attack surface diagram** — All entry points (API, SignalR, external source APIs).
2. **STRIDE analysis** per component (spoofing, tampering, repudiation, information disclosure, denial of service, elevation of privilege).
3. **Risk matrix** — Likelihood × impact for each identified threat.
4. **Mitigations** — Mapping each threat to the controls described in this spec.

This document alone demonstrates security awareness to a defence tech interviewer better than any amount of feature code.
