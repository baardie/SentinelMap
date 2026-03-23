import type { TrackProperties } from '../../types'

interface EntityDetailPanelProps {
  entity: TrackProperties
  onClose: () => void
}

function formatTime(iso: string): string {
  try {
    return new Date(iso).toLocaleTimeString(undefined, {
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    })
  } catch {
    return iso
  }
}

function statusColour(status: string): string {
  if (status === 'Active') return 'text-green-400'
  if (status === 'Dark') return 'text-red-400'
  return 'text-amber-400'
}

export function EntityDetailPanel({ entity, onClose }: EntityDetailPanelProps) {
  const title = entity.entityType === 'Aircraft' ? 'Aircraft Detail' : 'Vessel Detail'
  const speedKnots =
    entity.speed != null ? (entity.speed * 1.94384).toFixed(1) : '—'
  const heading =
    entity.heading != null ? `${entity.heading.toFixed(1)}°` : '—'

  return (
    <div
      className="absolute top-0 right-0 h-full w-80 bg-slate-900 border-l border-slate-700 flex flex-col z-10"
      style={{ borderRadius: '2px' }}
    >
      {/* Header */}
      <div className="flex items-center justify-between px-4 py-3 border-b border-slate-700">
        <span className="text-slate-100 text-sm font-semibold tracking-wide uppercase">
          {title}
        </span>
        <button
          onClick={onClose}
          className="text-slate-400 hover:text-slate-100 text-lg leading-none"
          aria-label="Close"
        >
          ×
        </button>
      </div>

      {/* Body */}
      <div className="flex flex-col gap-0 px-4 py-3 text-sm flex-1 overflow-y-auto">
        {/* Name */}
        <div className="flex justify-between py-1">
          <span className="text-slate-400">Name</span>
          <span className="text-slate-100">{entity.displayName || '—'}</span>
        </div>

        {/* Entity ID */}
        <div className="flex justify-between py-1">
          <span className="text-slate-400">Entity ID</span>
          <span className="text-slate-100 font-mono text-xs">{entity.entityId}</span>
        </div>

        {/* Type */}
        <div className="flex justify-between py-1">
          <span className="text-slate-400">Type</span>
          <span className="text-slate-100">{entity.entityType}</span>
        </div>

        {/* Status */}
        <div className="flex justify-between py-1">
          <span className="text-slate-400">Status</span>
          <span className={statusColour(entity.status)}>{entity.status}</span>
        </div>

        {/* Separator */}
        <hr className="border-slate-700 my-2" />

        {/* Heading */}
        <div className="flex justify-between py-1">
          <span className="text-slate-400">Heading</span>
          <span className="text-slate-100">{heading}</span>
        </div>

        {/* Speed */}
        <div className="flex justify-between py-1">
          <span className="text-slate-400">Speed</span>
          <span className="text-slate-100">{speedKnots} kn</span>
        </div>

        {/* Separator */}
        <hr className="border-slate-700 my-2" />

        {/* Vessel Type (vessels only) */}
        {entity.entityType === 'Vessel' && (
          <div className="flex justify-between py-1">
            <span className="text-slate-400">Vessel Type</span>
            <span className="text-slate-100">{entity.vesselType}</span>
          </div>
        )}

        {/* Last Update */}
        <div className="flex justify-between py-1">
          <span className="text-slate-400">Last Update</span>
          <span className="text-slate-100 font-mono text-xs">{formatTime(entity.lastUpdated)}</span>
        </div>
      </div>
    </div>
  )
}
