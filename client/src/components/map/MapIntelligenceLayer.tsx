import { useEffect, useRef } from 'react'
import maplibregl from 'maplibre-gl'
import type { MapFeatureData } from '../../types'
import { AIRPORT_ICON_DATA_URL } from './icons/airport'
import { MILITARY_ICON_DATA_URL } from './icons/military'
import { BASE_STATION_ICON_DATA_URL } from './icons/baseStation'
import { BUOY_ICON_DATA_URL } from './icons/buoy'
import { STRUCTURE_ICON_DATA_URL } from './icons/structure'

interface FeatureConfig {
  featureType: string
  sourceId: string
  layerId: string
  iconId: string
  iconUrl: string
  iconWidth: number
  iconHeight: number
  defaultColor: string
  iconSize: number
  visibilityKey: string
}

const FEATURE_CONFIGS: FeatureConfig[] = [
  {
    featureType: 'AisBaseStation',
    sourceId: 'intel-base-stations',
    layerId: 'intel-base-station-symbols',
    iconId: 'base-station-icon',
    iconUrl: BASE_STATION_ICON_DATA_URL,
    iconWidth: 24,
    iconHeight: 24,
    defaultColor: '#8b5cf6',
    iconSize: 0.6,
    visibilityKey: 'baseStations',
  },
  {
    featureType: 'AidToNavigation',
    sourceId: 'intel-aids-nav',
    layerId: 'intel-aids-nav-symbols',
    iconId: 'buoy-icon',
    iconUrl: BUOY_ICON_DATA_URL,
    iconWidth: 24,
    iconHeight: 24,
    defaultColor: '#06b6d4',
    iconSize: 0.5,
    visibilityKey: 'aidsToNav',
  },
  {
    featureType: 'Airport',
    sourceId: 'intel-airports',
    layerId: 'intel-airport-symbols',
    iconId: 'airport-icon',
    iconUrl: AIRPORT_ICON_DATA_URL,
    iconWidth: 24,
    iconHeight: 24,
    defaultColor: '#f97316',
    iconSize: 0.7,
    visibilityKey: 'airports',
  },
  {
    featureType: 'MilitaryBase',
    sourceId: 'intel-military',
    layerId: 'intel-military-symbols',
    iconId: 'military-icon',
    iconUrl: MILITARY_ICON_DATA_URL,
    iconWidth: 24,
    iconHeight: 24,
    defaultColor: '#ef4444',
    iconSize: 0.7,
    visibilityKey: 'military',
  },
  {
    featureType: 'CustomStructure',
    sourceId: 'intel-custom',
    layerId: 'intel-custom-symbols',
    iconId: 'structure-icon',
    iconUrl: STRUCTURE_ICON_DATA_URL,
    iconWidth: 24,
    iconHeight: 32,
    defaultColor: '#f59e0b',
    iconSize: 0.8,
    visibilityKey: 'structures',
  },
]

interface MapIntelligenceLayerProps {
  map: maplibregl.Map
  features: MapFeatureData[]
  layerVisibility: Record<string, boolean>
  onFeatureClick?: (feature: MapFeatureData) => void
}

export function MapIntelligenceLayer({
  map,
  features,
  layerVisibility,
  onFeatureClick,
}: MapIntelligenceLayerProps) {
  const iconsLoaded = useRef<Record<string, boolean>>({})
  const initializedConfigs = useRef<Set<string>>(new Set())

  // Load all icons once
  useEffect(() => {
    for (const cfg of FEATURE_CONFIGS) {
      if (iconsLoaded.current[cfg.iconId] || map.hasImage(cfg.iconId)) {
        iconsLoaded.current[cfg.iconId] = true
        continue
      }
      const img = new Image(cfg.iconWidth, cfg.iconHeight)
      const iconId = cfg.iconId
      img.onload = () => {
        if (!map.hasImage(iconId)) {
          map.addImage(iconId, img, { sdf: true })
          iconsLoaded.current[iconId] = true
        }
      }
      img.src = cfg.iconUrl
    }
  }, [map])

  // Set up sources and layers for each feature type
  useEffect(() => {
    for (const cfg of FEATURE_CONFIGS) {
      if (initializedConfigs.current.has(cfg.sourceId)) continue

      if (!map.getSource(cfg.sourceId)) {
        map.addSource(cfg.sourceId, {
          type: 'geojson',
          data: { type: 'FeatureCollection', features: [] },
        })
      }

      if (!map.getLayer(cfg.layerId)) {
        map.addLayer({
          id: cfg.layerId,
          type: 'symbol',
          source: cfg.sourceId,
          layout: {
            'icon-image': cfg.iconId,
            'icon-size': cfg.iconSize,
            'icon-allow-overlap': true,
            'text-field': ['get', 'name'],
            'text-font': ['Noto Sans Regular'],
            'text-size': 10,
            'text-offset': [0, 1.5],
            'text-anchor': 'top',
            'text-optional': true,
            'visibility': 'visible',
          },
          paint: {
            'icon-color': ['coalesce', ['get', 'color'], cfg.defaultColor],
            'icon-opacity': 1,
            'text-color': '#94a3b8',
            'text-halo-color': '#0f172a',
            'text-halo-width': 1,
          },
        })
      }

      initializedConfigs.current.add(cfg.sourceId)
    }

    // Register click handlers
    if (onFeatureClick) {
      for (const cfg of FEATURE_CONFIGS) {
        map.on('click', cfg.layerId, (e) => {
          if (e.features?.[0]) {
            const props = e.features[0].properties as {
              id: string; featureType: string; name: string
              longitude: number; latitude: number; icon: string | null
              color: string | null; details: string | null; source: string; isActive: boolean
            }
            onFeatureClick({
              id: props.id,
              featureType: props.featureType,
              name: props.name,
              longitude: props.longitude,
              latitude: props.latitude,
              icon: props.icon,
              color: props.color,
              details: props.details,
              source: props.source,
              isActive: props.isActive,
            })
          }
        })
        map.on('mouseenter', cfg.layerId, () => { map.getCanvas().style.cursor = 'pointer' })
        map.on('mouseleave', cfg.layerId, () => { map.getCanvas().style.cursor = '' })
      }
    }

    return () => {
      for (const cfg of FEATURE_CONFIGS) {
        if (map.getLayer(cfg.layerId)) map.removeLayer(cfg.layerId)
        if (map.getSource(cfg.sourceId)) map.removeSource(cfg.sourceId)
      }
      initializedConfigs.current.clear()
    }
  }, [map]) // eslint-disable-line react-hooks/exhaustive-deps

  // Update feature data whenever features prop changes
  useEffect(() => {
    for (const cfg of FEATURE_CONFIGS) {
      const source = map.getSource(cfg.sourceId) as maplibregl.GeoJSONSource | undefined
      if (!source) continue

      const typeFeatures = features.filter(f => f.featureType === cfg.featureType && f.isActive)

      source.setData({
        type: 'FeatureCollection',
        features: typeFeatures.map(f => ({
          type: 'Feature' as const,
          geometry: {
            type: 'Point' as const,
            coordinates: [f.longitude, f.latitude],
          },
          properties: {
            id: f.id,
            featureType: f.featureType,
            name: f.name,
            longitude: f.longitude,
            latitude: f.latitude,
            icon: f.icon,
            color: f.color,
            details: f.details,
            source: f.source,
            isActive: f.isActive,
          },
        })),
      })
    }
  }, [map, features])

  // Update layer visibility when layerVisibility prop changes
  useEffect(() => {
    for (const cfg of FEATURE_CONFIGS) {
      if (!map.getLayer(cfg.layerId)) continue
      const visible = layerVisibility[cfg.visibilityKey] !== false
      map.setLayoutProperty(cfg.layerId, 'visibility', visible ? 'visible' : 'none')
    }
  }, [map, layerVisibility])

  return null
}
