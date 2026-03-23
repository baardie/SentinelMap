import { useEffect } from 'react'
import maplibregl from 'maplibre-gl'
import type { GeofenceData } from '../../types'

const SOURCE_ID = 'geofence-polygons'
const FILL_LAYER_ID = 'geofence-fill'
const LINE_LAYER_ID = 'geofence-line'
const LABEL_LAYER_ID = 'geofence-label'

const DEFAULT_COLOR = '#f59e0b'

interface GeofenceLayerProps {
  map: maplibregl.Map
  geofences: GeofenceData[]
}

export function GeofenceLayer({ map, geofences }: GeofenceLayerProps) {
  // Register source and layers once
  useEffect(() => {
    if (map.getSource(SOURCE_ID)) return

    map.addSource(SOURCE_ID, {
      type: 'geojson',
      data: { type: 'FeatureCollection', features: [] },
    })

    // Fill using per-feature colour
    map.addLayer({
      id: FILL_LAYER_ID,
      type: 'fill',
      source: SOURCE_ID,
      paint: {
        'fill-color': ['coalesce', ['get', 'color'], DEFAULT_COLOR],
        'fill-opacity': 0.08,
      },
    })

    // Dashed outline using per-feature colour
    map.addLayer({
      id: LINE_LAYER_ID,
      type: 'line',
      source: SOURCE_ID,
      paint: {
        'line-color': ['coalesce', ['get', 'color'], DEFAULT_COLOR],
        'line-opacity': 0.6,
        'line-width': 2,
        'line-dasharray': [4, 4],
      },
    })

    // Geofence name label using per-feature colour
    map.addLayer({
      id: LABEL_LAYER_ID,
      type: 'symbol',
      source: SOURCE_ID,
      layout: {
        'text-field': ['get', 'name'],
        'text-font': ['Noto Sans Regular'],
        'text-size': 11,
        'text-anchor': 'center',
      },
      paint: {
        'text-color': ['coalesce', ['get', 'color'], DEFAULT_COLOR],
        'text-halo-color': '#0f172a',
        'text-halo-width': 1,
      },
    })

    return () => {
      if (map.getLayer(LABEL_LAYER_ID)) map.removeLayer(LABEL_LAYER_ID)
      if (map.getLayer(LINE_LAYER_ID)) map.removeLayer(LINE_LAYER_ID)
      if (map.getLayer(FILL_LAYER_ID)) map.removeLayer(FILL_LAYER_ID)
      if (map.getSource(SOURCE_ID)) map.removeSource(SOURCE_ID)
    }
  }, [map])

  // Update geofence data whenever the prop changes
  useEffect(() => {
    const source = map.getSource(SOURCE_ID) as maplibregl.GeoJSONSource | undefined
    if (!source) return

    const activeGeofences = geofences.filter(g => g.isActive)

    source.setData({
      type: 'FeatureCollection',
      features: activeGeofences.map(g => ({
        type: 'Feature' as const,
        geometry: {
          type: 'Polygon' as const,
          coordinates: [g.coordinates],
        },
        properties: {
          id: g.id,
          name: g.name,
          fenceType: g.fenceType,
          color: g.color || DEFAULT_COLOR,
        },
      })),
    })
  }, [map, geofences])

  return null
}
