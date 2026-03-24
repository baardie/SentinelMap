import { useCallback, useEffect, useState } from 'react'
import { apiFetch } from '../../lib/api'

interface SafetyAlert {
  id: string
  summary: string
  details: string | null
  severity: string
  createdAt: string
}

interface SafetyAlertsPanelProps {
  isOpen: boolean
  onClose: () => void
  onCountChange: (count: number) => void
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

export function SafetyAlertsPanel({ isOpen, onClose, onCountChange }: SafetyAlertsPanelProps) {
  const [alerts, setAlerts] = useState<SafetyAlert[]>([])
  const [loading, setLoading] = useState(false)

  const fetchAlerts = useCallback(async () => {
    try {
      setLoading(true)
      const res = await apiFetch('/api/v1/alerts?type=SafetyBroadcast&limit=50')
      const data = await res.json()
      const mapped = data.map((a: { id: string; summary: string; details: string | null; severity: string; createdAt: string }) => ({
        id: a.id,
        summary: a.summary,
        details: a.details,
        severity: a.severity,
        createdAt: a.createdAt,
      }))
      setAlerts(mapped)
      onCountChange(mapped.length)
    } catch {
      setAlerts([])
    } finally {
      setLoading(false)
    }
  }, [onCountChange])

  // Poll every 30s
  useEffect(() => {
    fetchAlerts()
    const interval = setInterval(fetchAlerts, 30000)
    return () => clearInterval(interval)
  }, [fetchAlerts])

  if (!isOpen) return null

  return (
    <div className="absolute right-0 top-10 z-30 w-96 max-h-[28rem] bg-slate-900 border border-slate-700 flex flex-col" style={{ borderRadius: '2px' }}>
      {/* Header */}
      <div className="flex items-center justify-between px-4 py-3 border-b border-slate-700">
        <div className="flex items-center gap-2">
          <span className="text-amber-400 text-sm">⚠</span>
          <span className="text-slate-100 text-sm font-semibold tracking-wide uppercase font-mono">
            SAFETY BROADCASTS
          </span>
          {alerts.length > 0 && (
            <span className="bg-amber-600 text-slate-900 px-1.5 py-0.5 font-mono text-xs font-bold" style={{ borderRadius: '2px' }}>
              {alerts.length}
            </span>
          )}
        </div>
        <button
          onClick={onClose}
          className="text-slate-400 hover:text-slate-100 text-lg leading-none"
          aria-label="Close"
        >
          ×
        </button>
      </div>

      {/* Alert list */}
      <div className="flex-1 overflow-y-auto">
        {loading && alerts.length === 0 ? (
          <div className="flex h-24 items-center justify-center font-mono text-xs text-slate-500">
            LOADING...
          </div>
        ) : alerts.length === 0 ? (
          <div className="flex h-24 items-center justify-center font-mono text-xs text-slate-600">
            NO SAFETY BROADCASTS RECEIVED
          </div>
        ) : (
          alerts.map(alert => {
            let text = alert.summary
            let mmsi = ''

            // Extract text and MMSI from details JSON
            if (alert.details) {
              try {
                const d = JSON.parse(alert.details)
                text = d.text || text
                mmsi = d.mmsi || ''
              } catch { /* use summary */ }
            }

            return (
              <div
                key={alert.id}
                className="border-b border-slate-800 px-4 py-3 hover:bg-slate-800 transition-colors"
              >
                <div className="flex items-center justify-between mb-1">
                  <span className="font-mono text-xs text-amber-400 tracking-widest">
                    {mmsi ? `MMSI ${mmsi}` : 'UNKNOWN SOURCE'}
                  </span>
                  <span className="font-mono text-xs text-slate-500">
                    {formatTime(alert.createdAt)}
                  </span>
                </div>
                <p className="text-xs text-slate-200 font-mono leading-relaxed whitespace-pre-wrap break-words">
                  {text}
                </p>
              </div>
            )
          })
        )}
      </div>
    </div>
  )
}
