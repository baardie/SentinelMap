import { useEffect, useRef, useState } from 'react'
import maplibregl from 'maplibre-gl'
import 'maplibre-gl/dist/maplibre-gl.css'
import { Protocol } from 'pmtiles'
import { layers, namedTheme } from 'protomaps-themes-base'
import { MaritimeTrackLayer } from './MaritimeTrackLayer'
import { useTrackHub } from '../../hooks/useTrackHub'

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

export function MapContainer() {
  const mapContainerRef = useRef<HTMLDivElement>(null)
  const [map, setMap] = useState<maplibregl.Map | null>(null)
  const tracks = useTrackHub()

  useEffect(() => {
    if (!mapContainerRef.current || map) return

    const m = new maplibregl.Map({
      container: mapContainerRef.current,
      style: buildMapStyle(),
      center: [-3.02, 53.38],
      zoom: 12,
    })

    m.addControl(new maplibregl.NavigationControl(), 'bottom-right')

    m.on('load', () => {
      setMap(m)
    })

    return () => {
      m.remove()
      setMap(null)
    }
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <div ref={mapContainerRef} className="h-full w-full">
      {map && <MaritimeTrackLayer map={map} tracks={tracks} />}
    </div>
  )
}
