import { useEffect } from 'react'
import maplibregl from 'maplibre-gl'
import type { GeofenceData } from '../../types'

const USER_SOURCE_ID = 'geofence-polygons'
const USER_FILL_LAYER_ID = 'geofence-fill'
const USER_LINE_LAYER_ID = 'geofence-line'
const USER_LABEL_LAYER_ID = 'geofence-label'

const AIRSPACE_SOURCE_ID = 'airspace-polygons'
const AIRSPACE_FILL_LAYER_ID = 'airspace-fill'
const AIRSPACE_LINE_LAYER_ID = 'airspace-line'
const AIRSPACE_LABEL_LAYER_ID = 'airspace-label'

const DEFAULT_COLOR = '#f59e0b'

function isAirspace(g: GeofenceData): boolean {
  return g.fenceType.startsWith('Airspace-')
}

function isDanger(fenceType: string): boolean {
  return fenceType === 'Airspace-Danger' || fenceType === 'Airspace-Prohibited'
}

interface GeofenceLayerProps {
  map: maplibregl.Map
  geofences: GeofenceData[]
  airspaceVisible?: boolean
}

export function GeofenceLayer({ map, geofences, airspaceVisible = true }: GeofenceLayerProps) {
  // Register source and layers once
  useEffect(() => {
    // --- User geofences ---
    if (!map.getSource(USER_SOURCE_ID)) {
      map.addSource(USER_SOURCE_ID, {
        type: 'geojson',
        data: { type: 'FeatureCollection', features: [] },
      })
    }

    if (!map.getLayer(USER_FILL_LAYER_ID)) {
      map.addLayer({
        id: USER_FILL_LAYER_ID,
        type: 'fill',
        source: USER_SOURCE_ID,
        paint: {
          'fill-color': ['coalesce', ['get', 'color'], DEFAULT_COLOR],
          'fill-opacity': 0.08,
        },
      })
    }

    if (!map.getLayer(USER_LINE_LAYER_ID)) {
      map.addLayer({
        id: USER_LINE_LAYER_ID,
        type: 'line',
        source: USER_SOURCE_ID,
        paint: {
          'line-color': ['coalesce', ['get', 'color'], DEFAULT_COLOR],
          'line-opacity': 0.6,
          'line-width': 2,
          'line-dasharray': [4, 4],
        },
      })
    }

    if (!map.getLayer(USER_LABEL_LAYER_ID)) {
      map.addLayer({
        id: USER_LABEL_LAYER_ID,
        type: 'symbol',
        source: USER_SOURCE_ID,
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
    }

    // --- Airspace zones ---
    if (!map.getSource(AIRSPACE_SOURCE_ID)) {
      map.addSource(AIRSPACE_SOURCE_ID, {
        type: 'geojson',
        data: { type: 'FeatureCollection', features: [] },
      })
    }

    if (!map.getLayer(AIRSPACE_FILL_LAYER_ID)) {
      map.addLayer({
        id: AIRSPACE_FILL_LAYER_ID,
        type: 'fill',
        source: AIRSPACE_SOURCE_ID,
        paint: {
          'fill-color': ['coalesce', ['get', 'color'], '#3b82f6'],
          'fill-opacity': 0.12,
        },
      })
    }

    if (!map.getLayer(AIRSPACE_LINE_LAYER_ID)) {
      map.addLayer({
        id: AIRSPACE_LINE_LAYER_ID,
        type: 'line',
        source: AIRSPACE_SOURCE_ID,
        paint: {
          'line-color': ['coalesce', ['get', 'color'], '#3b82f6'],
          'line-opacity': 0.7,
          'line-width': 1.5,
          'line-dasharray': [4, 2],
        },
      })
    }

    if (!map.getLayer(AIRSPACE_LABEL_LAYER_ID)) {
      map.addLayer({
        id: AIRSPACE_LABEL_LAYER_ID,
        type: 'symbol',
        source: AIRSPACE_SOURCE_ID,
        layout: {
          'text-field': ['get', 'name'],
          'text-font': ['Noto Sans Regular'],
          'text-size': 10,
          'text-anchor': 'center',
          'text-allow-overlap': false,
        },
        paint: {
          'text-color': ['coalesce', ['get', 'color'], '#3b82f6'],
          'text-halo-color': '#0f172a',
          'text-halo-width': 1,
          'text-opacity': 0.8,
        },
      })
    }

    return () => {
      // User layers
      if (map.getLayer(USER_LABEL_LAYER_ID)) map.removeLayer(USER_LABEL_LAYER_ID)
      if (map.getLayer(USER_LINE_LAYER_ID)) map.removeLayer(USER_LINE_LAYER_ID)
      if (map.getLayer(USER_FILL_LAYER_ID)) map.removeLayer(USER_FILL_LAYER_ID)
      if (map.getSource(USER_SOURCE_ID)) map.removeSource(USER_SOURCE_ID)
      // Airspace layers
      if (map.getLayer(AIRSPACE_LABEL_LAYER_ID)) map.removeLayer(AIRSPACE_LABEL_LAYER_ID)
      if (map.getLayer(AIRSPACE_LINE_LAYER_ID)) map.removeLayer(AIRSPACE_LINE_LAYER_ID)
      if (map.getLayer(AIRSPACE_FILL_LAYER_ID)) map.removeLayer(AIRSPACE_FILL_LAYER_ID)
      if (map.getSource(AIRSPACE_SOURCE_ID)) map.removeSource(AIRSPACE_SOURCE_ID)
    }
  }, [map])

  // Update user geofence data
  useEffect(() => {
    const source = map.getSource(USER_SOURCE_ID) as maplibregl.GeoJSONSource | undefined
    if (!source) return

    const userGeofences = geofences.filter(g => g.isActive && !isAirspace(g))

    source.setData({
      type: 'FeatureCollection',
      features: userGeofences.map(g => ({
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

  // Update airspace data
  useEffect(() => {
    const source = map.getSource(AIRSPACE_SOURCE_ID) as maplibregl.GeoJSONSource | undefined
    if (!source) return

    const airspaceGeofences = geofences.filter(g => g.isActive && isAirspace(g))

    source.setData({
      type: 'FeatureCollection',
      features: airspaceGeofences.map(g => ({
        type: 'Feature' as const,
        geometry: {
          type: 'Polygon' as const,
          coordinates: [g.coordinates],
        },
        properties: {
          id: g.id,
          name: g.name,
          fenceType: g.fenceType,
          color: g.color || '#3b82f6',
          isDanger: isDanger(g.fenceType),
        },
      })),
    })
  }, [map, geofences])

  // Toggle airspace visibility
  useEffect(() => {
    const vis = airspaceVisible ? 'visible' : 'none'
    for (const layerId of [AIRSPACE_FILL_LAYER_ID, AIRSPACE_LINE_LAYER_ID, AIRSPACE_LABEL_LAYER_ID]) {
      if (map.getLayer(layerId)) {
        map.setLayoutProperty(layerId, 'visibility', vis)
      }
    }
  }, [map, airspaceVisible])

  return null
}
