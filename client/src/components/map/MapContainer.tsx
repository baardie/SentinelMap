import { useEffect, useRef, useState, forwardRef, useImperativeHandle } from 'react'
import maplibregl from 'maplibre-gl'
import 'maplibre-gl/dist/maplibre-gl.css'
import { Protocol } from 'pmtiles'
import { layers, namedTheme } from 'protomaps-themes-base'
import { MaritimeTrackLayer } from './MaritimeTrackLayer'
import { AviationTrackLayer } from './AviationTrackLayer'
import { GeofenceLayer } from './GeofenceLayer'
import { EntityDetailPanel } from './EntityDetailPanel'
import type { TrackFeature, TrackProperties, GeofenceData } from '../../types'

const protocol = new Protocol()
maplibregl.addProtocol('pmtiles', protocol.tile)

const PMTILES_URL = '/tiles/basemap.pmtiles'

function buildMapStyle(): maplibregl.StyleSpecification {
  return {
    version: 8,
    glyphs: 'https://protomaps.github.io/basemaps-assets/fonts/{fontstack}/{range}.pbf',
    sprite: 'https://protomaps.github.io/basemaps-assets/sprites/v4/dark',
    sources: {
      basemap: {
        type: 'vector',
        url: `pmtiles://${PMTILES_URL}`,
        attribution: '© <a href="https://openstreetmap.org">OpenStreetMap</a>',
      },
    },
    layers: layers('basemap', namedTheme('dark')) as maplibregl.LayerSpecification[],
  }
}

export interface MapContainerHandle {
  flyToEntity: (entityId: string) => void
}

interface MapContainerProps {
  tracks: TrackFeature[]
  geofences?: GeofenceData[]
}

export const MapContainer = forwardRef<MapContainerHandle, MapContainerProps>(
  function MapContainer({ tracks, geofences = [] }, ref) {
    const mapContainerRef = useRef<HTMLDivElement>(null)
    const [map, setMap] = useState<maplibregl.Map | null>(null)
    const [selectedEntity, setSelectedEntity] = useState<TrackProperties | null>(null)
    const mapRef = useRef<maplibregl.Map | null>(null)
    const tracksRef = useRef<TrackFeature[]>(tracks)

    // Keep tracksRef current so flyToEntity closure can access latest tracks
    useEffect(() => {
      tracksRef.current = tracks
    }, [tracks])

    useImperativeHandle(ref, () => ({
      flyToEntity(entityId: string) {
        const track = tracksRef.current.find(t => t.properties.entityId === entityId)
        if (track && mapRef.current) {
          const [lng, lat] = track.geometry.coordinates
          mapRef.current.flyTo({ center: [lng, lat], zoom: 14, duration: 1200 })
          setSelectedEntity(track.properties)
        }
      },
    }))

    useEffect(() => {
      if (!mapContainerRef.current || mapRef.current) return

      const m = new maplibregl.Map({
        container: mapContainerRef.current,
        style: buildMapStyle(),
        center: [-3.02, 53.38],
        zoom: 12,
      })

      mapRef.current = m

      m.addControl(new maplibregl.NavigationControl(), 'bottom-right')

      m.on('load', () => {
        m.on('click', 'maritime-track-symbols', (e) => {
          if (e.features?.[0]) {
            setSelectedEntity(e.features[0].properties as unknown as TrackProperties)
          }
        })
        m.on('click', 'aviation-track-symbols', (e) => {
          if (e.features?.[0]) {
            setSelectedEntity(e.features[0].properties as unknown as TrackProperties)
          }
        })
        m.on('mouseenter', 'maritime-track-symbols', () => { m.getCanvas().style.cursor = 'pointer' })
        m.on('mouseleave', 'maritime-track-symbols', () => { m.getCanvas().style.cursor = '' })
        m.on('mouseenter', 'aviation-track-symbols', () => { m.getCanvas().style.cursor = 'pointer' })
        m.on('mouseleave', 'aviation-track-symbols', () => { m.getCanvas().style.cursor = '' })

        setMap(m)
      })

      return () => {
        m.remove()
        mapRef.current = null
        setMap(null)
      }
    }, []) // eslint-disable-line react-hooks/exhaustive-deps

    return (
      <div ref={mapContainerRef} className="h-full w-full relative">
        {map && <MaritimeTrackLayer map={map} tracks={tracks} />}
        {map && <AviationTrackLayer map={map} tracks={tracks} />}
        {map && <GeofenceLayer map={map} geofences={geofences} />}
        {selectedEntity && (
          <EntityDetailPanel entity={selectedEntity} onClose={() => setSelectedEntity(null)} />
        )}
      </div>
    )
  }
)
