import { useEffect, useRef } from 'react'
import maplibregl from 'maplibre-gl'
import type { TrackFeature } from '../../types'
import { VESSEL_ICON_DATA_URL } from './icons/vessel'

const SOURCE_ID = 'maritime-tracks'
const LAYER_ID = 'maritime-track-symbols'
const ICON_ID = 'vessel-icon'

const TYPE_COLOURS: Record<string, string> = {
  Cargo: '#94a3b8',
  Tanker: '#f59e0b',
  Passenger: '#2dd4bf',
  Fishing: '#a3e635',
  Unknown: '#64748b',
}

interface MaritimeTrackLayerProps {
  map: maplibregl.Map
  tracks: TrackFeature[]
}

export function MaritimeTrackLayer({ map, tracks }: MaritimeTrackLayerProps) {
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
    img.src = VESSEL_ICON_DATA_URL
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
        'icon-size': 0.8,
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
          'match', ['get', 'vesselType'],
          'Cargo', TYPE_COLOURS.Cargo,
          'Tanker', TYPE_COLOURS.Tanker,
          'Passenger', TYPE_COLOURS.Passenger,
          'Fishing', TYPE_COLOURS.Fishing,
          TYPE_COLOURS.Unknown,
        ],
        'icon-opacity': [
          'case',
          ['==', ['get', 'status'], 'Dark'], 0.3,
          ['interpolate', ['linear'], ['get', 'staleness'], 0, 1.0, 1, 0.3],
        ],
        'text-color': '#94a3b8',
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

    const vesselTracks = tracks.filter(t => t.properties.entityType === 'Vessel')

    source.setData({
      type: 'FeatureCollection',
      features: vesselTracks,
    })
  }, [map, tracks])

  return null
}
