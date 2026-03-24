import { useState, useEffect } from 'react'
import type { TrackProperties, EntityDetail } from '../../types'
import { apiFetch } from '../../lib/api'
import { useToast } from '../../contexts/ToastContext'

interface EntityDetailPanelProps {
  entity: TrackProperties
  onClose: () => void
  onCreateGeofence?: () => void
  onToggleTrails?: () => void
  onReplay?: () => void
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

function countryFlag(countryName: string): string {
  const map: Record<string, string> = {
    'United Kingdom': '\u{1F1EC}\u{1F1E7}', 'Germany': '\u{1F1E9}\u{1F1EA}', 'France': '\u{1F1EB}\u{1F1F7}',
    'Netherlands': '\u{1F1F3}\u{1F1F1}', 'Norway': '\u{1F1F3}\u{1F1F4}', 'Sweden': '\u{1F1F8}\u{1F1EA}',
    'Denmark': '\u{1F1E9}\u{1F1F0}', 'Italy': '\u{1F1EE}\u{1F1F9}', 'Spain': '\u{1F1EA}\u{1F1F8}',
    'Greece': '\u{1F1EC}\u{1F1F7}', 'Turkey': '\u{1F1F9}\u{1F1F7}', 'Russia': '\u{1F1F7}\u{1F1FA}',
    'China': '\u{1F1E8}\u{1F1F3}', 'Japan': '\u{1F1EF}\u{1F1F5}', 'South Korea': '\u{1F1F0}\u{1F1F7}',
    'United States': '\u{1F1FA}\u{1F1F8}', 'Canada': '\u{1F1E8}\u{1F1E6}', 'Australia': '\u{1F1E6}\u{1F1FA}',
    'Panama': '\u{1F1F5}\u{1F1E6}', 'Liberia': '\u{1F1F1}\u{1F1F7}', 'Marshall Islands': '\u{1F1F2}\u{1F1ED}',
    'Bahamas': '\u{1F1E7}\u{1F1F8}', 'Malta': '\u{1F1F2}\u{1F1F9}', 'Singapore': '\u{1F1F8}\u{1F1EC}',
    'Hong Kong': '\u{1F1ED}\u{1F1F0}', 'India': '\u{1F1EE}\u{1F1F3}', 'Brazil': '\u{1F1E7}\u{1F1F7}',
    'Portugal': '\u{1F1F5}\u{1F1F9}', 'Finland': '\u{1F1EB}\u{1F1EE}', 'Poland': '\u{1F1F5}\u{1F1F1}',
    'Croatia': '\u{1F1ED}\u{1F1F7}', 'Gibraltar': '\u{1F1EC}\u{1F1EE}', 'Estonia': '\u{1F1EA}\u{1F1EA}',
    'Latvia': '\u{1F1F1}\u{1F1FB}', 'Lithuania': '\u{1F1F1}\u{1F1F9}', 'Ukraine': '\u{1F1FA}\u{1F1E6}',
    'Israel': '\u{1F1EE}\u{1F1F1}', 'UAE': '\u{1F1E6}\u{1F1EA}', 'Saudi Arabia': '\u{1F1F8}\u{1F1E6}',
    'Indonesia': '\u{1F1EE}\u{1F1E9}', 'Philippines': '\u{1F1F5}\u{1F1ED}', 'Vietnam': '\u{1F1FB}\u{1F1F3}',
    'Thailand': '\u{1F1F9}\u{1F1ED}', 'Malaysia': '\u{1F1F2}\u{1F1FE}', 'New Zealand': '\u{1F1F3}\u{1F1FF}',
    'South Africa': '\u{1F1FF}\u{1F1E6}', 'Nigeria': '\u{1F1F3}\u{1F1EC}', 'Egypt': '\u{1F1EA}\u{1F1EC}',
    'Taiwan': '\u{1F1F9}\u{1F1FC}', 'Iran': '\u{1F1EE}\u{1F1F7}',
  }
  return map[countryName] ?? ''
}

export function EntityDetailPanel({ entity, onClose, onCreateGeofence, onToggleTrails, onReplay }: EntityDetailPanelProps) {
  const { showToast } = useToast()
  const [watchlistAdded, setWatchlistAdded] = useState(false)
  const [watchlistLoading, setWatchlistLoading] = useState(false)
  const [detail, setDetail] = useState<EntityDetail | null>(null)
  const [detailLoading, setDetailLoading] = useState(true)

  const title = entity.entityType === 'Aircraft' ? 'Aircraft Detail' : 'Vessel Detail'
  const speedKnots =
    entity.speed != null ? (entity.speed * 1.94384).toFixed(1) : null
  const heading =
    entity.heading != null ? `${entity.heading.toFixed(1)}` : null

  // Fetch enrichment data
  useEffect(() => {
    let cancelled = false
    setDetailLoading(true)
    setDetail(null)

    // The entityId on TrackProperties may be the identifier value (MMSI/ICAO),
    // but we need the GUID entity ID. Check if it looks like a GUID.
    const isGuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(entity.entityId)
    if (!isGuid) {
      setDetailLoading(false)
      return
    }

    apiFetch(`/api/v1/entities/${entity.entityId}`)
      .then(res => {
        if (!res.ok) throw new Error('Failed to fetch entity detail')
        return res.json()
      })
      .then(data => {
        if (!cancelled) setDetail(data)
      })
      .catch(() => {
        // Enrichment is optional — fail silently
      })
      .finally(() => {
        if (!cancelled) setDetailLoading(false)
      })

    return () => { cancelled = true }
  }, [entity.entityId])

  const handleAddToWatchlist = async () => {
    setWatchlistLoading(true)
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

      // Use real external identifier (MMSI/ICAO) from enrichment, not internal UUID
      const identifier = detail?.identifiers?.[0]
      const idType = identifier?.type ?? (entity.entityType === 'Aircraft' ? 'ICAO' : 'MMSI')
      const idValue = identifier?.value ?? entity.entityId

      await apiFetch(`/api/v1/watchlists/${watchlistId}/entries`, {
        method: 'POST',
        body: JSON.stringify({
          identifierType: idType,
          identifierValue: idValue,
          reason: `Added from entity detail panel — ${entity.displayName || 'Unknown'}`,
          severity: 'High'
        })
      })

      setWatchlistAdded(true)
      showToast('ADDED TO WATCHLIST', 'success')
      setTimeout(() => setWatchlistAdded(false), 2000)
    } catch {
      showToast('FAILED TO ADD TO WATCHLIST', 'error')
    } finally {
      setWatchlistLoading(false)
    }
  }

  const enrichment = detail?.enrichment
  const identifiers = detail?.identifiers

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
          &#xd7;
        </button>
      </div>

      {/* Body */}
      <div className="flex flex-col gap-0 px-4 py-3 text-sm flex-1 overflow-y-auto">
        {/* Name */}
        <div className="flex justify-between py-1">
          <span className="text-slate-400">Name</span>
          <span className="text-slate-100">{detail?.displayName ?? (entity.displayName || '\u2014')}</span>
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
          <span className={statusColour(detail?.status ?? entity.status)}>{detail?.status ?? entity.status}</span>
        </div>

        {/* Separator */}
        <hr className="border-slate-700 my-2" />

        {/* Heading */}
        <div className="flex justify-between py-1">
          <span className="text-slate-400">Heading</span>
          <span className="text-slate-100">{heading != null ? `${heading}\u00b0` : '\u2014'}</span>
        </div>

        {/* Speed */}
        <div className="flex justify-between py-1">
          <span className="text-slate-400">Speed</span>
          <span className="text-slate-100">{speedKnots != null ? `${speedKnots} kn` : '\u2014'}</span>
        </div>

        {/* Separator */}
        <hr className="border-slate-700 my-2" />

        {/* Enrichment section */}
        {detailLoading && (
          <div className="py-2 text-center text-slate-500 text-xs uppercase tracking-wide">
            Loading details...
          </div>
        )}

        {enrichment && (
          <>
            {/* Flag (vessels) */}
            {entity.entityType === 'Vessel' && (
              <div className="flex justify-between py-1">
                <span className="text-slate-400">Flag</span>
                <span className={enrichment.flag ? 'text-slate-100' : 'text-slate-500'}>
                  {enrichment.flag ? `${countryFlag(enrichment.flag)} ${enrichment.flag}` : 'Unknown'}
                </span>
              </div>
            )}

            {/* Vessel Type */}
            {entity.entityType === 'Vessel' && (
              <div className="flex justify-between py-1">
                <span className="text-slate-400">Vessel Type</span>
                <span className={enrichment.vesselType ? 'text-slate-100' : 'text-slate-500'}>
                  {enrichment.vesselType || 'Unknown'}
                </span>
              </div>
            )}

            {/* Destination */}
            {entity.entityType === 'Vessel' && enrichment.destination && (
              <div className="flex justify-between py-1">
                <span className="text-slate-400">Destination</span>
                <span className="text-slate-100 uppercase">{enrichment.destination}</span>
              </div>
            )}

            {/* ETA */}
            {entity.entityType === 'Vessel' && enrichment.eta && (
              <div className="flex justify-between py-1">
                <span className="text-slate-400">ETA</span>
                <span className="text-slate-100">{enrichment.eta}</span>
              </div>
            )}

            {/* Dimensions */}
            {entity.entityType === 'Vessel' && (enrichment.length != null || enrichment.beam != null) && (
              <div className="flex justify-between py-1">
                <span className="text-slate-400">Dimensions</span>
                <span className="text-slate-100">
                  {enrichment.length != null && enrichment.beam != null
                    ? `${enrichment.length}m \u00d7 ${enrichment.beam}m`
                    : enrichment.length != null
                      ? `${enrichment.length}m L`
                      : `${enrichment.beam}m B`}
                </span>
              </div>
            )}

            {/* Draught */}
            {entity.entityType === 'Vessel' && enrichment.draught != null && (
              <div className="flex justify-between py-1">
                <span className="text-slate-400">Draught</span>
                <span className="text-slate-100">{enrichment.draught}m</span>
              </div>
            )}

            {/* Aircraft Type */}
            {entity.entityType === 'Aircraft' && (
              <div className="flex justify-between py-1">
                <span className="text-slate-400">Aircraft Type</span>
                <span className={enrichment.aircraftType ? 'text-slate-100' : 'text-slate-500'}>
                  {enrichment.aircraftType || 'Unknown'}
                </span>
              </div>
            )}

            {/* Callsign */}
            {enrichment.callsign && (
              <div className="flex justify-between py-1">
                <span className="text-slate-400">Callsign</span>
                <span className="text-slate-100 font-mono text-xs">{enrichment.callsign}</span>
              </div>
            )}

            {/* IMO */}
            {enrichment.imo && (
              <div className="flex justify-between py-1">
                <span className="text-slate-400">IMO</span>
                <span className="text-slate-100 font-mono text-xs">{enrichment.imo}</span>
              </div>
            )}

            {/* Registration */}
            {enrichment.registration && (
              <div className="flex justify-between py-1">
                <span className="text-slate-400">Registration</span>
                <span className="text-slate-100 font-mono text-xs">{enrichment.registration}</span>
              </div>
            )}

            {/* Squawk */}
            {enrichment.squawk && (
              <div className="flex justify-between py-1">
                <span className="text-slate-400">Squawk</span>
                <span className="text-slate-100 font-mono text-xs">{enrichment.squawk}</span>
              </div>
            )}

            {/* Altitude */}
            {enrichment.altitude != null && (
              <div className="flex justify-between py-1">
                <span className="text-slate-400">Altitude</span>
                <span className="text-slate-100">{enrichment.altitude.toLocaleString()} ft</span>
              </div>
            )}
          </>
        )}

        {/* Emergency / Military / Vertical Rate from live track data */}
        {entity.entityType === 'Aircraft' && entity.emergency && entity.emergency !== 'none' && (
          <div className="flex justify-between py-1">
            <span className="text-slate-400">Emergency</span>
            <span className="text-red-400 font-mono text-xs font-semibold uppercase animate-pulse">
              {entity.emergency}
            </span>
          </div>
        )}

        {entity.entityType === 'Aircraft' && entity.isMilitary && (
          <div className="flex justify-between py-1">
            <span className="text-slate-400">Classification</span>
            <span className="text-orange-400 font-mono text-xs font-semibold uppercase">MILITARY</span>
          </div>
        )}

        {/* Identifiers */}
        {identifiers && identifiers.length > 0 && (
          <>
            <hr className="border-slate-700 my-2" />
            <div className="text-slate-500 text-xs uppercase tracking-wide py-1">Identifiers</div>
            {identifiers.map((ident, i) => (
              <div key={i} className="flex justify-between py-1">
                <span className="text-slate-400">{ident.type}</span>
                <span className="text-slate-100 font-mono text-xs">{ident.value}</span>
              </div>
            ))}
          </>
        )}

        {/* Fallback vessel type from track properties when no enrichment */}
        {!enrichment && !detailLoading && entity.entityType === 'Vessel' && (
          <div className="flex justify-between py-1">
            <span className="text-slate-400">Vessel Type</span>
            <span className="text-slate-100">{entity.vesselType}</span>
          </div>
        )}

        {/* Last Update */}
        <hr className="border-slate-700 my-2" />
        <div className="flex justify-between py-1">
          <span className="text-slate-400">Last Update</span>
          <span className="text-slate-100 font-mono text-xs">
            {detail?.lastSeen ? formatTime(detail.lastSeen) : formatTime(entity.lastUpdated)}
          </span>
        </div>
      </div>

      {/* Action Buttons */}
      <div className="flex flex-col gap-2 px-4 py-3 border-t border-slate-700">
        {/* External link button */}
        {enrichment?.externalUrl && (
          <a
            href={enrichment.externalUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="w-full px-3 py-2 font-mono text-xs tracking-widest uppercase bg-blue-900/40 hover:bg-blue-800/50 border border-blue-700/50 text-blue-300 transition-colors text-center block"
            style={{ borderRadius: '2px' }}
          >
            {entity.entityType === 'Vessel' ? 'VIEW ON MARINETRAFFIC' : 'VIEW ON PLANESPOTTERS'}
          </a>
        )}

        <button
          onClick={handleAddToWatchlist}
          disabled={watchlistLoading || watchlistAdded}
          className="w-full px-3 py-2 font-mono text-xs tracking-widest uppercase bg-slate-800 hover:bg-slate-700 border border-slate-600 transition-colors disabled:opacity-60 disabled:cursor-not-allowed"
          style={{ borderRadius: '2px' }}
        >
          {watchlistAdded ? (
            <span className="text-green-400">ADDED</span>
          ) : watchlistLoading ? (
            <span className="text-slate-400">ADDING...</span>
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

        <button
          onClick={onReplay}
          disabled={!onReplay}
          className="w-full px-3 py-2 font-mono text-xs tracking-widest uppercase bg-slate-800 hover:bg-slate-700 border border-slate-600 text-slate-200 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          style={{ borderRadius: '2px' }}
        >
          REPLAY
        </button>
      </div>
    </div>
  )
}
