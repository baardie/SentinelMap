import { useEffect, useRef } from 'react'
import maplibregl from 'maplibre-gl'
import type { TrackFeature } from '../../types'
import { AIRCRAFT_ICON_DATA_URL } from './icons/aircraft'

const SOURCE_ID = 'aviation-tracks'
const LAYER_ID = 'aviation-track-symbols'
const ICON_ID = 'aircraft-icon'

interface AviationTrackLayerProps {
  map: maplibregl.Map
  tracks: TrackFeature[]
}

export function AviationTrackLayer({ map, tracks }: AviationTrackLayerProps) {
  const iconLoaded = useRef(false)

  useEffect(() => {
    if (iconLoaded.current || map.hasImage(ICON_ID)) {
      iconLoaded.current = true
      return
    }

    const img = new Image(24, 32)
    img.onload = () => {
      if (!map.hasImage(ICON_ID)) {
        map.addImage(ICON_ID, img, { sdf: true })
        iconLoaded.current = true
      }
    }
    img.src = AIRCRAFT_ICON_DATA_URL
  }, [map])

  useEffect(() => {
    if (map.getSource(SOURCE_ID)) return

    map.addSource(SOURCE_ID, {
      type: 'geojson',
      data: { type: 'FeatureCollection', features: [] },
    })

    map.addLayer({
      id: LAYER_ID,
      type: 'symbol',
      source: SOURCE_ID,
      minzoom: 5,
      layout: {
        'icon-image': ICON_ID,
        'icon-size': 0.7,
        'icon-rotate': ['get', 'heading'],
        'icon-rotation-alignment': 'map',
        'icon-allow-overlap': true,
        'text-field': ['get', 'displayName'],
        'text-font': ['Noto Sans Regular'],
        'text-size': 10,
        'text-offset': [0, 1.5],
        'text-optional': true,
      },
      paint: {
        'icon-color': [
          'case',
          // Emergency aircraft — red
          ['all',
            ['has', 'emergency'],
            ['!=', ['get', 'emergency'], 'none'],
            ['!=', ['get', 'emergency'], ''],
          ],
          '#ef4444',
          // Military aircraft — orange
          ['==', ['get', 'isMilitary'], true],
          '#f97316',
          // Default — sky blue
          '#0ea5e9',
        ],
        'icon-opacity': [
          'case',
          ['==', ['get', 'status'], 'Dark'], 0.3,
          ['interpolate', ['linear'], ['get', 'staleness'], 0, 1.0, 1, 0.3],
        ],
        'text-color': [
          'case',
          ['all',
            ['has', 'emergency'],
            ['!=', ['get', 'emergency'], 'none'],
            ['!=', ['get', 'emergency'], ''],
          ],
          '#ef4444',
          ['==', ['get', 'isMilitary'], true],
          '#f97316',
          '#0ea5e9',
        ],
        'text-halo-color': '#0f172a',
        'text-halo-width': 1,
      },
    })

    return () => {
      if (map.getLayer(LAYER_ID)) map.removeLayer(LAYER_ID)
      if (map.getSource(SOURCE_ID)) map.removeSource(SOURCE_ID)
    }
  }, [map])

  useEffect(() => {
    const source = map.getSource(SOURCE_ID) as maplibregl.GeoJSONSource | undefined
    if (!source) return

    const aircraftTracks = tracks.filter(t => t.properties.entityType === 'Aircraft')

    source.setData({
      type: 'FeatureCollection',
      features: aircraftTracks,
    })
  }, [map, tracks])

  return null
}
