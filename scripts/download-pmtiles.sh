#!/usr/bin/env bash
# Downloads a PMTiles file for the UK + English Channel region.
# Requires: pmtiles CLI — install with: npm install -g pmtiles
# Source: Protomaps daily planet build (public).
#
# Usage: bash scripts/download-pmtiles.sh

set -euo pipefail

SOURCE="https://build.protomaps.com/20260319.pmtiles"
OUTPUT="client/public/tiles/basemap.pmtiles"
BBOX="-11,49,3,60"
MAXZOOM=12

mkdir -p "$(dirname "$OUTPUT")"

if [ -f "$OUTPUT" ]; then
  echo "PMTiles file already exists at $OUTPUT — delete it to re-download."
  exit 0
fi

echo "Downloading UK/English Channel PMTiles region..."
echo "Source: $SOURCE"
echo "Bbox:   $BBOX"
echo "Output: $OUTPUT"
echo ""
echo "This may take several minutes depending on connection speed."

pmtiles extract "$SOURCE" "$OUTPUT" --bbox="$BBOX" --maxzoom="$MAXZOOM"

echo ""
echo "Done. File size: $(du -sh "$OUTPUT" | cut -f1)"
