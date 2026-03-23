import { useCallback, useEffect, useState } from 'react'
import { useAuth } from '@/contexts/AuthContext'
import { apiFetch, getStoredRefreshToken } from '@/lib/api'

interface Session {
  familyId: string
  deviceInfo: string
  createdAt: string
  lastUsedAt: string
  isActive: boolean
  isCurrent: boolean
  userEmail?: string
}

interface SessionsPageProps {
  onClose: () => void
}

function parseDeviceInfo(ua: string): string {
  if (!ua || ua === 'Unknown') return 'Unknown Device'
  // Extract browser and OS from user-agent
  const browser =
    ua.match(/Edg\/([\d.]+)/)?.[0]?.replace('Edg/', 'Edge ') ??
    ua.match(/Chrome\/([\d.]+)/)?.[0]?.replace('Chrome/', 'Chrome ') ??
    ua.match(/Firefox\/([\d.]+)/)?.[0]?.replace('Firefox/', 'Firefox ') ??
    ua.match(/Safari\/([\d.]+)/)?.[0]?.replace('Safari/', 'Safari ') ??
    'Unknown Browser'
  const os =
    ua.includes('Windows') ? 'Windows' :
    ua.includes('Mac OS') ? 'macOS' :
    ua.includes('Linux') ? 'Linux' :
    ua.includes('Android') ? 'Android' :
    ua.includes('iPhone') || ua.includes('iPad') ? 'iOS' :
    'Unknown OS'
  return `${browser} / ${os}`
}

function formatTimestamp(ts: string): string {
  const d = new Date(ts)
  return d.toLocaleString('en-GB', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  })
}

export function SessionsPage({ onClose }: SessionsPageProps) {
  const { user } = useAuth()
  const [sessions, setSessions] = useState<Session[]>([])
  const [loading, setLoading] = useState(true)
  const [revoking, setRevoking] = useState<string | null>(null)

  const isAdmin = user?.role === 'Admin'

  const loadSessions = useCallback(async () => {
    try {
      const endpoint = isAdmin ? '/api/v1/admin/sessions' : '/api/v1/sessions'
      const headers: Record<string, string> = {}
      const refreshToken = getStoredRefreshToken()
      if (refreshToken) {
        headers['X-Current-Token'] = refreshToken
      }
      const res = await apiFetch(endpoint, { headers })
      if (res.ok) {
        setSessions(await res.json())
      }
    } catch {
      // ignore
    } finally {
      setLoading(false)
    }
  }, [isAdmin])

  useEffect(() => {
    loadSessions()
  }, [loadSessions])

  const handleRevoke = async (familyId: string) => {
    setRevoking(familyId)
    try {
      const endpoint = isAdmin
        ? `/api/v1/admin/sessions/${familyId}`
        : `/api/v1/sessions/${familyId}`
      await apiFetch(endpoint, { method: 'DELETE' })
      await loadSessions()
    } catch {
      // ignore
    } finally {
      setRevoking(null)
    }
  }

  return (
    <div className="absolute inset-0 z-50 flex items-center justify-center bg-slate-950/90">
      <div
        className="w-full max-w-4xl border border-slate-700 bg-slate-900 shadow-2xl"
        style={{ borderRadius: '2px' }}
      >
        {/* Header */}
        <div className="flex items-center justify-between border-b border-slate-700 px-6 py-4">
          <h2 className="font-mono text-sm font-bold uppercase tracking-widest text-slate-200">
            Active Sessions
          </h2>
          <button
            onClick={onClose}
            className="font-mono text-xs uppercase tracking-widest text-slate-500 transition-colors hover:text-slate-200"
          >
            CLOSE
          </button>
        </div>

        {/* Content */}
        <div className="max-h-[60vh] overflow-auto p-6">
          {loading ? (
            <div className="flex items-center justify-center py-12">
              <span className="animate-pulse font-mono text-xs uppercase tracking-widest text-slate-500">
                Loading sessions...
              </span>
            </div>
          ) : sessions.length === 0 ? (
            <div className="flex items-center justify-center py-12">
              <span className="font-mono text-xs uppercase tracking-widest text-slate-500">
                No sessions found
              </span>
            </div>
          ) : (
            <table className="w-full">
              <thead>
                <tr className="border-b border-slate-700">
                  {isAdmin && (
                    <th className="px-3 py-2 text-left font-mono text-xs font-normal uppercase tracking-widest text-slate-500">
                      User
                    </th>
                  )}
                  <th className="px-3 py-2 text-left font-mono text-xs font-normal uppercase tracking-widest text-slate-500">
                    Device
                  </th>
                  <th className="px-3 py-2 text-left font-mono text-xs font-normal uppercase tracking-widest text-slate-500">
                    Created
                  </th>
                  <th className="px-3 py-2 text-left font-mono text-xs font-normal uppercase tracking-widest text-slate-500">
                    Last Used
                  </th>
                  <th className="px-3 py-2 text-left font-mono text-xs font-normal uppercase tracking-widest text-slate-500">
                    Status
                  </th>
                  <th className="px-3 py-2 text-right font-mono text-xs font-normal uppercase tracking-widest text-slate-500">
                    Action
                  </th>
                </tr>
              </thead>
              <tbody>
                {sessions.map((session) => (
                  <tr
                    key={session.familyId}
                    className={`border-b border-slate-800 transition-colors hover:bg-slate-800/50 ${
                      session.isCurrent ? 'bg-slate-800/30' : ''
                    }`}
                  >
                    {isAdmin && (
                      <td className="px-3 py-3 font-mono text-xs text-slate-400">
                        {session.userEmail}
                      </td>
                    )}
                    <td className="px-3 py-3">
                      <div className="flex items-center gap-2">
                        <span className="font-mono text-xs text-slate-300">
                          {parseDeviceInfo(session.deviceInfo)}
                        </span>
                        {session.isCurrent && (
                          <span
                            className="bg-emerald-900/50 px-1.5 py-0.5 font-mono text-[10px] font-bold uppercase text-emerald-400 border border-emerald-700"
                            style={{ borderRadius: '2px' }}
                          >
                            Current
                          </span>
                        )}
                      </div>
                    </td>
                    <td className="px-3 py-3 font-mono text-xs text-slate-500">
                      {formatTimestamp(session.createdAt)}
                    </td>
                    <td className="px-3 py-3 font-mono text-xs text-slate-500">
                      {formatTimestamp(session.lastUsedAt)}
                    </td>
                    <td className="px-3 py-3">
                      {session.isActive ? (
                        <span className="font-mono text-xs font-bold uppercase text-emerald-400">
                          Active
                        </span>
                      ) : (
                        <span className="font-mono text-xs uppercase text-slate-600">
                          Revoked
                        </span>
                      )}
                    </td>
                    <td className="px-3 py-3 text-right">
                      {session.isActive && !session.isCurrent ? (
                        <button
                          onClick={() => handleRevoke(session.familyId)}
                          disabled={revoking === session.familyId}
                          className="font-mono text-xs uppercase tracking-widest text-slate-500 transition-colors hover:text-red-400 disabled:opacity-50"
                        >
                          {revoking === session.familyId ? 'REVOKING...' : 'REVOKE'}
                        </button>
                      ) : session.isCurrent ? (
                        <span className="font-mono text-[10px] uppercase text-slate-600">
                          This session
                        </span>
                      ) : null}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>

        {/* Footer */}
        <div className="border-t border-slate-700 px-6 py-3">
          <span className="font-mono text-[10px] uppercase tracking-widest text-slate-600">
            {sessions.filter((s) => s.isActive).length} active of {sessions.length} total sessions
          </span>
        </div>
      </div>
    </div>
  )
}
