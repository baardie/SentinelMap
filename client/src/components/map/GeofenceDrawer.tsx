import { useEffect, useRef, useCallback, useState } from 'react'
import maplibregl from 'maplibre-gl'
import turfCircle from '@turf/circle'
import { point as turfPoint } from '@turf/helpers'
import turfDistance from '@turf/distance'
import { GeofenceConfigPanel } from './GeofenceConfigPanel'

const DRAW_SOURCE = 'draw-preview'
const FILL_LAYER = 'draw-preview-fill'
const LINE_LAYER = 'draw-preview-line'
const VERTEX_LAYER = 'draw-preview-vertices'

interface GeofenceDrawerProps {
  map: maplibregl.Map
  mode: 'polygon' | 'circle' | null
  onComplete: (geometry: number[][], name: string, color: string, fenceType: string) => void
  onCancel: () => void
}

type DrawState =
  | 'idle'
  | 'polygon_drawing'
  | 'polygon_complete'
  | 'circle_placing'
  | 'circle_sizing'
  | 'circle_complete'
  | 'configuring'

export function GeofenceDrawer({ map, mode, onComplete, onCancel }: GeofenceDrawerProps) {
  const [drawState, setDrawState] = useState<DrawState>('idle')
  const [vertexCount, setVertexCount] = useState(0)
  const [radiusNm, setRadiusNm] = useState(0)
  const [cursorRadiusNm, setCursorRadiusNm] = useState<number | null>(null)
  const [cursorPos, setCursorPos] = useState<{ x: number; y: number } | null>(null)

  const pointsRef = useRef<number[][]>([])
  const circleCentreRef = useRef<number[] | null>(null)
  const finalGeometryRef = useRef<number[][]>([])
  const drawColorRef = useRef('#f59e0b')
  const stateRef = useRef<DrawState>('idle')

  // Keep stateRef in sync
  useEffect(() => {
    stateRef.current = drawState
  }, [drawState])

  const ensureSourceAndLayers = useCallback(() => {
    if (!map.getSource(DRAW_SOURCE)) {
      map.addSource(DRAW_SOURCE, {
        type: 'geojson',
        data: { type: 'FeatureCollection', features: [] },
      })
    }
    if (!map.getLayer(FILL_LAYER)) {
      map.addLayer({
        id: FILL_LAYER,
        type: 'fill',
        source: DRAW_SOURCE,
        filter: ['==', ['geometry-type'], 'Polygon'],
        paint: {
          'fill-color': ['coalesce', ['get', 'color'], '#f59e0b'],
          'fill-opacity': 0.15,
        },
      })
    }
    if (!map.getLayer(LINE_LAYER)) {
      map.addLayer({
        id: LINE_LAYER,
        type: 'line',
        source: DRAW_SOURCE,
        filter: ['any', ['==', ['geometry-type'], 'Polygon'], ['==', ['geometry-type'], 'LineString']],
        paint: {
          'line-color': ['coalesce', ['get', 'color'], '#f59e0b'],
          'line-width': 2,
        },
      })
    }
    if (!map.getLayer(VERTEX_LAYER)) {
      map.addLayer({
        id: VERTEX_LAYER,
        type: 'circle',
        source: DRAW_SOURCE,
        filter: ['==', ['geometry-type'], 'Point'],
        paint: {
          'circle-radius': 5,
          'circle-color': ['coalesce', ['get', 'color'], '#f59e0b'],
          'circle-stroke-color': '#0f172a',
          'circle-stroke-width': 2,
        },
      })
    }
  }, [map])

  const cleanupDrawLayers = useCallback(() => {
    if (map.getLayer(VERTEX_LAYER)) map.removeLayer(VERTEX_LAYER)
    if (map.getLayer(LINE_LAYER)) map.removeLayer(LINE_LAYER)
    if (map.getLayer(FILL_LAYER)) map.removeLayer(FILL_LAYER)
    if (map.getSource(DRAW_SOURCE)) map.removeSource(DRAW_SOURCE)
  }, [map])

  const updatePreview = useCallback((cursorCoord?: number[]) => {
    const source = map.getSource(DRAW_SOURCE) as maplibregl.GeoJSONSource | undefined
    if (!source) return

    const state = stateRef.current
    const features: GeoJSON.Feature[] = []
    const color = drawColorRef.current

    if (state === 'polygon_drawing' || state === 'polygon_complete') {
      const pts = pointsRef.current

      // Vertices
      for (const p of pts) {
        features.push({
          type: 'Feature',
          geometry: { type: 'Point', coordinates: p },
          properties: { color },
        })
      }

      if (pts.length >= 2) {
        const lineCoords = [...pts]
        if (cursorCoord && state === 'polygon_drawing') {
          lineCoords.push(cursorCoord)
        }
        if (state === 'polygon_complete' || pts.length >= 3) {
          // Close the polygon
          const ring = [...pts, pts[0]]
          features.push({
            type: 'Feature',
            geometry: { type: 'Polygon', coordinates: [ring] },
            properties: { color },
          })
        }
        if (state === 'polygon_drawing') {
          features.push({
            type: 'Feature',
            geometry: { type: 'LineString', coordinates: lineCoords },
            properties: { color },
          })
        }
      } else if (pts.length === 1 && cursorCoord) {
        features.push({
          type: 'Feature',
          geometry: { type: 'LineString', coordinates: [pts[0], cursorCoord] },
          properties: { color },
        })
      }
    } else if (state === 'circle_placing') {
      // Nothing yet - waiting for first click
    } else if (state === 'circle_sizing' || state === 'circle_complete') {
      const centre = circleCentreRef.current
      if (!centre) return

      features.push({
        type: 'Feature',
        geometry: { type: 'Point', coordinates: centre },
        properties: { color },
      })

      const radiusKm = state === 'circle_complete'
        ? radiusNmToKm(radiusNm)
        : cursorCoord
          ? distanceKm(centre, cursorCoord)
          : 0

      if (radiusKm > 0) {
        const centrePoint = turfPoint(centre)
        const circleFeature = turfCircle(centrePoint, radiusKm, { steps: 64, units: 'kilometers' })
        circleFeature.properties = { color }
        features.push(circleFeature)
      }
    }

    source.setData({ type: 'FeatureCollection', features })
  }, [map, radiusNm])

  const reset = useCallback(() => {
    pointsRef.current = []
    circleCentreRef.current = null
    finalGeometryRef.current = []
    setVertexCount(0)
    setRadiusNm(0)
    setCursorRadiusNm(null)
    setCursorPos(null)
    setDrawState('idle')
    map.getCanvas().style.cursor = ''
    cleanupDrawLayers()
  }, [map, cleanupDrawLayers])

  // Start drawing when mode changes
  useEffect(() => {
    if (!mode) {
      if (stateRef.current !== 'configuring') {
        reset()
      }
      return
    }

    reset()
    ensureSourceAndLayers()
    map.getCanvas().style.cursor = 'crosshair'

    if (mode === 'polygon') {
      setDrawState('polygon_drawing')
    } else if (mode === 'circle') {
      setDrawState('circle_placing')
    }
  }, [mode]) // eslint-disable-line react-hooks/exhaustive-deps

  // Map event handlers
  useEffect(() => {
    if (!mode) return

    const handleClick = (e: maplibregl.MapMouseEvent) => {
      const state = stateRef.current
      const lngLat = e.lngLat

      if (state === 'polygon_drawing') {
        pointsRef.current.push([lngLat.lng, lngLat.lat])
        setVertexCount(pointsRef.current.length)
        updatePreview()
      } else if (state === 'circle_placing') {
        circleCentreRef.current = [lngLat.lng, lngLat.lat]
        setDrawState('circle_sizing')
        updatePreview()
      } else if (state === 'circle_sizing') {
        const centre = circleCentreRef.current!
        const km = distanceKm(centre, [lngLat.lng, lngLat.lat])
        const nm = kmToNm(km)
        setRadiusNm(nm)

        // Generate final circle polygon
        const centrePoint = turfPoint(centre)
        const circleFeature = turfCircle(centrePoint, km, { steps: 64, units: 'kilometers' })
        const coords = circleFeature.geometry.coordinates[0]
        finalGeometryRef.current = coords

        setDrawState('circle_complete')
        map.getCanvas().style.cursor = ''
        setCursorRadiusNm(null)
        setCursorPos(null)
        updatePreview()

        // Transition to configuring
        setTimeout(() => setDrawState('configuring'), 50)
      }
    }

    const handleDblClick = (e: maplibregl.MapMouseEvent) => {
      const state = stateRef.current
      if (state !== 'polygon_drawing') return

      e.preventDefault()

      if (pointsRef.current.length < 3) return

      // Close polygon
      finalGeometryRef.current = [...pointsRef.current, pointsRef.current[0]]
      setDrawState('polygon_complete')
      map.getCanvas().style.cursor = ''
      updatePreview()

      setTimeout(() => setDrawState('configuring'), 50)
    }

    const handleMouseMove = (e: maplibregl.MapMouseEvent) => {
      const state = stateRef.current
      const coord = [e.lngLat.lng, e.lngLat.lat]

      if (state === 'polygon_drawing') {
        updatePreview(coord)
      } else if (state === 'circle_sizing') {
        const centre = circleCentreRef.current!
        const km = distanceKm(centre, coord)
        const nm = kmToNm(km)
        setCursorRadiusNm(nm)
        setCursorPos({ x: e.point.x, y: e.point.y })
        updatePreview(coord)
      }
    }

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        reset()
        onCancel()
      }
    }

    map.on('click', handleClick)
    map.on('dblclick', handleDblClick)
    map.on('mousemove', handleMouseMove)
    document.addEventListener('keydown', handleKeyDown)

    return () => {
      map.off('click', handleClick)
      map.off('dblclick', handleDblClick)
      map.off('mousemove', handleMouseMove)
      document.removeEventListener('keydown', handleKeyDown)
    }
  }, [mode, map, updatePreview, reset, onCancel])

  const handleConfigSave = (name: string, color: string, fenceType: string) => {
    const geometry = finalGeometryRef.current
    onComplete(geometry, name, color, fenceType)
    reset()
  }

  const handleConfigCancel = () => {
    reset()
    onCancel()
  }

  return (
    <>
      {/* Vertex count indicator during polygon drawing */}
      {drawState === 'polygon_drawing' && vertexCount > 0 && (
        <div
          className="absolute top-14 left-3 z-10 px-2 py-1 bg-slate-900/90 border border-slate-700 font-mono text-xs text-slate-300 tracking-widest"
          style={{ borderRadius: '2px' }}
        >
          VERTICES: {vertexCount} {vertexCount < 3 ? '(MIN 3)' : '— DBL-CLICK TO CLOSE'}
        </div>
      )}

      {/* Radius indicator during circle sizing */}
      {cursorRadiusNm != null && cursorPos && (
        <div
          className="absolute z-10 px-2 py-1 bg-slate-900/90 border border-slate-700 font-mono text-xs text-slate-300 tracking-widest pointer-events-none"
          style={{
            left: cursorPos.x + 16,
            top: cursorPos.y - 12,
            borderRadius: '2px',
          }}
        >
          {cursorRadiusNm.toFixed(2)} NM
        </div>
      )}

      {/* Config panel after drawing complete */}
      {drawState === 'configuring' && (
        <GeofenceConfigPanel
          geometry={finalGeometryRef.current}
          radiusNm={circleCentreRef.current ? radiusNm : undefined}
          onSave={handleConfigSave}
          onCancel={handleConfigCancel}
        />
      )}
    </>
  )
}

/* Utility functions */
function distanceKm(a: number[], b: number[]): number {
  return turfDistance(turfPoint(a), turfPoint(b), { units: 'kilometers' })
}

function kmToNm(km: number): number {
  return km / 1.852
}

function radiusNmToKm(nm: number): number {
  return nm * 1.852
}
