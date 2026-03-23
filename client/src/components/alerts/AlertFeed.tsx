import { useState } from 'react'
import type { AlertNotification } from '../../types'

interface AlertFeedProps {
  alerts: AlertNotification[]
  onAlertClick?: (entityId: string) => void
}

function severityDotClass(severity: AlertNotification['severity']): string {
  if (severity === 'Critical' || severity === 'High') return 'text-red-500'
  if (severity === 'Medium') return 'text-amber-500'
  return 'text-sky-500'
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

export function AlertFeed({ alerts, onAlertClick }: AlertFeedProps) {
  const [expanded, setExpanded] = useState(true)

  const handleAlertClick = (alert: AlertNotification) => {
    if (alert.entityId && onAlertClick) {
      onAlertClick(alert.entityId)
    }
  }

  return (
    <div
      className={`flex flex-col border-t border-slate-700 bg-slate-900 transition-all duration-200 ${
        expanded ? 'h-48' : 'h-8'
      }`}
    >
      {/* Header bar */}
      <div className="flex h-8 flex-shrink-0 items-center justify-between border-b border-slate-700 px-4">
        <div className="flex items-center gap-2">
          <span className="font-mono text-xs font-bold tracking-widest text-slate-300">ALERTS</span>
          {alerts.length > 0 && (
            <span className="rounded bg-red-600 px-1.5 py-0.5 font-mono text-xs font-bold text-white">
              {alerts.length}
            </span>
          )}
        </div>
        <button
          onClick={() => setExpanded(prev => !prev)}
          className="font-mono text-xs text-slate-400 hover:text-slate-100"
          aria-label={expanded ? 'Collapse alert feed' : 'Expand alert feed'}
        >
          {expanded ? '▼' : '▲'}
        </button>
      </div>

      {/* Alert list */}
      {expanded && (
        <div className="flex-1 overflow-y-auto">
          {alerts.length === 0 ? (
            <div className="flex h-full items-center justify-center font-mono text-xs text-slate-600">
              No alerts
            </div>
          ) : (
            alerts.map(alert => (
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
                      {alert.type}
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
