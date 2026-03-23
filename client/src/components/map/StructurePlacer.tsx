import { useEffect } from 'react'
import maplibregl from 'maplibre-gl'

interface StructurePlacerProps {
  map: maplibregl.Map
  active: boolean
  onPlace: (lng: number, lat: number) => void
  onCancel: () => void
}

export function StructurePlacer({ map, active, onPlace, onCancel }: StructurePlacerProps) {
  useEffect(() => {
    if (!active) return

    map.getCanvas().style.cursor = 'crosshair'

    const handleClick = (e: maplibregl.MapMouseEvent) => {
      onPlace(e.lngLat.lng, e.lngLat.lat)
    }

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        onCancel()
      }
    }

    map.on('click', handleClick)
    window.addEventListener('keydown', handleKeyDown)

    return () => {
      map.getCanvas().style.cursor = ''
      map.off('click', handleClick)
      window.removeEventListener('keydown', handleKeyDown)
    }
  }, [active, map, onPlace, onCancel])

  if (!active) return null

  return (
    <div
      className="absolute top-1/2 left-1/2 -translate-x-1/2 z-30 px-4 py-2 bg-slate-900 border border-slate-600 text-slate-200 text-xs font-mono tracking-widest uppercase pointer-events-none"
      style={{ borderRadius: '2px', transform: 'translate(-50%, -200%)' }}
    >
      CLICK TO PLACE STRUCTURE
    </div>
  )
}
