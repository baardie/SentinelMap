import { useState, useMemo } from 'react'
import type { AlertNotification } from '../../types'

interface AlertFeedProps {
  alerts: AlertNotification[]
  onAlertClick?: (entityId: string) => void
}

const ALERT_TYPES = [
  'GeofenceBreach',
  'WatchlistMatch',
  'AisDark',
  'SpeedAnomaly',
  'TransponderSwap',
  'CorrelationLink',
  'RouteDeviation',
  'SafetyBroadcast',
  'EmergencySquawk',
] as const

const SEVERITY_LEVELS = ['Critical', 'High', 'Medium', 'Low'] as const

function severityDotClass(severity: string): string {
  if (severity === 'Critical' || severity === 'High') return 'text-red-500'
  if (severity === 'Medium') return 'text-amber-400'
  return 'text-sky-500'
}

function severityBgClass(severity: string, active: boolean): string {
  if (!active) return 'bg-slate-800 border-slate-700 text-slate-600'
  if (severity === 'Critical' || severity === 'High') return 'bg-red-950 border-red-800 text-red-400'
  if (severity === 'Medium') return 'bg-amber-950 border-amber-800 text-amber-400'
  return 'bg-sky-950 border-sky-800 text-sky-400'
}

function formatRelativeTime(iso: string): string {
  try {
    const diffMs = Date.now() - new Date(iso).getTime()
    const diffSec = Math.floor(diffMs / 1000)
    if (diffSec < 60) return `${diffSec}s ago`
    const diffMin = Math.floor(diffSec / 60)
    if (diffMin < 60) return `${diffMin}m ago`
    const diffHr = Math.floor(diffMin / 60)
    if (diffHr < 24) return `${diffHr}h ago`
    return new Date(iso).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })
  } catch {
    return iso
  }
}

function shortType(type: string): string {
  return type
    .replace('GeofenceBreach', 'GEOFENCE')
    .replace('WatchlistMatch', 'WATCHLIST')
    .replace('AisDark', 'AIS DARK')
    .replace('SpeedAnomaly', 'SPEED')
    .replace('TransponderSwap', 'TRANSPONDER')
    .replace('CorrelationLink', 'CORRELATION')
    .replace('RouteDeviation', 'DEVIATION')
    .replace('SafetyBroadcast', 'SAFETY')
    .replace('EmergencySquawk', 'EMERGENCY')
}

export function AlertFeed({ alerts, onAlertClick }: AlertFeedProps) {
  const [expanded, setExpanded] = useState(true)
  const [showFilters, setShowFilters] = useState(false)
  const [enabledTypes, setEnabledTypes] = useState<Set<string>>(new Set(ALERT_TYPES.filter(t => t !== 'CorrelationLink')))
  const [enabledSeverities, setEnabledSeverities] = useState<Set<string>>(new Set(SEVERITY_LEVELS.filter(s => s !== 'Low')))

  const toggleType = (type: string) => {
    setEnabledTypes(prev => {
      const next = new Set(prev)
      if (next.has(type)) next.delete(type)
      else next.add(type)
      return next
    })
  }

  const toggleSeverity = (sev: string) => {
    setEnabledSeverities(prev => {
      const next = new Set(prev)
      if (next.has(sev)) next.delete(sev)
      else next.add(sev)
      return next
    })
  }

  const filteredAlerts = useMemo(
    () => alerts.filter(a => enabledTypes.has(a.type) && enabledSeverities.has(a.severity)),
    [alerts, enabledTypes, enabledSeverities],
  )

  const handleAlertClick = (alert: AlertNotification) => {
    if (alert.entityId && onAlertClick) {
      onAlertClick(alert.entityId)
    }
  }

  return (
    <div
      className={`flex flex-col border-t border-slate-700 bg-slate-900 transition-all duration-200 ${
        expanded ? 'h-64' : 'h-8'
      }`}
    >
      {/* Header bar */}
      <div className="flex h-8 flex-shrink-0 items-center justify-between border-b border-slate-700 px-4">
        <div className="flex items-center gap-2">
          <span className="font-mono text-xs font-bold tracking-widest text-slate-300">ALERTS</span>
          {filteredAlerts.length > 0 && (
            <span className="bg-red-600 px-1.5 py-0.5 font-mono text-xs font-bold text-white" style={{ borderRadius: '2px' }}>
              {filteredAlerts.length}
            </span>
          )}
          {filteredAlerts.length !== alerts.length && (
            <span className="font-mono text-xs text-slate-500">
              / {alerts.length}
            </span>
          )}
          <button
            onClick={() => setShowFilters(v => !v)}
            className={`px-1.5 py-0.5 font-mono text-xs tracking-widest border transition-colors ${
              showFilters
                ? 'bg-slate-700 border-slate-500 text-slate-200'
                : 'bg-slate-800 border-slate-700 text-slate-500 hover:text-slate-300'
            }`}
            style={{ borderRadius: '2px' }}
          >
            FILTER
          </button>
        </div>
        <button
          onClick={() => setExpanded(prev => !prev)}
          className="font-mono text-xs text-slate-400 hover:text-slate-100"
          aria-label={expanded ? 'Collapse alert feed' : 'Expand alert feed'}
        >
          {expanded ? '▼' : '▲'}
        </button>
      </div>

      {/* Filter bar */}
      {expanded && showFilters && (
        <div className="flex flex-col gap-2 border-b border-slate-800 px-4 py-2 bg-slate-950">
          {/* Type filters */}
          <div className="flex items-center gap-1 flex-wrap">
            <span className="font-mono text-xs text-slate-500 tracking-widest mr-1 w-12">TYPE</span>
            {ALERT_TYPES.map(type => {
              const active = enabledTypes.has(type)
              const count = alerts.filter(a => a.type === type).length
              return (
                <button
                  key={type}
                  onClick={() => toggleType(type)}
                  className={`px-1.5 py-0.5 font-mono text-xs tracking-wider border transition-colors ${
                    active
                      ? 'bg-slate-700 border-slate-500 text-slate-200'
                      : 'bg-slate-800 border-slate-700 text-slate-600'
                  }`}
                  style={{ borderRadius: '2px' }}
                >
                  {shortType(type)}
                  {count > 0 && <span className="ml-1 text-slate-500">{count}</span>}
                </button>
              )
            })}
          </div>
          {/* Severity filters */}
          <div className="flex items-center gap-1">
            <span className="font-mono text-xs text-slate-500 tracking-widest mr-1 w-12">SEV</span>
            {SEVERITY_LEVELS.map(sev => {
              const active = enabledSeverities.has(sev)
              return (
                <button
                  key={sev}
                  onClick={() => toggleSeverity(sev)}
                  className={`px-1.5 py-0.5 font-mono text-xs tracking-wider border transition-colors ${severityBgClass(sev, active)}`}
                  style={{ borderRadius: '2px' }}
                >
                  {sev.toUpperCase()}
                </button>
              )
            })}
          </div>
        </div>
      )}

      {/* Alert list */}
      {expanded && (
        <div className="flex-1 overflow-y-auto">
          {filteredAlerts.length === 0 ? (
            <div className="flex h-full items-center justify-center font-mono text-xs text-slate-600">
              {alerts.length === 0 ? 'No alerts' : 'No alerts match filters'}
            </div>
          ) : (
            filteredAlerts.map(alert => (
              <button
                key={alert.alertId}
                className="flex w-full items-start gap-3 border-b border-slate-800 px-4 py-2 text-left hover:bg-slate-800 focus:outline-none"
                onClick={() => handleAlertClick(alert)}
              >
                {/* Severity dot */}
                <span className={`mt-0.5 flex-shrink-0 text-lg leading-none ${severityDotClass(alert.severity)}`}>
                  ●
                </span>

                {/* Type + summary */}
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <span className="font-mono text-xs font-semibold uppercase tracking-wide text-slate-400">
                      {shortType(alert.type)}
                    </span>
                    <span className={`font-mono text-xs uppercase tracking-wide ${severityDotClass(alert.severity)}`}>
                      {alert.severity}
                    </span>
                  </div>
                  <p className="mt-0.5 truncate text-xs text-slate-200">{alert.summary}</p>
                </div>

                {/* Timestamp */}
                <span className="flex-shrink-0 font-mono text-xs text-slate-500">
                  {formatRelativeTime(alert.createdAt)}
                </span>
              </button>
            ))
          )}
        </div>
      )}
    </div>
  )
}
