# ADR-004: Redis Backplane for SignalR

**Status:** Accepted
**Date:** 2026-03-18

## Context

Workers publish track updates and alerts to Redis. The API needs to push these to connected WebSocket clients via SignalR. Without a backplane, we'd need workers to call the API directly (violating "no service-to-service HTTP") or have the API poll Redis.

## Decision

Use Redis as the SignalR backplane. Workers publish to Redis channels. The API's SignalR hub subscribes via the backplane and pushes events to connected clients automatically.

## Consequences

- **Positive:** Maintains the "no service-to-service HTTP" constraint. Workers and API communicate entirely through Redis — publish/subscribe pattern. If the API scales to multiple instances, all instances receive events via the backplane.
- **Negative:** Redis becomes a critical dependency for real-time updates (already a dependency for dedup and caching, so no additional risk surface).
