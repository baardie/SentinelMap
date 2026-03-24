#!/usr/bin/env bash
# Downloads a PMTiles basemap for SentinelMap.
# Requires: pmtiles CLI — install with: npm install -g pmtiles
# Source: Protomaps daily planet build (public).
#
# Usage:
#   bash scripts/download-pmtiles.sh              # Default: UK + Ireland
#   bash scripts/download-pmtiles.sh europe        # Western Europe
#   bash scripts/download-pmtiles.sh world         # Global (large!)
#   bash scripts/download-pmtiles.sh mersey         # Liverpool/Mersey only (fast)

set -euo pipefail

SOURCE="https://build.protomaps.com/20260319.pmtiles"
OUTPUT="client/public/tiles/basemap.pmtiles"

PRESET="${1:-uk}"

case "$PRESET" in
  mersey|liverpool)
    BBOX="-3.9,52.9,-2.3,53.9"
    MAXZOOM=14
    DESC="Mersey / Liverpool (detailed, ~75MB)"
    ;;
  uk|default)
    BBOX="-11,49,3,60"
    MAXZOOM=12
    DESC="UK + Ireland (zoom 12, ~400MB)"
    ;;
  europe|eu)
    BBOX="-15,35,30,62"
    MAXZOOM=10
    DESC="Western Europe (zoom 10, ~1.5GB)"
    ;;
  world|global)
    BBOX="-180,-85,180,85"
    MAXZOOM=8
    DESC="Global (zoom 8, ~2GB)"
    ;;
  *)
    echo "Unknown preset: $PRESET"
    echo "Available: mersey, uk (default), europe, world"
    exit 1
    ;;
esac

mkdir -p "$(dirname "$OUTPUT")"

if [ -f "$OUTPUT" ]; then
  echo "PMTiles file already exists at $OUTPUT"
  echo "Delete it first to re-download: rm $OUTPUT"
  exit 0
fi

echo "Downloading PMTiles basemap..."
echo "Preset: $PRESET — $DESC"
echo "Source: $SOURCE"
echo "Bbox:   $BBOX"
echo "Zoom:   0–$MAXZOOM"
echo "Output: $OUTPUT"
echo ""

pmtiles extract "$SOURCE" "$OUTPUT" --bbox="$BBOX" --maxzoom="$MAXZOOM"

echo ""
echo "Done. File size: $(du -sh "$OUTPUT" | cut -f1)"
