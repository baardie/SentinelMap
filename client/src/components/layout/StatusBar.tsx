import type { ConnectionStatus } from '@/hooks/useTrackHub'

interface StatusBarProps {
  trackCount: number
  vesselCount: number
  aircraftCount: number
  alertCount: number
  connectionStatus: ConnectionStatus
}

const STATUS_DOT: Record<ConnectionStatus, string> = {
  connected: 'bg-emerald-500',
  reconnecting: 'bg-amber-400',
  disconnected: 'bg-red-500',
}

const STATUS_LABEL: Record<ConnectionStatus, string> = {
  connected: 'LIVE',
  reconnecting: 'RECONNECTING',
  disconnected: 'DISCONNECTED',
}

export function StatusBar({ trackCount, vesselCount, aircraftCount, alertCount, connectionStatus }: StatusBarProps) {
  return (
    <div className="flex h-6 items-center justify-between border-t border-slate-800 bg-slate-950 px-4 font-mono text-xs text-slate-500">
      <div className="flex items-center gap-2">
        <span className={`h-2 w-2 rounded-full ${STATUS_DOT[connectionStatus]}`} />
        <span className="tracking-widest">{STATUS_LABEL[connectionStatus]}</span>
      </div>
      <span>
        TRACKS: {trackCount} ({vesselCount}V | {aircraftCount}A)
      </span>
      <span>ALERTS: {alertCount}</span>
      <span className="text-slate-600">SIM</span>
    </div>
  )
}
