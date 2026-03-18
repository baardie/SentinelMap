# ADR-001: PMTiles Over OSM Raster Tiles

**Status:** Accepted
**Date:** 2026-03-18

## Context

SentinelMap needs a basemap for the Common Operating Picture. Options: proxy OSM raster tiles from an external server, or self-host vector tiles via PMTiles (Protomaps). The PRD requires air-gappable deployment — the system must function without internet access.

## Decision

Use PMTiles with a dark-styled MapLibre vector basemap. A single static `.pmtiles` file serves the entire world tileset. No tile server process required — MapLibre loads tiles directly via the pmtiles protocol handler.

## Consequences

- **Positive:** Fully air-gapped. No external dependencies at runtime. Custom dark styling trivial via MapLibre style JSON. Single file to manage.
- **Negative:** PMTiles file is large (~70GB for planet, ~1GB for a regional extract). Must be downloaded separately during setup. Not included in git.
- **Mitigated by:** `scripts/download-pmtiles.sh` downloads a UK regional extract (~100MB) for development. Planet file documented for production deployment.
