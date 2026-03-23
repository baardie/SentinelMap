interface LayerControlPanelProps {
  visibility: Record<string, boolean>
  onChange: (layer: string, visible: boolean) => void
}

const LAYERS = [
  { id: 'vessels', label: 'VESSELS', defaultOn: true },
  { id: 'aircraft', label: 'AIRCRAFT', defaultOn: true },
  { id: 'trails', label: 'TRAILS', defaultOn: false },
  { id: 'geofences', label: 'GEOFENCES', defaultOn: true },
  { id: 'baseStations', label: 'BASE STATIONS', defaultOn: true },
  { id: 'aidsToNav', label: 'AIDS TO NAV', defaultOn: false },
  { id: 'airspace', label: 'AIRSPACE', defaultOn: true },
  { id: 'airports', label: 'AIRPORTS', defaultOn: true },
  { id: 'military', label: 'MILITARY', defaultOn: true },
  { id: 'structures', label: 'STRUCTURES', defaultOn: true },
]

export function LayerControlPanel({ visibility, onChange }: LayerControlPanelProps) {
  return (
    <div
      className="w-44 bg-slate-900 border border-slate-700 flex flex-col"
      style={{ borderRadius: '2px' }}
    >
      <div className="px-3 py-2 border-b border-slate-700">
        <span className="text-slate-400 text-xs font-mono uppercase tracking-widest">LAYERS</span>
      </div>
      <div className="flex flex-col py-1">
        {LAYERS.map(layer => {
          const isVisible = visibility[layer.id] !== false
          return (
            <label
              key={layer.id}
              className="flex items-center justify-between px-3 py-1.5 cursor-pointer hover:bg-slate-800 transition-colors"
            >
              <span className={`text-xs font-mono tracking-wider ${isVisible ? 'text-slate-200' : 'text-slate-600'}`}>
                {layer.label}
              </span>
              <button
                role="checkbox"
                aria-checked={isVisible}
                onClick={() => onChange(layer.id, !isVisible)}
                className={`w-8 h-4 border transition-colors flex items-center ${
                  isVisible
                    ? 'bg-slate-600 border-slate-400'
                    : 'bg-slate-800 border-slate-700'
                }`}
                style={{ borderRadius: '2px' }}
              >
                <span
                  className={`w-3 h-3 transition-transform ${
                    isVisible ? 'bg-slate-200 translate-x-4' : 'bg-slate-600 translate-x-0.5'
                  }`}
                  style={{ borderRadius: '1px' }}
                />
              </button>
            </label>
          )
        })}
      </div>
    </div>
  )
}
