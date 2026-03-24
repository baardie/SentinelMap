import { useEffect, useRef } from 'react'
import maplibregl from 'maplibre-gl'
import type { TrackPosition } from '../../types'

interface TrackReplayLayerProps {
  map: maplibregl.Map
  trackData: TrackPosition[]
  currentTime: Date | null
  visible: boolean
}

const FULL_LINE_SOURCE = 'replay-full-line'
const FULL_LINE_LAYER = 'replay-full-line-layer'
const TRAVERSED_SOURCE = 'replay-traversed-line'
const TRAVERSED_LAYER = 'replay-traversed-line-layer'
const MARKER_SOURCE = 'replay-marker'
const MARKER_LAYER = 'replay-marker-layer'
const HEADING_LAYER = 'replay-heading-layer'

function interpolatePosition(
  positions: TrackPosition[],
  time: Date,
): { lng: number; lat: number; heading: number; traversedCoords: [number, number][] } | null {
  if (positions.length === 0) return null

  const t = time.getTime()
  const firstTime = new Date(positions[0].observedAt).getTime()
  const lastTime = new Date(positions[positions.length - 1].observedAt).getTime()

  if (t <= firstTime) {
    return {
      lng: positions[0].longitude,
      lat: positions[0].latitude,
      heading: positions[0].heading ?? 0,
      traversedCoords: [[positions[0].longitude, positions[0].latitude]],
    }
  }

  if (t >= lastTime) {
    return {
      lng: positions[positions.length - 1].longitude,
      lat: positions[positions.length - 1].latitude,
      heading: positions[positions.length - 1].heading ?? 0,
      traversedCoords: positions.map(p => [p.longitude, p.latitude] as [number, number]),
    }
  }

  // Find bracket
  for (let i = 0; i < positions.length - 1; i++) {
    const t0 = new Date(positions[i].observedAt).getTime()
    const t1 = new Date(positions[i + 1].observedAt).getTime()

    if (t >= t0 && t <= t1) {
      const ratio = t1 === t0 ? 0 : (t - t0) / (t1 - t0)
      const lng = positions[i].longitude + ratio * (positions[i + 1].longitude - positions[i].longitude)
      const lat = positions[i].latitude + ratio * (positions[i + 1].latitude - positions[i].latitude)
      const heading = positions[i + 1].heading ?? positions[i].heading ?? 0

      const traversedCoords: [number, number][] = positions
        .slice(0, i + 1)
        .map(p => [p.longitude, p.latitude] as [number, number])
      traversedCoords.push([lng, lat])

      return { lng, lat, heading, traversedCoords }
    }
  }

  return null
}

export function TrackReplayLayer({ map, trackData, currentTime, visible }: TrackReplayLayerProps) {
  const setupDoneRef = useRef(false)

  // Set up sources and layers
  useEffect(() => {
    if (setupDoneRef.current) return
    if (map.getSource(FULL_LINE_SOURCE)) return

    map.addSource(FULL_LINE_SOURCE, {
      type: 'geojson',
      data: { type: 'FeatureCollection', features: [] },
    })

    map.addSource(TRAVERSED_SOURCE, {
      type: 'geojson',
      data: { type: 'FeatureCollection', features: [] },
    })

    map.addSource(MARKER_SOURCE, {
      type: 'geojson',
      data: { type: 'FeatureCollection', features: [] },
    })

    // Full track (faded)
    map.addLayer({
      id: FULL_LINE_LAYER,
      type: 'line',
      source: FULL_LINE_SOURCE,
      layout: { visibility: 'none' },
      paint: {
        'line-color': '#94a3b8',
        'line-opacity': 0.25,
        'line-width': 2,
        'line-dasharray': [4, 3],
      },
    })

    // Traversed track (bright)
    map.addLayer({
      id: TRAVERSED_LAYER,
      type: 'line',
      source: TRAVERSED_SOURCE,
      layout: { visibility: 'none' },
      paint: {
        'line-color': '#38bdf8',
        'line-opacity': 0.7,
        'line-width': 2.5,
      },
    })

    // Marker at current position
    map.addLayer({
      id: MARKER_LAYER,
      type: 'circle',
      source: MARKER_SOURCE,
      layout: { visibility: 'none' },
      paint: {
        'circle-radius': 6,
        'circle-color': '#38bdf8',
        'circle-stroke-color': '#0f172a',
        'circle-stroke-width': 2,
      },
    })

    // Heading indicator (small triangle rendered as symbol — fallback to another circle slightly offset)
    map.addLayer({
      id: HEADING_LAYER,
      type: 'circle',
      source: MARKER_SOURCE,
      layout: { visibility: 'none' },
      paint: {
        'circle-radius': 3,
        'circle-color': '#ffffff',
        'circle-translate': [0, -10],
      },
    })

    setupDoneRef.current = true

    return () => {
      ;[HEADING_LAYER, MARKER_LAYER, TRAVERSED_LAYER, FULL_LINE_LAYER].forEach(id => {
        if (map.getLayer(id)) map.removeLayer(id)
      })
      ;[MARKER_SOURCE, TRAVERSED_SOURCE, FULL_LINE_SOURCE].forEach(id => {
        if (map.getSource(id)) map.removeSource(id)
      })
      setupDoneRef.current = false
    }
  }, [map])

  // Update visibility
  useEffect(() => {
    const vis = visible ? 'visible' : 'none'
    ;[FULL_LINE_LAYER, TRAVERSED_LAYER, MARKER_LAYER, HEADING_LAYER].forEach(id => {
      if (map.getLayer(id)) map.setLayoutProperty(id, 'visibility', vis)
    })
  }, [map, visible])

  // Update full track line when trackData changes
  useEffect(() => {
    const source = map.getSource(FULL_LINE_SOURCE) as maplibregl.GeoJSONSource | undefined
    if (!source) return

    if (trackData.length < 2) {
      source.setData({ type: 'FeatureCollection', features: [] })
      return
    }

    source.setData({
      type: 'FeatureCollection',
      features: [
        {
          type: 'Feature',
          geometry: {
            type: 'LineString',
            coordinates: trackData.map(p => [p.longitude, p.latitude]),
          },
          properties: {},
        },
      ],
    })
  }, [map, trackData])

  // Update marker and traversed line when currentTime changes
  useEffect(() => {
    if (!visible || !currentTime || trackData.length === 0) return

    const result = interpolatePosition(trackData, currentTime)
    if (!result) return

    // Update marker
    const markerSource = map.getSource(MARKER_SOURCE) as maplibregl.GeoJSONSource | undefined
    if (markerSource) {
      markerSource.setData({
        type: 'FeatureCollection',
        features: [
          {
            type: 'Feature',
            geometry: {
              type: 'Point',
              coordinates: [result.lng, result.lat],
            },
            properties: { heading: result.heading },
          },
        ],
      })
    }

    // Update traversed line
    const traversedSource = map.getSource(TRAVERSED_SOURCE) as maplibregl.GeoJSONSource | undefined
    if (traversedSource && result.traversedCoords.length >= 2) {
      traversedSource.setData({
        type: 'FeatureCollection',
        features: [
          {
            type: 'Feature',
            geometry: {
              type: 'LineString',
              coordinates: result.traversedCoords,
            },
            properties: {},
          },
        ],
      })
    }
  }, [map, trackData, currentTime, visible])

  return null
}
