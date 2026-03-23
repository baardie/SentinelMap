# SentinelMap Threat Model

| Field          | Value                                            |
|----------------|--------------------------------------------------|
| **Title**      | STRIDE Threat Model — SentinelMap v1.0           |
| **Date**       | 2026-03-23                                       |
| **Version**    | 1.0                                              |
| **Author**     | Luke B                                           |
| **Review Status** | Draft — pending external security review      |
| **Scope**      | SentinelMap v1.0 — OSINT Common Operating Picture platform (maritime AIS + aviation ADS-B) |

---

## 1. Introduction

SentinelMap is an OSINT aggregation and correlation platform fusing real-time maritime (AIS) and aviation (ADS-B) data into a unified Common Operating Picture (COP). The system is a portfolio demonstration targeting defence technology reviewers.

This document applies the STRIDE threat modelling framework to identify, categorise, and assess threats across all system components, then maps identified threats to implemented or recommended mitigations. It is intended to be honest about residual risk — a realistic threat model is more useful to defence reviewers than one that claims total security.

---

## 2. System Overview

### 2.1 Architecture Diagram (Text)

```
┌──────────────────────────────────────────────────────────────────────────┐
│  CLIENT TIER                                                             │
│  Browser (React SPA, MapLibre GL JS, SignalR WebSocket client)           │
└─────────────────────────────┬────────────────────────────────────────────┘
                              │  HTTPS / WSS  (TB5)
┌─────────────────────────────▼────────────────────────────────────────────┐
│  DMZ                                                                     │
│  Caddy reverse proxy  (port 80/443 — only externally exposed service)    │
│  CSP headers, X-Frame-Options, X-Content-Type-Options                   │
└──────┬───────────────────────────────────────────────────────────────────┘
       │  HTTP (TB2) — internal Docker bridge `sentinel`
┌──────▼──────────────────────────────────────────────┐
│  APP TIER                                           │
│  ┌─────────────────────────┐  ┌──────────────────┐ │
│  │ SentinelMap.Api          │  │ SentinelMap.     │ │
│  │ ASP.NET Core 9           │  │ Workers          │ │
│  │ REST API + SignalR hub   │  │ Ingestion /      │ │
│  │ /api/* /hubs/* /swagger/ │  │ Correlation /    │ │
│  │ FluentValidation         │  │ Alerting         │ │
│  │ JWT RS256 Auth           │  │ BackgroundService│ │
│  └────────────┬────────────┘  └────────┬─────────┘ │
└───────────────┼─────────────────────────┼───────────┘
                │  (TB3)                  │  (TB4 — outbound only)
┌───────────────▼───────────────┐    ┌────▼──────────────────────────┐
│  DATA TIER                    │    │  EXTERNAL DATA SOURCES        │
│  ┌────────────┐ ┌───────────┐ │    │  AISStream.io   (WSS outbound)│
│  │ PostgreSQL │ │   Redis   │ │    │  Airplanes.live (HTTPS poll)  │
│  │ PostGIS    │ │ 7-alpine  │ │    └───────────────────────────────┘
│  │ (db:5432)  │ │(redis:6379│ │
│  └────────────┘ └───────────┘ │
└───────────────────────────────┘
```

### 2.2 Components

| Component | Tier | Description |
|-----------|------|-------------|
| Browser (React SPA) | Client | MapLibre GL JS map, SignalR WebSocket client, shadcn/ui |
| Caddy | DMZ | Reverse proxy; only service with external ports (80, 443) |
| SentinelMap.Api | App | ASP.NET Core 9 REST API + SignalR hub; JWT RS256 auth; rate limiting |
| SentinelMap.Workers | App | Background services: ingestion, correlation, alerting |
| PostgreSQL + PostGIS | Data | Primary store: entities, observations, users, alerts, geofences, audit log |
| Redis | Data | Pub/sub backplane; deduplication cache; geofence membership state |

### 2.3 External Integrations

| Integration | Direction | Protocol | Auth |
|-------------|-----------|----------|------|
| AISStream.io | Workers → external (outbound) | WSS | API key |
| Airplanes.live | Workers → external (outbound) | HTTPS REST poll | None required |

---

## 3. Trust Boundaries

| ID | Boundary | Description |
|----|----------|-------------|
| TB1 | Internet ↔ Caddy | Public internet traffic enters at the Caddy reverse proxy. Caddy is the sole internet-facing service. All other services are on the internal `sentinel` bridge and have no external port bindings in production. |
| TB2 | Caddy ↔ Internal services (Api, Web) | HTTP traffic from Caddy to Api (`api:5000`) and to the Web SPA container (`web:80`) crosses from the DMZ into the app tier. No TLS on this hop — relies on Docker network isolation. |
| TB3 | Api / Workers ↔ PostgreSQL / Redis | Authenticated database and cache connections within the `sentinel` bridge. Credentials passed via environment variables. No mTLS. |
| TB4 | Workers ↔ External data sources | Outbound-only connections from Workers. AISStream.io uses an API key. Airplanes.live requires no auth. Data arrives unverified — AIS and ADS-B have no cryptographic integrity guarantees. |
| TB5 | Browser ↔ Caddy | HTTPS/WSS from end-user browser. This is the user-facing attack surface: authentication, JWT handling, CSP policy enforcement. |

---

## 4. STRIDE Analysis

### Key

| Column | Description |
|--------|-------------|
| Likelihood | Low / Medium / High — probability of exploitation in the current deployment context |
| Impact | Low / Medium / High — consequence if successfully exploited |
| Risk | Low / Medium / High / Critical — combined assessment |
| Status | Mitigated / Partial / Residual / Accepted |

---

### 4.1 Spoofing

| ID | Component | Threat Category | Threat Description | Likelihood | Impact | Risk | Mitigation | Status |
|----|-----------|----------------|-------------------|-----------|--------|------|-----------|--------|
| S1 | Api — `/api/v1/auth/login` | Spoofing | Credential brute force: attacker makes repeated login attempts to guess a user's password and authenticate as them. | Medium | High | High | ASP.NET Core Identity account lockout: 5 failed attempts triggers a 15-minute lockout. Rate limiting middleware: 5 requests/min on auth endpoints. Password policy enforces 12+ character minimum (NIST 800-63B). | Mitigated |
| S2 | Browser / Api | Spoofing | JWT access token theft and replay: an attacker obtains a valid JWT (via XSS, network interception, or local storage access) and uses it to impersonate the legitimate user. | Medium | High | High | Access tokens are short-lived (15 minutes), limiting the replay window. RS256 asymmetric signing makes token forgery computationally infeasible. CSP headers (`default-src 'self'`) reduce XSS risk. TLS in transit via Caddy. Residual risk: tokens in browser memory/localStorage are accessible to JavaScript. | Partial |
| S3 | Browser / Api | Spoofing | Refresh token theft and reuse: a stolen refresh token is replayed after the legitimate user has already used it once. | Low | High | Medium | Refresh token family tracking (`RefreshTokenService.cs`): reuse of any token in a family immediately revokes the entire family, forcing reauthentication for all sessions. Sessions UI (`/profile/sessions`) allows users to revoke individual families. | Mitigated |
| S4 | Workers — AIS ingestion | Spoofing | AIS data spoofing: malicious AIS signals (broadcast from a rogue transponder or software-defined radio) inject false vessel positions into the ingestion pipeline, causing alerts based on fabricated data or corrupting entity tracking. | High | Medium | High | AIS protocol has no cryptographic authentication — this is an inherent limitation of the protocol. FluentValidation rejects observations with impossible coordinates, timestamps in the future, or >24h stale. Correlation confidence scoring flags low-confidence entity links for analyst review rather than auto-merging. Speed anomaly alert catches implausible AIS position jumps. Residual risk: no application-level mechanism can distinguish authentic from spoofed AIS. | Accepted |

---

### 4.2 Tampering

| ID | Component | Threat Category | Threat Description | Likelihood | Impact | Risk | Mitigation | Status |
|----|-----------|----------------|-------------------|-----------|--------|------|-----------|--------|
| T1 | Api — all endpoints | Tampering | SQL injection via API inputs: attacker sends crafted payloads in query parameters or request bodies to manipulate database queries and access or corrupt data. | Low | Critical | High | EF Core with parameterised queries prevents classic SQL injection across all generated queries. FluentValidation on all request DTOs rejects malformed input at the API boundary. No raw SQL constructed from user input in the codebase. | Mitigated |
| T2 | TB2 — Caddy ↔ Api | Tampering | Observation data manipulation in transit between Caddy and the Api container: an attacker with access to the Docker host or network namespace modifies HTTP traffic on the internal bridge. | Low | High | Medium | Threat requires prior host-level compromise. Docker `sentinel` bridge isolates service-to-service traffic. No other containers on the bridge. Residual risk: the Caddy-to-Api hop uses plain HTTP; adding mTLS would eliminate this residual. | Partial |
| T3 | Redis pub/sub channels | Tampering | Redis pub/sub message injection: a process on the `sentinel` network publishes crafted messages to `observations:*`, `entities:updated`, or `alerts:*` channels, causing Workers or the Api to process malicious events. | Low | High | Medium | No Redis authentication in the base configuration (`redis:7-alpine` with no `requirepass`). The `sentinel` Docker bridge limits access to co-located containers only. Residual risk: any compromised container on the bridge could inject messages. Adding Redis `AUTH` and input validation on deserialized pub/sub payloads would reduce this further. | Partial |

---

### 4.3 Repudiation

| ID | Component | Threat Category | Threat Description | Likelihood | Impact | Risk | Mitigation | Status |
|----|-----------|----------------|-------------------|-----------|--------|------|-----------|--------|
| R1 | Api — `PATCH /alerts/{id}/acknowledge` | Repudiation | Unlogged alert acknowledgements: an analyst acknowledges an alert without a durable record of who acknowledged it or when, allowing plausible deniability. | Low | Medium | Low | `AuditService.cs` records alert state transitions as operational events (async bounded-channel write). All alert lifecycle transitions (`Triggered → Acknowledged → Resolved/Dismissed`) are captured with user identity and timestamp. Dismissals require a reason field. Residual risk: async audit path is fire-and-forget — if the container crashes during flush, the operational event may be lost. Security events (auth, role changes) use the synchronous `WriteSecurityEventAsync` path and are not subject to this risk. | Partial |
| R2 | Api — geofence mutation endpoints | Repudiation | Unlogged geofence modifications: geofences are created, updated, or deleted without a tamper-evident audit trail, making it impossible to reconstruct changes that led to a missed alert. | Low | Medium | Low | Geofence mutations (create, update, delete) are audit-logged as operational events via `AuditService`. Admin users can access the full audit log. Residual risk: same async-flush caveat as R1 applies to operational events. No external SIEM receives these logs — if the database is compromised, audit records could be altered. | Partial |

---

### 4.4 Information Disclosure

| ID | Component | Threat Category | Threat Description | Likelihood | Impact | Risk | Mitigation | Status |
|----|-----------|----------------|-------------------|-----------|--------|------|-----------|--------|
| I1 | Api — entity/alert/geofence endpoints | Information Disclosure | Classification bypass: a user with lower clearance (e.g. OFFICIAL) receives data classified above their clearance level (e.g. SECRET) due to a failure in the classification filter. | Low | Critical | High | EF Core global query filter on `SentinelMapDbContext` automatically restricts all queries to data at or below the user's `ClearanceLevel`, scoped via `IUserContext`. The `ClassifiedAccess` policy and `ClassificationAuthorizationHandler` enforce this at the endpoint level. Worker processes use a separate `SystemDbContext` without filtering — they are not reachable from the internet. Residual risk: classification is mock — there is no KMS or HSM. Clearance levels are stored in the same database as the data they protect. A database-level compromise would expose all data regardless of clearance. | Partial |
| I2 | Browser / JWT payload | Information Disclosure | JWT payload exposure: the JWT access token payload contains sensitive claims (user role, clearance level) that can be trivially decoded (base64) by anyone who obtains the token. | Medium | Medium | Medium | JWT claims are application roles and clearance levels — not credentials or PII beyond user ID. Short token lifetime (15 minutes) limits exposure window. No secret keys or database credentials included in JWT payload. Residual risk: role and clearance level claims are readable by any token holder; token theft would expose these values alongside authentication. | Accepted |
| I3 | docker-compose.yml / environment vars | Information Disclosure | Database credential exposure: connection strings including the PostgreSQL password are stored in `docker-compose.yml` environment variables in plaintext. | Medium | High | High | The base compose file contains the development password (`sentinel_dev_password`). This is a known dev-environment practice. `SENTINELMAP_SEED_PASSWORD` environment variable for seed users must be overridden in production. Residual risk: no secrets management (Vault, Docker Secrets, or environment-variable injection from a secrets store) is implemented. Production deployments must override all default credentials. | Residual |
| I4 | Api — error responses | Information Disclosure | Stack trace leakage: unhandled exceptions return stack traces or internal error details (file paths, class names, connection strings) to API consumers, aiding reconnaissance. | Low | Medium | Low | ASP.NET Core in production mode (`ASPNETCORE_ENVIRONMENT=Production`) suppresses detailed exception pages. Error responses use RFC 7807 Problem Details format — `title`, `status`, `detail` only; no stack traces. Development environment exposes additional detail intentionally. | Mitigated |

---

### 4.5 Denial of Service

| ID | Component | Threat Category | Threat Description | Likelihood | Impact | Risk | Mitigation | Status |
|----|-----------|----------------|-------------------|-----------|--------|------|-----------|--------|
| D1 | Api — all endpoints | Denial of Service | API rate limit bypass: attacker distributes requests across multiple IP addresses or rotated authentication tokens to exceed per-client rate limits without triggering a block, overwhelming the Api or database. | Medium | Medium | Medium | ASP.NET Core rate limiting middleware enforces: auth endpoints 5/min, API reads 100/min, API writes 30/min. Limits are per-client (IP + user). Residual risk: no WAF in front of Caddy. Distributed attacks from many IPs are not rate-limited by the in-process middleware alone. Adding a cloud WAF or ModSecurity would harden this boundary. | Partial |
| D2 | Workers — AIS ingestion (WSS to AISStream.io) | Denial of Service | WebSocket connection exhaustion: the AISStream.io WebSocket is flooded with high-volume AIS traffic causing the in-memory buffer to fill, triggering backpressure that drops observations. | Medium | Low | Low | The ingestion pipeline uses a bounded in-memory buffer (1,000 observations) with drop-oldest backpressure. For positional data this is safe — newer positions supersede older ones. Circuit breaker (3 failures → 30 s open → half-open probe) protects against external source instability. This threat is a data quality degradation, not a service outage — the system continues operating with reduced data fidelity. | Mitigated |
| D3 | Redis — pub/sub channels | Denial of Service | Redis pub/sub flood: a compromised container or misconfigured internal service publishes messages at a rate that saturates the `sentinel`, `entities:updated`, or `observations:*` channels, causing the correlation and alerting workers to fall behind or crash. | Low | Medium | Low | Redis 7 is single-threaded for command processing but handles pub/sub efficiently at high rates. Worker subscribers process messages via `IAsyncEnumerable` with backpressure propagation. No Redis `maxmemory` policy configured — a flood could exhaust Redis memory. Residual risk: no Redis `AUTH` in the base config reduces the barrier for a co-located attacker to flood channels. | Partial |
| D4 | PostgreSQL — `observations` table | Denial of Service | Observation table partition exhaustion: continuous ingestion of high-volume AIS/ADS-B data fills the `observations` table partitions faster than retention policies archive or purge them, degrading query performance and eventually exhausting disk. | Low | Medium | Low | Observations are batched (`SaveChangesAsync` batch insert). Deduplication via Redis (60 s TTL buckets) reduces write volume significantly. Residual risk: no data retention/archiving pipeline is implemented in v1. Long-running deployments will require a maintenance job to partition and archive older observations. | Residual |

---

### 4.6 Elevation of Privilege

| ID | Component | Threat Category | Threat Description | Likelihood | Impact | Risk | Mitigation | Status |
|----|-----------|----------------|-------------------|-----------|--------|------|-----------|--------|
| E1 | Api — JWT validation | Elevation of Privilege | Role escalation via JWT manipulation: an attacker forges or modifies a JWT to claim a higher role (e.g. changing `Viewer` to `Admin`) without valid credentials. | Low | Critical | High | JWT tokens are signed with RS256 (asymmetric key pair). The private key is held by the Api only; the public key is used for validation. Modifying the payload invalidates the signature and causes rejection. Residual risk: the RSA key pair is generated ephemerally at startup — a container restart rotates the key, invalidating all issued tokens. In production, the key must be persisted (HSM or persistent volume) to avoid disrupting sessions. | Mitigated |
| E2 | PostgreSQL — direct access | Elevation of Privilege | Direct database access bypassing classification filters: an attacker with database credentials (from leaked connection strings or a compromised container) connects directly to PostgreSQL and queries data without the EF Core classification filters, accessing SECRET-classified data as an unauthenticated user. | Low | Critical | High | The `sentinel` Docker bridge isolates the database from external networks — in production, port 5432 is not exposed. `docker-compose.override.yml` exposes 5432 only for local development. Residual risk: any actor who obtains the connection string (e.g. via the `docker-compose.yml` plaintext credentials) and network access to the host can bypass all application-layer controls. Classification filtering is an application concern, not a database-level control. No row-level security (RLS) is implemented in PostgreSQL. | Residual |
| E3 | Api — watchlist / geofence endpoints | Elevation of Privilege | Horizontal privilege escalation: a Viewer-role user crafts requests to access or modify another user's watchlists or geofences by guessing or enumerating resource GUIDs belonging to other users. | Low | Medium | Medium | Authorization policies enforce role-based access (`AnalystAccess` for watchlist and geofence mutation). All resource queries are scoped to the authenticated user's identity via `IUserContext`. GUIDs are non-sequential (UUID v4), making enumeration impractical. Residual risk: tests confirming ownership enforcement on all mutation endpoints should be added to the test suite. | Partial |

---

## 5. Risk Matrix

The following matrix maps each threat by Likelihood and Impact. Threats in the High/Critical cells require priority attention.

```
            │  LOW IMPACT  │  MEDIUM IMPACT  │  HIGH IMPACT
            │              │                 │
HIGH        │              │  S4, D1         │  S1, S2
LIKELIHOOD  │              │                 │
────────────┼──────────────┼─────────────────┼──────────────
MEDIUM      │              │  R1, R2, I2,    │  I3, E1*
LIKELIHOOD  │              │  D2, D3         │
────────────┼──────────────┼─────────────────┼──────────────
LOW         │  I4          │  T3, E3, D4     │  T1, T2, I1,
LIKELIHOOD  │              │                 │  E2
```

*E1 (role escalation via JWT): Likelihood Low — RS256 makes forgery computationally infeasible, but Impact is Critical, placing it in the High-Impact column for tracking purposes.

### Residual High-Risk Items (no complete mitigation)

| ID | Summary | Why Residual |
|----|---------|-------------|
| S4 | AIS data spoofing | Protocol-level limitation; no application countermeasure can fully address |
| I3 | Database credential exposure | Plaintext credentials in docker-compose; secrets management not implemented |
| E2 | Direct DB access bypasses classification | No PostgreSQL RLS; application-layer filtering only |

---

## 6. Mitigations Implemented

The following table maps security controls to the source files where they are implemented.

| Control | Implementation Location | Threats Addressed |
|---------|------------------------|-------------------|
| RS256 JWT signing with 15-minute access token lifetime | `src/SentinelMap.SharedKernel/Auth/JwtTokenService.cs` | S2, E1 |
| Refresh token family tracking; reuse revokes entire family | `src/SentinelMap.SharedKernel/Auth/RefreshTokenService.cs` | S3 |
| EF Core global query filter for classification scoping | `src/SentinelMap.Infrastructure/Data/SentinelMapDbContext.cs` | I1, E2 (partial) |
| Two-path audit logging (synchronous security events; async operational events) | `src/SentinelMap.SharedKernel/Audit/AuditService.cs` | R1, R2 |
| ASP.NET Core rate limiting middleware (auth 5/min, reads 100/min, writes 30/min) | `src/SentinelMap.Api/Program.cs` | S1, D1 |
| Account lockout (5 failures → 15-minute lockout) and password policy (12+ chars, NIST 800-63B) | `src/SentinelMap.Api/Program.cs` — ASP.NET Core Identity configuration | S1 |
| Security response headers: `Content-Security-Policy`, `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff` | `Caddyfile` | S2 (XSS vector), I2 |
| Docker bridge network isolation — only Caddy exposes external ports | `docker-compose.yml` — `networks.sentinel` | T2, T3, E2 |
| FluentValidation on all API request DTOs; lat/lon bounds, timestamp sanity checks | `src/SentinelMap.Api/` validators | T1, S4 |
| Named authorization policies and `ClassificationAuthorizationHandler` | `src/SentinelMap.Api/Program.cs` | I1, E1, E3 |
| RFC 7807 Problem Details error responses (no stack traces in production) | `src/SentinelMap.Api/Program.cs` — exception handler | I4 |
| CORS origin allowlist | `src/SentinelMap.Api/Program.cs` | S2 (CSRF mitigation) |

### CSP Policy (from Caddyfile)

```
default-src 'self';
script-src 'self' 'unsafe-inline';
style-src 'self' 'unsafe-inline';
img-src 'self' data: blob: https://protomaps.github.io;
connect-src 'self' ws: wss: https://protomaps.github.io;
worker-src 'self' blob:;
font-src 'self' https://protomaps.github.io;
```

Note: `'unsafe-inline'` for `script-src` and `style-src` weakens the XSS protection offered by CSP. Migrating to a nonce-based or hash-based approach would harden this policy.

---

## 7. Residual Risks

The following risks are known and accepted at v1.0 for a portfolio/demonstration deployment. Each would require additional investment to address in a production environment.

| # | Residual Risk | Rationale / Path to Resolution |
|---|--------------|-------------------------------|
| 1 | **No WAF in front of Caddy** | Distributed attacks, OWASP Top 10 probing, and volumetric floods are not filtered before reaching the application. Resolution: add ModSecurity to the Caddy container, or route through a cloud WAF (AWS WAF, Cloudflare). |
| 2 | **No SIEM or external log aggregation** | Application and audit logs exist only within the container. A container restart, volume loss, or database compromise could destroy audit evidence. Resolution: structured logging (Serilog) shipped to ELK Stack or Splunk via a log forwarder. |
| 3 | **Classification system is mock; no KMS or HSM** | Classification levels are stored in the same PostgreSQL instance as the data they protect. There is no envelope encryption, key management service, or hardware security module. A database-level compromise exposes all data regardless of classification. Resolution: integrate AWS KMS, Azure Key Vault, or HashiCorp Vault for envelope encryption of SECRET-classified records. |
| 4 | **Refresh tokens stored in localStorage (not httpOnly cookies)** | Tokens in localStorage are accessible to JavaScript — any XSS vulnerability can exfiltrate them. Resolution: store refresh tokens in httpOnly, Secure, SameSite=Strict cookies, making them inaccessible to JavaScript entirely. |
| 5 | **No mTLS between internal services** | Service-to-service traffic on the `sentinel` bridge (Caddy → Api, Api → PostgreSQL, Api → Redis) uses plain HTTP/TCP. A compromised container on the bridge can observe or tamper with this traffic. Resolution: implement mTLS with mutual certificate authentication between all internal services. |
| 6 | **AIS and ADS-B data are not cryptographically authenticated** | VHF AIS and 1090 MHz ADS-B transmissions have no digital signatures. Any radio transmitter can broadcast plausible-looking messages. This is an inherent protocol limitation — no application-layer countermeasure can fully resolve it. Resolution: cross-validate tracks between multiple independent receivers and flag single-source reports for analyst review. |
| 7 | **No penetration testing performed** | The system has not been subjected to OWASP ZAP scanning, manual penetration testing, or independent security review. All security claims are based on design intent and unit tests, not adversarial validation. Resolution: conduct OWASP ZAP baseline scan, manual pen test of auth flows, and independent code review before any production deployment. |
| 8 | **Ephemeral RSA key pair for JWT signing** | The RSA key pair used for JWT RS256 signing is generated at Api startup. A container restart rotates the key, immediately invalidating all issued access tokens. Resolution: persist the key pair in a secrets store (Docker Secrets, Vault) or back it with an HSM. |
| 9 | **Redis has no authentication** | The `redis:7-alpine` service runs without a `requirepass` directive. Any container on the `sentinel` bridge can connect without credentials. Resolution: configure Redis `AUTH` and pass the password via a Docker Secret. |

---

## 8. Security Testing

### 8.1 Tests Implemented

| Test Area | Coverage |
|-----------|----------|
| JWT token generation and RS256 signature validation | Unit tests verify tokens are correctly signed and rejected when signature is invalid or claims are tampered with |
| Classification query filter | Unit tests confirm Viewers cannot retrieve OFFICIAL-SENSITIVE or SECRET entities; Analysts cannot retrieve SECRET entities |
| Refresh token family revocation | Unit tests verify reuse of a consumed refresh token revokes the entire family |
| Rate limiting behaviour | Integration tests confirm auth endpoint rejects the 6th request within a minute |
| Account lockout | Integration tests confirm the 6th failed login within the lockout window is rejected |
| Input validation (FluentValidation) | Unit tests on validators for lat/lon bounds, required fields, timestamp sanity |

### 8.2 Recommended Additional Testing

| Test | Tool / Approach | Priority |
|------|----------------|----------|
| OWASP ZAP baseline scan | OWASP ZAP in CI pipeline against a running Docker Compose instance | High |
| Manual pen test of auth flows (token theft, replay, family revocation) | Manual testing with Burp Suite | High |
| SQL injection fuzz testing on all API endpoints | SQLMap or OWASP ZAP active scan | Medium |
| WebSocket message injection against SignalR hub | Custom tooling or Burp Suite WebSocket extension | Medium |
| Rate limit bypass via IP rotation | Manual testing with rotating proxies | Medium |
| Dependency vulnerability scan | `dotnet list package --vulnerable`, `npm audit` | High (ongoing) |

---

## 9. Recommendations for Production

The following changes are required before this system handles real operational data or is exposed to an untrusted network in a production capacity.

| Priority | Recommendation | Threat(s) Addressed |
|----------|---------------|---------------------|
| P1 | Replace ephemeral RSA key pair with HSM-backed or persisted key (Docker Secret / Vault) | E1, S2 (token invalidation on restart) |
| P1 | Move refresh tokens from localStorage to httpOnly, Secure, SameSite=Strict cookies | S2, S3 |
| P1 | Override all default credentials (`sentinel_dev_password`, `Demo123!`) via secrets management; remove plaintext credentials from `docker-compose.yml` | I3 |
| P1 | Conduct OWASP ZAP scan and manual penetration test before production deployment | All |
| P2 | Add WAF layer (ModSecurity on Caddy or cloud WAF) in front of the reverse proxy | D1, T1 |
| P2 | Implement mTLS between internal services (Caddy ↔ Api, Api ↔ Redis, Api ↔ PostgreSQL) | T2, T3 |
| P2 | Add Redis `AUTH` password (via Docker Secret) | T3, D3 |
| P2 | Implement PostgreSQL row-level security (RLS) to enforce classification at the database layer | I1, E2 |
| P3 | Integrate structured logging with SIEM (Serilog → ELK Stack or Splunk) | R1, R2 — durable audit trail |
| P3 | Implement a data retention and archiving pipeline for the `observations` table | D4 |
| P3 | Tighten CSP policy: replace `'unsafe-inline'` with nonce-based or hash-based script/style sources | S2 |
| P3 | Add regular automated dependency vulnerability scanning in CI (`dotnet list package --vulnerable`, `npm audit`) | General supply-chain risk |

---

## Appendix A — STRIDE Reference

| Letter | Category | Question Asked |
|--------|----------|---------------|
| S | Spoofing | Can an attacker falsely claim an identity? |
| T | Tampering | Can an attacker modify data or code? |
| R | Repudiation | Can an actor deny performing an action? |
| I | Information Disclosure | Can an attacker access data they should not see? |
| D | Denial of Service | Can an attacker prevent legitimate use? |
| E | Elevation of Privilege | Can an attacker gain more permissions than granted? |

---

*This document was produced as part of the SentinelMap portfolio project. It represents a good-faith assessment of the system's security posture at v1.0. It is not a substitute for professional penetration testing or an independent security audit.*
