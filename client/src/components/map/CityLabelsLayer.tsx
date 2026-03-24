import { useEffect } from 'react'
import maplibregl from 'maplibre-gl'

const SOURCE_ID = 'city-labels'
const LAYER_ID = 'city-label-symbols'

interface CityLabel {
  name: string
  lng: number
  lat: number
  rank: number // 1 = major city, 2 = city, 3 = town
}

// Key UK + Ireland cities/towns for the COP
const CITIES: CityLabel[] = [
  // Major cities (rank 1) — always visible from zoom 6+
  { name: 'London', lng: -0.1278, lat: 51.5074, rank: 1 },
  { name: 'Birmingham', lng: -1.8904, lat: 52.4862, rank: 1 },
  { name: 'Manchester', lng: -2.2426, lat: 53.4808, rank: 1 },
  { name: 'Liverpool', lng: -2.9916, lat: 53.4084, rank: 1 },
  { name: 'Edinburgh', lng: -3.1883, lat: 55.9533, rank: 1 },
  { name: 'Glasgow', lng: -4.2518, lat: 55.8642, rank: 1 },
  { name: 'Dublin', lng: -6.2603, lat: 53.3498, rank: 1 },
  { name: 'Belfast', lng: -5.9301, lat: 54.5973, rank: 1 },
  { name: 'Cardiff', lng: -3.1791, lat: 51.4816, rank: 1 },
  { name: 'Leeds', lng: -1.5491, lat: 53.8008, rank: 1 },

  // Cities (rank 2) — visible from zoom 8+
  { name: 'Bristol', lng: -2.5879, lat: 51.4545, rank: 2 },
  { name: 'Newcastle', lng: -1.6178, lat: 54.9783, rank: 2 },
  { name: 'Sheffield', lng: -1.4701, lat: 53.3811, rank: 2 },
  { name: 'Nottingham', lng: -1.1581, lat: 52.9548, rank: 2 },
  { name: 'Southampton', lng: -1.4044, lat: 50.9097, rank: 2 },
  { name: 'Plymouth', lng: -4.1427, lat: 50.3755, rank: 2 },
  { name: 'Aberdeen', lng: -2.0943, lat: 57.1497, rank: 2 },
  { name: 'Inverness', lng: -4.2246, lat: 57.4778, rank: 2 },
  { name: 'Cork', lng: -8.4863, lat: 51.8985, rank: 2 },
  { name: 'Galway', lng: -9.0568, lat: 53.2707, rank: 2 },
  { name: 'Swansea', lng: -3.9436, lat: 51.6214, rank: 2 },
  { name: 'Dundee', lng: -2.9707, lat: 56.4620, rank: 2 },
  { name: 'Stoke-on-Trent', lng: -2.1753, lat: 53.0027, rank: 2 },
  { name: 'Coventry', lng: -1.5197, lat: 52.4068, rank: 2 },
  { name: 'Leicester', lng: -1.1398, lat: 52.6369, rank: 2 },
  { name: 'Portsmouth', lng: -1.0880, lat: 50.8198, rank: 2 },
  { name: 'Sunderland', lng: -1.3812, lat: 54.9069, rank: 2 },
  { name: 'Brighton', lng: -0.1313, lat: 50.8225, rank: 2 },
  { name: 'Hull', lng: -0.3274, lat: 53.7676, rank: 2 },
  { name: 'Preston', lng: -2.7074, lat: 53.7632, rank: 2 },

  // Towns near Liverpool (rank 3) — visible from zoom 10+
  { name: 'Birkenhead', lng: -3.0148, lat: 53.3934, rank: 3 },
  { name: 'Chester', lng: -2.8909, lat: 53.1931, rank: 3 },
  { name: 'Warrington', lng: -2.5970, lat: 53.3900, rank: 3 },
  { name: 'Southport', lng: -3.0053, lat: 53.6475, rank: 3 },
  { name: 'Blackpool', lng: -3.0503, lat: 53.8142, rank: 3 },
  { name: 'Wigan', lng: -2.6325, lat: 53.5448, rank: 3 },
  { name: 'St Helens', lng: -2.7355, lat: 53.4534, rank: 3 },
  { name: 'Runcorn', lng: -2.7335, lat: 53.3419, rank: 3 },
  { name: 'Ellesmere Port', lng: -2.9014, lat: 53.2779, rank: 3 },
  { name: 'Bootle', lng: -2.9891, lat: 53.4457, rank: 3 },
  { name: 'Wallasey', lng: -3.0590, lat: 53.4241, rank: 3 },
  { name: 'Crosby', lng: -3.0340, lat: 53.4872, rank: 3 },
  { name: 'Formby', lng: -3.0594, lat: 53.5566, rank: 3 },
  { name: 'New Brighton', lng: -3.0505, lat: 53.4396, rank: 3 },
  { name: 'Hoylake', lng: -3.1820, lat: 53.3897, rank: 3 },
  { name: 'Bromborough', lng: -2.9874, lat: 53.3321, rank: 3 },
  { name: 'Douglas', lng: -4.4833, lat: 54.1509, rank: 3 },
  { name: 'Barrow-in-Furness', lng: -3.2267, lat: 54.1109, rank: 3 },
  { name: 'Lancaster', lng: -2.8007, lat: 54.0466, rank: 3 },
  { name: 'Fleetwood', lng: -3.0128, lat: 53.9226, rank: 3 },
]

interface CityLabelsLayerProps {
  map: maplibregl.Map
  visible?: boolean
}

export function CityLabelsLayer({ map, visible = true }: CityLabelsLayerProps) {
  useEffect(() => {
    if (map.getSource(SOURCE_ID)) return

    map.addSource(SOURCE_ID, {
      type: 'geojson',
      data: {
        type: 'FeatureCollection',
        features: CITIES.map(c => ({
          type: 'Feature' as const,
          geometry: { type: 'Point' as const, coordinates: [c.lng, c.lat] },
          properties: { name: c.name, rank: c.rank },
        })),
      },
    })

    map.addLayer({
      id: LAYER_ID,
      type: 'symbol',
      source: SOURCE_ID,
      layout: {
        'text-field': ['get', 'name'],
        'text-font': ['Noto Sans Regular'],
        'text-size': [
          'match', ['get', 'rank'],
          1, 14,
          2, 11,
          3, 9,
          9,
        ],
        'text-anchor': 'center',
        'text-allow-overlap': false,
        'text-padding': 4,
        // Show major cities from zoom 6, cities from 8, towns from 10
        'text-variable-anchor': ['center', 'top', 'bottom', 'left', 'right'],
      },
      paint: {
        'text-color': [
          'match', ['get', 'rank'],
          1, '#cbd5e1',  // slate-300 for major cities
          2, '#94a3b8',  // slate-400 for cities
          '#64748b',     // slate-500 for towns
        ],
        'text-halo-color': '#0f172a',
        'text-halo-width': 1.5,
        'text-opacity': [
          'step', ['zoom'],
          0,       // hidden below zoom 6
          6, ['match', ['get', 'rank'], 1, 1, 0],    // zoom 6+: only rank 1
          8, ['match', ['get', 'rank'], 1, 1, 2, 1, 0], // zoom 8+: rank 1+2
          10, 1,   // zoom 10+: all ranks
        ],
      },
    })

    return () => {
      if (map.getLayer(LAYER_ID)) map.removeLayer(LAYER_ID)
      if (map.getSource(SOURCE_ID)) map.removeSource(SOURCE_ID)
    }
  }, [map])

  // Toggle visibility
  useEffect(() => {
    if (!map.getLayer(LAYER_ID)) return
    map.setLayoutProperty(LAYER_ID, 'visibility', visible ? 'visible' : 'none')
  }, [map, visible])

  return null
}
