import { useEffect, useRef } from 'react'
import maplibregl from 'maplibre-gl'
import 'maplibre-gl/dist/maplibre-gl.css'
import { Protocol } from 'pmtiles'
import { layers, namedTheme } from 'protomaps-themes-base'

const protocol = new Protocol()
maplibregl.addProtocol('pmtiles', (request) => protocol.tile(request))

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
    layers: [
      { id: 'background', type: 'background', paint: { 'background-color': '#0f172a' } },
      ...layers('basemap', namedTheme('dark')) as maplibregl.LayerSpecification[],
    ],
  }
}

interface MapContainerProps {
  onMapReady?: (map: maplibregl.Map) => void
}

export function MapContainer({ onMapReady }: MapContainerProps) {
  const mapContainerRef = useRef<HTMLDivElement>(null)
  const mapRef = useRef<maplibregl.Map | null>(null)

  useEffect(() => {
    if (!mapContainerRef.current || mapRef.current) return

    const map = new maplibregl.Map({
      container: mapContainerRef.current,
      style: buildMapStyle(),
      center: [1.0, 51.0],
      zoom: 7,
    })

    map.addControl(new maplibregl.NavigationControl(), 'bottom-right')
    mapRef.current = map

    map.on('load', () => {
      onMapReady?.(map)
    })

    return () => {
      mapRef.current?.remove()
      mapRef.current = null
    }
  }, [onMapReady])

  return <div ref={mapContainerRef} className="h-full w-full" />
}
