import { useState } from 'react'

interface GeofenceConfigPanelProps {
  geometry: number[][]
  radiusNm?: number
  onSave: (name: string, color: string, fenceType: string) => void
  onCancel: () => void
  // Edit mode props
  editMode?: boolean
  initialName?: string
  initialColor?: string
  initialFenceType?: string
  onDelete?: () => void
}

const COLOUR_PRESETS = [
  { name: 'amber', hex: '#f59e0b' },
  { name: 'red', hex: '#ef4444' },
  { name: 'blue', hex: '#3b82f6' },
  { name: 'green', hex: '#22c55e' },
  { name: 'purple', hex: '#a855f7' },
  { name: 'cyan', hex: '#06b6d4' },
]

const FENCE_TYPES = ['ENTRY', 'EXIT', 'BOTH'] as const

export function GeofenceConfigPanel({
  radiusNm,
  onSave,
  onCancel,
  editMode = false,
  initialName,
  initialColor,
  initialFenceType,
  onDelete,
}: GeofenceConfigPanelProps) {
  const [name, setName] = useState(initialName ?? '')
  const [color, setColor] = useState(initialColor ?? '#f59e0b')
  const [fenceType, setFenceType] = useState<string>(initialFenceType ?? 'ENTRY')

  const canSave = name.trim().length > 0

  const handleSave = () => {
    if (!canSave) return
    onSave(name.trim(), color, fenceType)
  }

  return (
    <div
      className="absolute top-0 right-0 h-full w-80 bg-slate-900 border-l border-slate-700 flex flex-col z-20"
      style={{ borderRadius: '2px' }}
    >
      {/* Header */}
      <div className="flex items-center justify-between px-4 py-3 border-b border-slate-700">
        <span className="text-slate-100 text-sm font-semibold tracking-wide uppercase font-mono">
          {editMode ? 'EDIT GEOFENCE' : 'NEW GEOFENCE'}
        </span>
        <button
          onClick={onCancel}
          className="text-slate-400 hover:text-slate-100 text-lg leading-none"
          aria-label="Close"
        >
          ×
        </button>
      </div>

      {/* Body */}
      <div className="flex flex-col gap-4 px-4 py-4 flex-1 overflow-y-auto">
        {/* Name */}
        <div className="flex flex-col gap-1">
          <label className="text-slate-400 text-xs font-mono uppercase tracking-widest">
            NAME
          </label>
          <input
            type="text"
            value={name}
            onChange={e => setName(e.target.value)}
            placeholder="GEOFENCE NAME"
            className="bg-slate-800 border border-slate-700 text-slate-100 text-sm font-mono px-3 py-2 outline-none focus:border-slate-500 placeholder:text-slate-600 uppercase"
            style={{ borderRadius: '2px' }}
            autoFocus
          />
        </div>

        {/* Colour */}
        <div className="flex flex-col gap-2">
          <label className="text-slate-400 text-xs font-mono uppercase tracking-widest">
            COLOUR
          </label>
          <div className="flex gap-2">
            {COLOUR_PRESETS.map(c => (
              <button
                key={c.hex}
                onClick={() => setColor(c.hex)}
                className="w-8 h-8 border-2 transition-colors"
                style={{
                  backgroundColor: c.hex,
                  borderColor: color === c.hex ? '#e2e8f0' : 'transparent',
                  borderRadius: '2px',
                }}
                title={c.name}
                aria-label={`Select ${c.name} colour`}
              />
            ))}
          </div>
        </div>

        {/* Fence Type */}
        <div className="flex flex-col gap-2">
          <label className="text-slate-400 text-xs font-mono uppercase tracking-widest">
            FENCE TYPE
          </label>
          <div className="flex gap-1">
            {FENCE_TYPES.map(ft => (
              <button
                key={ft}
                onClick={() => setFenceType(ft)}
                className={`flex-1 px-2 py-2 text-xs font-mono tracking-widest border transition-colors ${
                  fenceType === ft
                    ? 'bg-slate-700 border-slate-500 text-slate-100'
                    : 'bg-slate-800 border-slate-700 text-slate-500 hover:text-slate-300'
                }`}
                style={{ borderRadius: '2px' }}
              >
                {ft}
              </button>
            ))}
          </div>
        </div>

        {/* Radius (circle mode only) */}
        {radiusNm != null && (
          <div className="flex flex-col gap-1">
            <label className="text-slate-400 text-xs font-mono uppercase tracking-widest">
              RADIUS
            </label>
            <div className="bg-slate-800 border border-slate-700 text-slate-100 text-sm font-mono px-3 py-2" style={{ borderRadius: '2px' }}>
              {radiusNm.toFixed(2)} NM
            </div>
          </div>
        )}
      </div>

      {/* Footer */}
      <div className="flex flex-col gap-2 px-4 py-4 border-t border-slate-700">
        <button
          onClick={handleSave}
          disabled={!canSave}
          className={`w-full py-2 text-xs font-mono tracking-widest uppercase transition-colors border ${
            canSave
              ? 'bg-slate-700 border-slate-500 text-slate-100 hover:bg-slate-600'
              : 'bg-slate-800 border-slate-700 text-slate-600 cursor-not-allowed'
          }`}
          style={{ borderRadius: '2px' }}
        >
          {editMode ? 'UPDATE GEOFENCE' : 'CREATE GEOFENCE'}
        </button>
        <button
          onClick={onCancel}
          className="w-full py-2 text-xs font-mono tracking-widest uppercase bg-slate-800 border border-slate-700 text-slate-400 hover:text-slate-200 transition-colors"
          style={{ borderRadius: '2px' }}
        >
          CANCEL
        </button>
        {editMode && onDelete && (
          <button
            onClick={onDelete}
            className="w-full py-2 text-xs font-mono tracking-widest uppercase bg-slate-900 border border-red-800 text-red-500 hover:text-red-300 hover:border-red-600 transition-colors"
            style={{ borderRadius: '2px' }}
          >
            DELETE GEOFENCE
          </button>
        )}
      </div>
    </div>
  )
}
