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
import { MapIntelligenceLayer } from './MapIntelligenceLayer'
import { StructurePlacer } from './StructurePlacer'
import { StructureConfigPanel } from './StructureConfigPanel'
import { LayerControlPanel } from './LayerControlPanel'
import { PredictionLayer } from './PredictionLayer'
import { ExportButton } from './ExportButton'
import { TrackReplayLayer } from '../timeline/TrackReplayLayer'
import { TimelineScrubber } from '../timeline/TimelineScrubber'
import { apiFetch } from '../../lib/api'
import { useToast } from '../../contexts/ToastContext'
import type { TrackFeature, TrackProperties, GeofenceData, MapFeatureData, TrackPosition } from '../../types'

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
  mapFeatures?: MapFeatureData[]
  onMapFeatureCreated?: () => void
  replayEntityId: string | null
  onReplayOpen: (entityId: string) => void
  onReplayClose: () => void
}

export const MapContainer = forwardRef<MapContainerHandle, MapContainerProps>(
  function MapContainer({ tracks, trackHistory, geofences = [], onGeofenceCreated, mapFeatures = [], onMapFeatureCreated, replayEntityId, onReplayOpen, onReplayClose }, ref) {
    const { showToast } = useToast()
    const mapContainerRef = useRef<HTMLDivElement>(null)
    const [map, setMap] = useState<maplibregl.Map | null>(null)
    const [selectedEntity, setSelectedEntity] = useState<TrackProperties | null>(null)
    const [drawMode, setDrawMode] = useState<'polygon' | 'circle' | null>(null)
    const [editingGeofence, setEditingGeofence] = useState<{ id: string; name: string; fenceType: string; color: string } | null>(null)
    // Refs for mode state — accessible from map click handlers (stale closure issue)
    const drawModeRef = useRef(drawMode)
    const placingStructureRef = useRef(false)

    // Replay state
    const [replayTrackData, setReplayTrackData] = useState<TrackPosition[]>([])
    const [replayTime, setReplayTime] = useState<Date | null>(null)

    const [layerVisibility, setLayerVisibility] = useState<Record<string, boolean>>({
      vessels: true,
      aircraft: true,
      trails: false,
      geofences: true,
      airspace: true,
      baseStations: true,
      aidsToNav: false,
      airports: true,
      military: true,
      structures: true,
      predictions: false,
    })
    const [layerPanelOpen, setLayerPanelOpen] = useState(false)
    const [placingStructure, setPlacingStructure] = useState(false)
    const [structurePosition, setStructurePosition] = useState<[number, number] | null>(null)
    const mapRef = useRef<maplibregl.Map | null>(null)
    const tracksRef = useRef<TrackFeature[]>(tracks)

    // Keep refs current so map click handlers can access latest state
    useEffect(() => { tracksRef.current = tracks }, [tracks])
    useEffect(() => { drawModeRef.current = drawMode }, [drawMode])
    useEffect(() => { placingStructureRef.current = placingStructure }, [placingStructure])

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
          if (drawModeRef.current || placingStructureRef.current) return
          if (e.features?.[0]) {
            setSelectedEntity(e.features[0].properties as unknown as TrackProperties)
          }
        })
        m.on('click', 'aviation-track-symbols', (e) => {
          if (drawModeRef.current || placingStructureRef.current) return
          if (e.features?.[0]) {
            setSelectedEntity(e.features[0].properties as unknown as TrackProperties)
          }
        })
        m.on('mouseenter', 'maritime-track-symbols', () => { m.getCanvas().style.cursor = 'pointer' })
        m.on('mouseleave', 'maritime-track-symbols', () => { m.getCanvas().style.cursor = '' })
        m.on('mouseenter', 'aviation-track-symbols', () => { m.getCanvas().style.cursor = 'pointer' })
        m.on('mouseleave', 'aviation-track-symbols', () => { m.getCanvas().style.cursor = '' })

        m.on('click', 'geofence-fill', (e) => {
          if (drawModeRef.current || placingStructureRef.current) return
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

    // Sync vessel layer visibility
    useEffect(() => {
      if (!map) return
      if (!map.getLayer('maritime-track-symbols')) return
      map.setLayoutProperty('maritime-track-symbols', 'visibility', layerVisibility.vessels ? 'visible' : 'none')
    }, [map, layerVisibility.vessels])

    // Sync aircraft layer visibility
    useEffect(() => {
      if (!map) return
      if (!map.getLayer('aviation-track-symbols')) return
      map.setLayoutProperty('aviation-track-symbols', 'visibility', layerVisibility.aircraft ? 'visible' : 'none')
    }, [map, layerVisibility.aircraft])

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
        showToast('GEOFENCE CREATED', 'success')
      } catch (err) {
        showToast('FAILED TO CREATE GEOFENCE', 'error')
      }
    }, [onGeofenceCreated, showToast])

    const handleDrawCancel = useCallback(() => {
      setDrawMode(null)
    }, [])

    const handleStructurePlace = useCallback((lng: number, lat: number) => {
      setPlacingStructure(false)
      setStructurePosition([lng, lat])
    }, [])

    const handleStructurePlaceCancel = useCallback(() => {
      setPlacingStructure(false)
      setStructurePosition(null)
    }, [])

    const handleStructureSave = useCallback(async (
      name: string,
      featureType: string,
      color: string,
      details: string,
    ) => {
      if (!structurePosition) return
      const [lng, lat] = structurePosition

      try {
        await apiFetch('/api/v1/map-features', {
          method: 'POST',
          body: JSON.stringify({
            name,
            featureType: 'CustomStructure',
            longitude: lng,
            latitude: lat,
            color,
            details: `${featureType}${details ? ': ' + details : ''}`,
            source: 'UserPlaced',
          }),
        })
        onMapFeatureCreated?.()
        showToast('STRUCTURE PLACED', 'success')
      } catch (err) {
        showToast('FAILED TO PLACE STRUCTURE', 'error')
      }

      setStructurePosition(null)
    }, [structurePosition, onMapFeatureCreated, showToast])

    const handleLayerChange = useCallback((layer: string, visible: boolean) => {
      setLayerVisibility(prev => ({ ...prev, [layer]: visible }))
    }, [])

    return (
      <div ref={mapContainerRef} className="h-full w-full relative">
        {map && (
          <TrackHistoryLayer
            map={map}
            trackHistory={trackHistory}
            tracks={tracks}
            visible={layerVisibility.trails}
          />
        )}
        {map && <MaritimeTrackLayer map={map} tracks={tracks} />}
        {map && <AviationTrackLayer map={map} tracks={tracks} />}
        {map && layerVisibility.geofences && (
          <GeofenceLayer map={map} geofences={geofences} airspaceVisible={layerVisibility.airspace !== false} />
        )}
        {map && (
          <MapIntelligenceLayer
            map={map}
            features={mapFeatures}
            layerVisibility={layerVisibility}
          />
        )}
        {map && (
          <GeofenceDrawer
            map={map}
            mode={drawMode}
            onComplete={handleDrawComplete}
            onCancel={handleDrawCancel}
          />
        )}
        {map && (
          <StructurePlacer
            map={map}
            active={placingStructure}
            onPlace={handleStructurePlace}
            onCancel={handleStructurePlaceCancel}
          />
        )}
        {map && (
          <PredictionLayer
            map={map}
            tracks={tracks}
            visible={layerVisibility.predictions === true}
          />
        )}
        {map && (
          <TrackReplayLayer
            map={map}
            trackData={replayTrackData}
            currentTime={replayTime}
            visible={replayEntityId !== null}
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
                showToast('GEOFENCE UPDATED', 'success')
              } catch (err) {
                showToast('FAILED TO UPDATE GEOFENCE', 'error')
              }
            }}
            onCancel={() => setEditingGeofence(null)}
            onDelete={async () => {
              try {
                await apiFetch(`/api/v1/geofences/${editingGeofence.id}`, { method: 'DELETE' })
                setEditingGeofence(null)
                onGeofenceCreated?.()
                showToast('GEOFENCE DELETED', 'success')
              } catch (err) {
                showToast('FAILED TO DELETE GEOFENCE', 'error')
              }
            }}
          />
        )}
        {structurePosition && (
          <StructureConfigPanel
            position={structurePosition}
            onSave={handleStructureSave}
            onCancel={() => setStructurePosition(null)}
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
            onToggleTrails={() => setLayerVisibility(prev => ({ ...prev, trails: !prev.trails }))}
            onReplay={() => {
              onReplayOpen(selectedEntity.entityId)
              setSelectedEntity(null)
            }}
          />
        )}

        {/* Toolbar */}
        <div className="absolute top-3 left-3 z-10 flex flex-col gap-1">
          {/* LAYERS toggle */}
          <button
            onClick={() => setLayerPanelOpen(v => !v)}
            className={`px-2 py-1 font-mono text-xs tracking-widest border transition-colors ${
              layerPanelOpen
                ? 'bg-slate-700 border-slate-500 text-slate-200'
                : 'bg-slate-900 border-slate-700 text-slate-500 hover:text-slate-300'
            }`}
            style={{ borderRadius: '2px' }}
          >
            LAYERS
          </button>

          {/* Geofence draw buttons */}
          <div className="flex flex-col gap-1 mt-1">
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
            <button
              onClick={() => {
                setPlacingStructure(true)
                setStructurePosition(null)
              }}
              title="Place a custom structure"
              className={`px-2 py-1 font-mono text-xs tracking-widest border transition-colors ${
                placingStructure
                  ? 'bg-slate-600 border-slate-400 text-slate-100'
                  : 'bg-slate-900 border-slate-700 text-slate-500 hover:text-slate-300'
              }`}
              style={{ borderRadius: '2px' }}
            >
              ADD STRUCTURE
            </button>
          </div>

          {/* Layer control panel (inline below LAYERS button) */}
          {layerPanelOpen && (
            <div className="mt-1">
              <LayerControlPanel visibility={layerVisibility} onChange={handleLayerChange} />
            </div>
          )}

          {/* Export */}
          <div className="mt-1">
            <ExportButton />
          </div>
        </div>

        {/* Timeline Scrubber */}
        {replayEntityId && (
          <div className="absolute bottom-0 left-0 right-0 z-10">
            <TimelineScrubber
              visible={replayEntityId !== null}
              entityId={replayEntityId}
              onTimeChange={setReplayTime}
              onTrackData={setReplayTrackData}
              onClose={() => {
                onReplayClose()
                setReplayTrackData([])
                setReplayTime(null)
              }}
            />
          </div>
        )}
      </div>
    )
  }
)
