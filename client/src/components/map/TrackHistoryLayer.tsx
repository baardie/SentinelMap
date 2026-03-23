import { useEffect } from 'react'
import maplibregl from 'maplibre-gl'
import type { FeatureCollection, LineString } from 'geojson'
import type { TrackFeature } from '../../types'

const SOURCE_ID = 'track-history'
const LAYER_ID = 'track-history-lines'

interface TrackHistoryLayerProps {
  map: maplibregl.Map
  trackHistory: Map<string, [number, number][]>
  tracks: TrackFeature[]
  visible: boolean
}

export function TrackHistoryLayer({ map, trackHistory, tracks, visible }: TrackHistoryLayerProps) {
  // Set up source and layer once
  useEffect(() => {
    if (map.getSource(SOURCE_ID)) return

    map.addSource(SOURCE_ID, {
      type: 'geojson',
      data: { type: 'FeatureCollection', features: [] },
    })

    // Find the first symbol layer to place history lines beneath it
    const firstSymbolLayer = map.getStyle().layers?.find(l => l.type === 'symbol')?.id

    map.addLayer(
      {
        id: LAYER_ID,
        type: 'line',
        source: SOURCE_ID,
        layout: {
          'line-cap': 'round',
          'line-join': 'round',
          'visibility': 'visible',
        },
        paint: {
          'line-color': [
            'match',
            ['get', 'entityType'],
            'Vessel', '#64748b',
            'Aircraft', '#0ea5e9',
            '#64748b',
          ],
          'line-opacity': 0.4,
          'line-width': 1.5,
          'line-dasharray': [2, 2],
        },
        minzoom: 5,
      },
      firstSymbolLayer,
    )

    return () => {
      if (map.getLayer(LAYER_ID)) map.removeLayer(LAYER_ID)
      if (map.getSource(SOURCE_ID)) map.removeSource(SOURCE_ID)
    }
  }, [map])

  // Build a lookup map from entityId -> entityType
  useEffect(() => {
    const source = map.getSource(SOURCE_ID) as maplibregl.GeoJSONSource | undefined
    if (!source) return

    const entityTypeMap = new Map<string, string>()
    for (const track of tracks) {
      entityTypeMap.set(track.properties.entityId, track.properties.entityType)
    }

    const features: FeatureCollection<LineString>['features'] = []

    for (const [entityId, positions] of trackHistory) {
      if (positions.length < 2) continue
      features.push({
        type: 'Feature',
        geometry: {
          type: 'LineString',
          coordinates: positions,
        },
        properties: {
          entityId,
          entityType: entityTypeMap.get(entityId) ?? 'Unknown',
        },
      })
    }

    source.setData({ type: 'FeatureCollection', features })
  }, [map, trackHistory, tracks])

  // Toggle visibility
  useEffect(() => {
    if (!map.getLayer(LAYER_ID)) return
    map.setLayoutProperty(LAYER_ID, 'visibility', visible ? 'visible' : 'none')
  }, [map, visible])

  return null
}
