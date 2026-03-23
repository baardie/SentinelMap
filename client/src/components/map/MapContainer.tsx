import { useEffect, useRef, useState, forwardRef, useImperativeHandle, useCallback } from 'react'
import maplibregl from 'maplibre-gl'
import 'maplibre-gl/dist/maplibre-gl.css'
import { Protocol } from 'pmtiles'
import { layers, namedTheme } from 'protomaps-themes-base'
import { MaritimeTrackLayer } from './MaritimeTrackLayer'
import { AviationTrackLayer } from './AviationTrackLayer'
import { GeofenceLayer } from './GeofenceLayer'
import { TrackHistoryLayer } from './TrackHistoryLayer'
import { EntityDetailPanel } from './EntityDetailPanel'
import { GeofenceDrawer } from './GeofenceDrawer'
import { GeofenceConfigPanel } from './GeofenceConfigPanel'
import { apiFetch } from '../../lib/api'
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
  trackHistory: Map<string, [number, number][]>
  geofences?: GeofenceData[]
  onGeofenceCreated?: () => void
}

export const MapContainer = forwardRef<MapContainerHandle, MapContainerProps>(
  function MapContainer({ tracks, trackHistory, geofences = [], onGeofenceCreated }, ref) {
    const mapContainerRef = useRef<HTMLDivElement>(null)
    const [map, setMap] = useState<maplibregl.Map | null>(null)
    const [selectedEntity, setSelectedEntity] = useState<TrackProperties | null>(null)
    const [trailsVisible, setTrailsVisible] = useState(true)
    const [drawMode, setDrawMode] = useState<'polygon' | 'circle' | null>(null)
    const [editingGeofence, setEditingGeofence] = useState<{ id: string; name: string; fenceType: string; color: string } | null>(null)
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

        m.on('click', 'geofence-fill', (e) => {
          if (drawMode) return
          if (e.features?.[0]) {
            const trackFeatures = m.queryRenderedFeatures(e.point, { layers: ['maritime-track-symbols', 'aviation-track-symbols'] })
            if (trackFeatures.length > 0) return
            const props = e.features[0].properties
            setEditingGeofence({
              id: props.id,
              name: props.name,
              fenceType: props.fenceType,
              color: props.color,
            })
          }
        })
        m.on('mouseenter', 'geofence-fill', () => { m.getCanvas().style.cursor = 'pointer' })
        m.on('mouseleave', 'geofence-fill', () => { m.getCanvas().style.cursor = '' })

        setMap(m)
      })

      return () => {
        m.remove()
        mapRef.current = null
        setMap(null)
      }
    }, []) // eslint-disable-line react-hooks/exhaustive-deps

    const handleDrawComplete = useCallback(async (
      geometry: number[][],
      name: string,
      color: string,
      fenceType: string,
    ) => {
      setDrawMode(null)

      try {
        await apiFetch('/api/v1/geofences', {
          method: 'POST',
          body: JSON.stringify({
            name,
            coordinates: geometry,
            fenceType,
            color,
          }),
        })
        onGeofenceCreated?.()
      } catch {
        // Silently handle — in production this would show an error toast
      }
    }, [onGeofenceCreated])

    const handleDrawCancel = useCallback(() => {
      setDrawMode(null)
    }, [])

    return (
      <div ref={mapContainerRef} className="h-full w-full relative">
        {map && (
          <TrackHistoryLayer
            map={map}
            trackHistory={trackHistory}
            tracks={tracks}
            visible={trailsVisible}
          />
        )}
        {map && <MaritimeTrackLayer map={map} tracks={tracks} />}
        {map && <AviationTrackLayer map={map} tracks={tracks} />}
        {map && <GeofenceLayer map={map} geofences={geofences} />}
        {map && (
          <GeofenceDrawer
            map={map}
            mode={drawMode}
            onComplete={handleDrawComplete}
            onCancel={handleDrawCancel}
          />
        )}
        {editingGeofence && (
          <GeofenceConfigPanel
            geometry={[]}
            editMode
            initialName={editingGeofence.name}
            initialColor={editingGeofence.color}
            initialFenceType={editingGeofence.fenceType}
            onSave={async (name, color, fenceType) => {
              try {
                await apiFetch(`/api/v1/geofences/${editingGeofence.id}`, {
                  method: 'PUT',
                  body: JSON.stringify({ name, coordinates: [], fenceType, color }),
                })
                setEditingGeofence(null)
                onGeofenceCreated?.()
              } catch {
                // Silently handle — in production this would show an error toast
              }
            }}
            onCancel={() => setEditingGeofence(null)}
            onDelete={async () => {
              try {
                await apiFetch(`/api/v1/geofences/${editingGeofence.id}`, { method: 'DELETE' })
                setEditingGeofence(null)
                onGeofenceCreated?.()
              } catch {
                // Silently handle — in production this would show an error toast
              }
            }}
          />
        )}
        {selectedEntity && (
          <EntityDetailPanel
            entity={selectedEntity}
            onClose={() => setSelectedEntity(null)}
            onCreateGeofence={() => {
              setDrawMode('circle')
              setSelectedEntity(null)
            }}
            onToggleTrails={() => setTrailsVisible(v => !v)}
          />
        )}
        {/* Trails toggle button */}
        <button
          onClick={() => setTrailsVisible(v => !v)}
          title={trailsVisible ? 'Hide Trails' : 'Show Trails'}
          className={`absolute top-3 left-3 z-10 px-2 py-1 font-mono text-xs tracking-widest border transition-colors ${
            trailsVisible
              ? 'bg-slate-700 border-slate-500 text-slate-200'
              : 'bg-slate-900 border-slate-700 text-slate-500'
          }`}
          style={{ borderRadius: '2px' }}
        >
          TRAILS
        </button>
        {/* Draw mode toolbar */}
        <div className="absolute top-10 left-3 z-10 flex flex-col gap-1 mt-2">
          <button
            onClick={() => setDrawMode(drawMode === 'polygon' ? null : 'polygon')}
            title="Draw polygon geofence"
            className={`px-2 py-1 font-mono text-xs tracking-widest border transition-colors ${
              drawMode === 'polygon'
                ? 'bg-slate-600 border-slate-400 text-slate-100'
                : 'bg-slate-900 border-slate-700 text-slate-500 hover:text-slate-300'
            }`}
            style={{ borderRadius: '2px' }}
          >
            POLYGON
          </button>
          <button
            onClick={() => setDrawMode(drawMode === 'circle' ? null : 'circle')}
            title="Draw circle geofence"
            className={`px-2 py-1 font-mono text-xs tracking-widest border transition-colors ${
              drawMode === 'circle'
                ? 'bg-slate-600 border-slate-400 text-slate-100'
                : 'bg-slate-900 border-slate-700 text-slate-500 hover:text-slate-300'
            }`}
            style={{ borderRadius: '2px' }}
          >
            CIRCLE
          </button>
        </div>
      </div>
    )
  }
)
