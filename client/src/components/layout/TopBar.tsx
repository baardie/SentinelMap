import { useAuth } from '@/contexts/AuthContext'

interface TopBarProps {
  searchTerm: string
  onSearch: (term: string) => void
}

export function TopBar({ searchTerm, onSearch }: TopBarProps) {
  const { user, logout } = useAuth()

  return (
    <div className="flex h-10 items-center justify-between border-b border-slate-800 bg-slate-900 px-4">
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
        <span className="font-mono text-xs text-slate-500">Simulated</span>
        <span className="text-sm text-slate-400">System Status</span>
        {user && (
          <>
            <span className="font-mono text-xs text-slate-400">{user.email}</span>
            <span
              className="font-mono text-xs font-bold text-slate-300 bg-slate-700 px-2 py-0.5"
              style={{ borderRadius: '2px' }}
            >
              {user.role.toUpperCase()}
            </span>
            <button
              onClick={() => void logout()}
              className="font-mono text-xs text-slate-400 hover:text-red-400 transition-colors"
            >
              LOGOUT
            </button>
          </>
        )}
      </div>
    </div>
  )
}
