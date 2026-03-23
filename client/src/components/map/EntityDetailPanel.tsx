import { useState } from 'react'
import type { TrackProperties } from '../../types'
import { apiFetch } from '../../lib/api'

interface EntityDetailPanelProps {
  entity: TrackProperties
  onClose: () => void
  onCreateGeofence?: () => void
  onToggleTrails?: () => void
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

export function EntityDetailPanel({ entity, onClose, onCreateGeofence, onToggleTrails }: EntityDetailPanelProps) {
  const [watchlistAdded, setWatchlistAdded] = useState(false)

  const title = entity.entityType === 'Aircraft' ? 'Aircraft Detail' : 'Vessel Detail'
  const speedKnots =
    entity.speed != null ? (entity.speed * 1.94384).toFixed(1) : '—'
  const heading =
    entity.heading != null ? `${entity.heading.toFixed(1)}°` : '—'

  const handleAddToWatchlist = async () => {
    try {
      // Get or create a default watchlist
      const res = await apiFetch('/api/v1/watchlists')
      const watchlists = await res.json()
      let watchlistId = watchlists[0]?.id

      if (!watchlistId) {
        const createRes = await apiFetch('/api/v1/watchlists', {
          method: 'POST',
          body: JSON.stringify({ name: 'Default Watchlist', description: 'Auto-created' })
        })
        const created = await createRes.json()
        watchlistId = created.id
      }

      await apiFetch(`/api/v1/watchlists/${watchlistId}/entries`, {
        method: 'POST',
        body: JSON.stringify({
          identifierType: entity.entityType === 'Aircraft' ? 'ICAO' : 'MMSI',
          identifierValue: entity.entityId,
          reason: 'Added from entity detail panel',
          severity: 'High'
        })
      })

      setWatchlistAdded(true)
      setTimeout(() => setWatchlistAdded(false), 2000)
    } catch (e) {
      console.warn('Failed to add to watchlist:', e)
    }
  }

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

      {/* Action Buttons */}
      <div className="flex flex-col gap-2 px-4 py-3 border-t border-slate-700">
        <button
          onClick={handleAddToWatchlist}
          className="w-full px-3 py-2 font-mono text-xs tracking-widest uppercase bg-slate-800 hover:bg-slate-700 border border-slate-600 transition-colors"
          style={{ borderRadius: '2px' }}
        >
          {watchlistAdded ? (
            <span className="text-green-400">ADDED ✓</span>
          ) : (
            <span className="text-slate-200">ADD TO WATCHLIST</span>
          )}
        </button>

        <button
          onClick={onCreateGeofence}
          disabled={!onCreateGeofence}
          className="w-full px-3 py-2 font-mono text-xs tracking-widest uppercase bg-slate-800 hover:bg-slate-700 border border-slate-600 text-slate-200 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          style={{ borderRadius: '2px' }}
        >
          CREATE GEOFENCE
        </button>

        <button
          onClick={onToggleTrails}
          disabled={!onToggleTrails}
          className="w-full px-3 py-2 font-mono text-xs tracking-widest uppercase bg-slate-800 hover:bg-slate-700 border border-slate-600 text-slate-200 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          style={{ borderRadius: '2px' }}
        >
          TRACK HISTORY
        </button>
      </div>
    </div>
  )
}
