# ADR-002: Separate Correlation Worker Over Inline Pipeline

**Status:** Accepted
**Date:** 2026-03-18

## Context

The correlation engine could run inline within the ingestion pipeline (process each observation immediately after persistence) or as a separate background service subscribing to Redis pub/sub.

## Decision

Correlation runs as a dedicated `BackgroundService` subscribing to `observations:*` Redis channel. Ingestion and correlation are independent consumers.

## Consequences

- **Positive:** Ingestion and correlation fail independently — a slow correlation query doesn't backpressure ingestion. Observations are persisted even if correlation is degraded. Architecture diagram maps 1:1 to actual runtime components. Can be independently monitored and scaled.
- **Negative:** Slightly higher latency (Redis hop). More complex startup ordering.
- **Mitigated by:** Hot-path cache ensures 90%+ of observations skip the full correlation pipeline entirely. Redis pub/sub latency is sub-millisecond.
