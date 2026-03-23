# M5: Security Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the auth system (login/refresh/revoke endpoints, refresh token storage), implement audit logging to PostgreSQL, add rate limiting, build a frontend login page with auth context, wire classification dynamically, and write the threat model document.

**Architecture:** Auth endpoints use ASP.NET Core Identity + JwtTokenService (already scaffolded). Refresh tokens stored in a new `refresh_tokens` table with family tracking for reuse detection. AuditService DB writes replace the TODO stubs. Rate limiting via ASP.NET Core middleware. Frontend gets a login page, auth context with token refresh, and protected routes.

**Spec:** `docs/superpowers/specs/2026-03-18-sentinelmap-system-design.md` — Section 9 (Security & Access Control)

**Codebase state:** M4 complete. Auth skeleton exists: JwtTokenService (RS256), Identity configured, UserSeeder creates 3 users, ClassificationAuthorizationHandler stubs, AuditService channel infrastructure with TODO DB writes, AuthEndpoints are stubs returning placeholder messages.

**IMPORTANT:** All work must stay within `C:\Users\lukeb\source\repos\SentinelMap`.

---

## File Structure (M5 Deliverables)

### New backend files
```
src/SentinelMap.Domain/
└── Entities/
    └── RefreshToken.cs                      (new)

src/SentinelMap.Infrastructure/
├── Data/
│   └── Configurations/
│       └── RefreshTokenConfiguration.cs     (new)
└── Auth/
    └── RefreshTokenService.cs               (new)
```

### Modified backend files
```
src/SentinelMap.Api/
├── Endpoints/
│   └── AuthEndpoints.cs                     (rewrite — login, refresh, revoke)
└── Program.cs                               (rate limiting, RefreshTokenService DI)

src/SentinelMap.Infrastructure/
├── Data/
│   ├── SystemDbContext.cs                   (add RefreshToken DbSet)
│   └── SentinelMapDbContext.cs              (add RefreshToken DbSet)
├── Services/
│   └── AuditService.cs                      (replace TODO stubs with DB writes)
└── Auth/
    └── ClassificationAuthorizationHandler.cs (no-op — already works correctly)
```

### New frontend files
```
client/src/
├── contexts/
│   └── AuthContext.tsx                       (new — auth state, login/logout, token refresh)
├── pages/
│   └── LoginPage.tsx                        (new)
└── lib/
    └── api.ts                               (new — fetch wrapper with auth headers)
```

### Modified frontend files
```
client/src/
├── App.tsx                                  (route guard, auth provider)
├── components/layout/
│   ├── ClassificationBanner.tsx             (dynamic from auth context)
│   └── TopBar.tsx                           (user info, logout)
└── hooks/
    └── useTrackHub.ts                       (pass auth token to SignalR)
```

### New documentation
```
docs/
└── THREAT_MODEL.md                          (new)
```

---

## Task 1: RefreshToken Entity + Migration

**Context:** Create a RefreshToken domain entity with family tracking for reuse detection. Generate EF Core migration.

**Files:**
- Create: `src/SentinelMap.Domain/Entities/RefreshToken.cs`
- Create: `src/SentinelMap.Infrastructure/Data/Configurations/RefreshTokenConfiguration.cs`
- Modify: `src/SentinelMap.Infrastructure/Data/SystemDbContext.cs` (add DbSet)
- Modify: `src/SentinelMap.Infrastructure/Data/SentinelMapDbContext.cs` (add DbSet)

- [ ] **Step 1: Create RefreshToken entity**

```csharp
namespace SentinelMap.Domain.Entities;

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;  // SHA256 hash of the token
    public string? FamilyId { get; set; }                   // For rotation tracking
    public string? DeviceInfo { get; set; }                 // User-Agent parsed
    public bool IsRevoked { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedAt { get; set; }
}
```

- [ ] **Step 2: Create configuration**

Map to `refresh_tokens` table, index on `(UserId)` and `(TokenHash)`.

- [ ] **Step 3: Add DbSet to both contexts**

- [ ] **Step 4: Generate and apply migration**

```bash
dotnet ef migrations add AddRefreshTokens -p src/SentinelMap.Infrastructure -s src/SentinelMap.Api
```

- [ ] **Step 5: Commit**

---

## Task 2: RefreshTokenService + Auth Endpoints

**Context:** Implement the RefreshTokenService for token lifecycle and rewrite AuthEndpoints with working login, refresh, and revoke handlers.

**Files:**
- Create: `src/SentinelMap.Infrastructure/Auth/RefreshTokenService.cs`
- Rewrite: `src/SentinelMap.Api/Endpoints/AuthEndpoints.cs`
- Modify: `src/SentinelMap.Api/Program.cs` (register RefreshTokenService)

- [ ] **Step 1: Create RefreshTokenService**

```csharp
public class RefreshTokenService
{
    Task<(string token, RefreshToken entity)> CreateAsync(Guid userId, string? deviceInfo, CancellationToken ct);
    Task<RefreshToken?> ValidateAndRotateAsync(string token, string? deviceInfo, CancellationToken ct);
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct);
    Task RevokeFamilyAsync(string familyId, CancellationToken ct);
}
```

- Token storage: hash with SHA256, store hash only
- Family tracking: new token gets same FamilyId as the rotated one
- Reuse detection: if a used token is presented again, revoke the entire family
- Max 5 active families per user — oldest family revoked when new one created

- [ ] **Step 2: Rewrite AuthEndpoints**

`POST /api/v1/auth/login`:
- Accept `{ email, password }`
- Validate via `UserManager.CheckPasswordAsync`
- Check lockout via `UserManager.IsLockedOutAsync`
- Load domain `User` for clearance level
- Generate access token via `JwtTokenService`
- Generate refresh token via `RefreshTokenService`
- Write `user.login` security audit event
- Return `{ accessToken, refreshToken, expiresIn: 900 }`

`POST /api/v1/auth/refresh`:
- Accept `{ refreshToken }`
- Validate and rotate via `RefreshTokenService`
- Generate new access token
- Return `{ accessToken, refreshToken, expiresIn: 900 }`

`POST /api/v1/auth/revoke` (requires auth):
- Revoke all refresh tokens for the current user
- Write `user.logout` security audit event

- [ ] **Step 3: Register in Program.cs**

- [ ] **Step 4: Build and test**

- [ ] **Step 5: Commit**

---

## Task 3: Audit Service DB Writes

**Context:** Replace the TODO stubs in AuditService with actual PostgreSQL writes. The audit_events table is partitioned monthly — ensure the partition exists before writing.

**Files:**
- Modify: `src/SentinelMap.Infrastructure/Services/AuditService.cs`
- Modify: `src/SentinelMap.Api/Program.cs` (ensure audit table + partition creation on startup)

- [ ] **Step 1: Create audit_events table on startup**

Read `AuditEventConfiguration.cs` — it may have the DDL. Add startup SQL in `Program.cs` (after migrations) to create the partitioned table and current month's partition if they don't exist.

- [ ] **Step 2: Implement WriteSecurityEventAsync**

Replace the TODO with a direct SQL INSERT:
```csharp
await using var scope = _scopeFactory.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
await db.Database.ExecuteSqlRawAsync(
    "INSERT INTO audit_events (event_type, user_id, action, resource_type, resource_id, details, ip_address, created_at) VALUES ({0}, {1}, {2}, {3}, {4}, {5}::jsonb, {6}, {7})",
    evt.EventType, evt.UserId, evt.Action, evt.ResourceType, evt.ResourceId, evt.Details, evt.IpAddress, DateTimeOffset.UtcNow);
```

- [ ] **Step 3: Implement background channel consumer**

Replace the TODO in the channel consumer loop with the same INSERT.

- [ ] **Step 4: Add audit calls to auth endpoints**

Login success/failure, refresh, revoke — all should write security audit events.

- [ ] **Step 5: Build and test**

- [ ] **Step 6: Commit**

---

## Task 4: Rate Limiting

**Context:** Add ASP.NET Core rate limiting middleware. Auth endpoints: 10/min per IP. API reads: 100/min. API writes: 30/min.

**Files:**
- Modify: `src/SentinelMap.Api/Program.cs`

- [ ] **Step 1: Add rate limiting**

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("auth", o => { o.Window = TimeSpan.FromMinutes(1); o.PermitLimit = 10; });
    options.AddFixedWindowLimiter("api-read", o => { o.Window = TimeSpan.FromMinutes(1); o.PermitLimit = 100; });
    options.AddFixedWindowLimiter("api-write", o => { o.Window = TimeSpan.FromMinutes(1); o.PermitLimit = 30; });
    options.RejectionStatusCode = 429;
});

// In middleware:
app.UseRateLimiter();
```

Apply `RequireRateLimiting("auth")` to auth endpoints.

- [ ] **Step 2: Build and test**

- [ ] **Step 3: Commit**

---

## Task 5: Frontend Login + Auth Context

**Context:** Create a login page, auth context for token management, and protect the main app behind authentication.

**Files:**
- Create: `client/src/lib/api.ts`
- Create: `client/src/contexts/AuthContext.tsx`
- Create: `client/src/pages/LoginPage.tsx`
- Modify: `client/src/App.tsx`
- Modify: `client/src/components/layout/ClassificationBanner.tsx`
- Modify: `client/src/components/layout/TopBar.tsx`
- Modify: `client/src/hooks/useTrackHub.ts`

- [ ] **Step 1: Create API client**

`client/src/lib/api.ts`:
- Wraps `fetch` with `Authorization: Bearer <token>` header
- Handles 401 by attempting token refresh
- Exports `apiGet`, `apiPost`, `apiPatch`, `apiDelete`

- [ ] **Step 2: Create AuthContext**

```typescript
interface AuthState {
  isAuthenticated: boolean
  user: { email: string; role: string; clearance: string } | null
  accessToken: string | null
  login: (email: string, password: string) => Promise<void>
  logout: () => Promise<void>
}
```

- Stores tokens in memory (not localStorage for security)
- Parses JWT to extract user info (decode base64 payload)
- Sets up auto-refresh timer (refresh 1 minute before expiry)
- On mount, try to use existing refresh token (stored in httpOnly cookie or memory)

For simplicity in this demo: store refresh token in localStorage (document this as a security tradeoff — production would use httpOnly cookies).

- [ ] **Step 3: Create LoginPage**

Defence-themed login form:
- Dark background (`bg-slate-950`)
- Centred card (`bg-slate-900 border border-slate-700`)
- "SENTINELMAP" header in mono uppercase
- Email + password inputs (slate theme, sharp corners)
- "AUTHENTICATE" button
- Error message display
- Classification banner at top

- [ ] **Step 4: Update App.tsx**

Wrap everything in `AuthProvider`. Show `LoginPage` when not authenticated, show the main COP when authenticated.

- [ ] **Step 5: Update ClassificationBanner**

Read clearance from `AuthContext` instead of hardcoded prop.

- [ ] **Step 6: Update TopBar**

Show user email, role badge, and "LOGOUT" button from `AuthContext`.

- [ ] **Step 7: Update useTrackHub**

Pass `accessToken` to SignalR connection:
```typescript
.withUrl('/hubs/tracks', { accessTokenFactory: () => accessToken })
```

Actually, the hub is currently not auth-protected. For the demo, keep it unauthenticated so tracks flow without login. The JWT Bearer middleware already handles the `access_token` query param for hubs — this can be added later.

- [ ] **Step 8: Build and verify**

```bash
cd client && npm run build
```

- [ ] **Step 9: Commit**

---

## Task 6: Threat Model Document

**Context:** Write a STRIDE threat model document covering the attack surface of SentinelMap. This is a portfolio piece — defence reviewers will look at it.

**Files:**
- Create: `docs/THREAT_MODEL.md`

- [ ] **Step 1: Write threat model**

Structure:
1. **System Overview** — architecture diagram (text-based), trust boundaries
2. **Attack Surface** — external APIs, WebSocket, SignalR, Caddy proxy, Docker networking
3. **STRIDE Analysis** — table with threats per component
4. **Risk Matrix** — likelihood × impact grid
5. **Mitigations Implemented** — map to actual code (JWT, RBAC, query filters, CSP, audit logging)
6. **Residual Risks** — honest assessment of what's not addressed (e.g., no WAF, no SIEM integration)
7. **Security Testing** — what tests verify security properties

- [ ] **Step 2: Commit**

---

## Task 7: Docker Compose + E2E Verification

- [ ] **Step 1: Build and test**
- [ ] **Step 2: Docker compose rebuild**
- [ ] **Step 3: Verify login flow**
  - Open `http://localhost` → redirected to login
  - Login with `admin@sentinel.local` / `Demo123!`
  - See COP with classification banner showing "SECRET"
  - TopBar shows user email and Admin role
  - Vessels + aircraft tracks visible
  - Alert feed working
  - Logout → back to login page
- [ ] **Step 4: Fix any issues**
- [ ] **Step 5: Final commit**

---

## Verification Checklist

- [ ] `dotnet build SentinelMap.slnx` — 0 errors
- [ ] `dotnet test SentinelMap.slnx` — all tests pass
- [ ] `cd client && npm run build` — succeeds
- [ ] `docker compose up --build` — all services healthy
- [ ] Login with seed credentials works
- [ ] JWT access token returned with correct claims
- [ ] Refresh token rotation works
- [ ] Rate limiting blocks auth brute force (429 after 10 requests/min)
- [ ] Audit events written to PostgreSQL
- [ ] Classification banner reflects user clearance
- [ ] TopBar shows user info + logout
- [ ] Protected API endpoints return 401 without token
- [ ] `docs/THREAT_MODEL.md` exists with STRIDE analysis
