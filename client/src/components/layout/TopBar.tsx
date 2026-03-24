import { useCallback, useState } from 'react'
import { useAuth } from '@/contexts/AuthContext'
import { CorrelationReviewPanel } from '@/components/correlation/CorrelationReviewPanel'
import { SafetyAlertsPanel } from '@/components/alerts/SafetyAlertsPanel'

interface TopBarProps {
  searchTerm: string
  onSearch: (term: string) => void
  onShowSessions?: () => void
}

export function TopBar({ searchTerm, onSearch, onShowSessions }: TopBarProps) {
  const { user, logout } = useAuth()
  const [reviewPanelOpen, setReviewPanelOpen] = useState(false)
  const [pendingCount, setPendingCount] = useState(0)
  const [safetyPanelOpen, setSafetyPanelOpen] = useState(false)
  const [safetyCount, setSafetyCount] = useState(0)

  const handleCountChange = useCallback((count: number) => {
    setPendingCount(count)
  }, [])

  const handleSafetyCountChange = useCallback((count: number) => {
    setSafetyCount(count)
  }, [])

  return (
    <div className="relative flex h-10 items-center justify-between border-b border-slate-800 bg-slate-900 px-4">
      <div className="flex items-center gap-4">
        <input
          type="text"
          placeholder="SEARCH ENTITIES..."
          className="bg-slate-800 border border-slate-600 px-3 py-1 text-xs font-mono text-slate-200 w-64 placeholder:text-slate-500 focus:outline-none focus:border-slate-400"
          style={{ borderRadius: '2px' }}
          value={searchTerm}
          onChange={e => onSearch(e.target.value)}
        />
      </div>
      <div className="flex items-center gap-4">
        {user && (
          <>
            <button
              onClick={() => { setSafetyPanelOpen(!safetyPanelOpen); setReviewPanelOpen(false) }}
              className="relative font-mono text-xs text-slate-400 hover:text-amber-300 uppercase tracking-widest transition-colors"
            >
              SAFETY
              {safetyCount > 0 && (
                <span className="ml-1 inline-block font-mono text-[10px] font-bold bg-amber-600 text-slate-900 px-1.5 py-0.5 min-w-[1.25rem] text-center" style={{ borderRadius: '2px' }}>
                  {safetyCount}
                </span>
              )}
            </button>
            <button
              onClick={() => { setReviewPanelOpen(!reviewPanelOpen); setSafetyPanelOpen(false) }}
              className="relative font-mono text-xs text-slate-400 hover:text-amber-300 uppercase tracking-widest transition-colors"
            >
              REVIEW
              {pendingCount > 0 && (
                <span className="ml-1 inline-block font-mono text-[10px] font-bold bg-amber-600 text-slate-900 px-1.5 py-0.5 min-w-[1.25rem] text-center" style={{ borderRadius: '2px' }}>
                  {pendingCount}
                </span>
              )}
            </button>
            <span className="font-mono text-xs text-slate-400">{user.email}</span>
            <span
              className="font-mono text-xs font-bold text-slate-300 bg-slate-700 px-2 py-0.5"
              style={{ borderRadius: '2px' }}
            >
              {user.role.toUpperCase()}
            </span>
            {onShowSessions && (
              <button
                onClick={onShowSessions}
                className="text-xs font-mono text-slate-400 hover:text-slate-200 uppercase tracking-widest transition-colors"
              >
                SESSIONS
              </button>
            )}
            <button
              onClick={() => void logout()}
              className="font-mono text-xs text-slate-400 hover:text-red-400 transition-colors"
            >
              LOGOUT
            </button>
          </>
        )}
      </div>
      <SafetyAlertsPanel
        isOpen={safetyPanelOpen}
        onClose={() => setSafetyPanelOpen(false)}
        onCountChange={handleSafetyCountChange}
      />
      <CorrelationReviewPanel
        isOpen={reviewPanelOpen}
        onClose={() => setReviewPanelOpen(false)}
        onCountChange={handleCountChange}
      />
    </div>
  )
}
