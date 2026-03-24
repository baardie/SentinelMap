import { useEffect, useRef } from 'react'
import maplibregl from 'maplibre-gl'
import type { TrackFeature } from '../../types'

interface PredictionLayerProps {
  map: maplibregl.Map
  tracks: TrackFeature[]
  visible: boolean
  predictionMinutes?: number
}

const LINE_SOURCE = 'prediction-lines'
const LINE_LAYER = 'prediction-lines-layer'
const DOT_SOURCE = 'prediction-dots'
const DOT_LAYER = 'prediction-dots-layer'

const MIN_SPEED_KNOTS = 2
const MPS_TO_KNOTS = 1.94384

export function PredictionLayer({ map, tracks, visible, predictionMinutes = 15 }: PredictionLayerProps) {
  const setupDoneRef = useRef(false)

  // Set up sources and layers
  useEffect(() => {
    if (setupDoneRef.current) return
    if (map.getSource(LINE_SOURCE)) return

    map.addSource(LINE_SOURCE, {
      type: 'geojson',
      data: { type: 'FeatureCollection', features: [] },
    })

    map.addSource(DOT_SOURCE, {
      type: 'geojson',
      data: { type: 'FeatureCollection', features: [] },
    })

    map.addLayer({
      id: LINE_LAYER,
      type: 'line',
      source: LINE_SOURCE,
      layout: { visibility: 'none' },
      paint: {
        'line-color': '#ffffff',
        'line-opacity': 0.3,
        'line-width': 1.5,
        'line-dasharray': [4, 4],
      },
    })

    map.addLayer({
      id: DOT_LAYER,
      type: 'circle',
      source: DOT_SOURCE,
      layout: { visibility: 'none' },
      paint: {
        'circle-radius': 3,
        'circle-color': '#ffffff',
        'circle-opacity': 0.3,
      },
    })

    setupDoneRef.current = true

    return () => {
      ;[DOT_LAYER, LINE_LAYER].forEach(id => {
        if (map.getLayer(id)) map.removeLayer(id)
      })
      ;[DOT_SOURCE, LINE_SOURCE].forEach(id => {
        if (map.getSource(id)) map.removeSource(id)
      })
      setupDoneRef.current = false
    }
  }, [map])

  // Update visibility
  useEffect(() => {
    const vis = visible ? 'visible' : 'none'
    ;[LINE_LAYER, DOT_LAYER].forEach(id => {
      if (map.getLayer(id)) map.setLayoutProperty(id, 'visibility', vis)
    })
  }, [map, visible])

  // Update prediction geometries
  useEffect(() => {
    const lineSource = map.getSource(LINE_SOURCE) as maplibregl.GeoJSONSource | undefined
    const dotSource = map.getSource(DOT_SOURCE) as maplibregl.GeoJSONSource | undefined
    if (!lineSource || !dotSource) return

    if (!visible) {
      lineSource.setData({ type: 'FeatureCollection', features: [] })
      dotSource.setData({ type: 'FeatureCollection', features: [] })
      return
    }

    const lineFeatures: GeoJSON.Feature[] = []
    const dotFeatures: GeoJSON.Feature[] = []

    for (const track of tracks) {
      const speedMps = track.properties.speed ?? 0
      const speedKnots = speedMps * MPS_TO_KNOTS
      if (speedKnots < MIN_SPEED_KNOTS) continue
      if (track.properties.heading == null) continue

      const [lng, lat] = track.geometry.coordinates
      const headingRad = (track.properties.heading * Math.PI) / 180

      const distanceM = speedMps * predictionMinutes * 60
      const distanceDeg = distanceM / 111320

      const predLat = lat + distanceDeg * Math.cos(headingRad)
      const predLng = lng + (distanceDeg * Math.sin(headingRad)) / Math.cos((lat * Math.PI) / 180)

      lineFeatures.push({
        type: 'Feature',
        geometry: {
          type: 'LineString',
          coordinates: [
            [lng, lat],
            [predLng, predLat],
          ],
        },
        properties: { entityId: track.properties.entityId },
      })

      dotFeatures.push({
        type: 'Feature',
        geometry: {
          type: 'Point',
          coordinates: [predLng, predLat],
        },
        properties: { entityId: track.properties.entityId },
      })
    }

    lineSource.setData({ type: 'FeatureCollection', features: lineFeatures })
    dotSource.setData({ type: 'FeatureCollection', features: dotFeatures })
  }, [map, tracks, visible, predictionMinutes])

  return null
}
