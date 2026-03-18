# ADR-006: SystemDbContext Without Classification Filters

**Status:** Accepted
**Date:** 2026-03-18

## Context

EF Core global query filters enforce classification-based data access in the API — users only see data at or below their clearance level. However, background workers (ingestion, correlation, alerting) need unfiltered access to all data regardless of classification.

## Decision

Two DbContext types: `SentinelMapDbContext` (filtered, scoped via `IUserContext` from HTTP context) for the API, and `SystemDbContext` (unfiltered) for background workers. Workers run at system level.

## Consequences

- **Positive:** Classification filtering is impossible to bypass through any API endpoint — it's enforced at the query level, not the controller level. Workers aren't artificially restricted from processing data they need.
- **Negative:** Two DbContext types to maintain. Must ensure workers never accidentally use the filtered context.
- **Mitigated by:** Separate DI registration per host. API registers `SentinelMapDbContext`, Workers registers `SystemDbContext`. No cross-contamination possible.
