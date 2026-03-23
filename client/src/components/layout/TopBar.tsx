import { useAuth } from '@/contexts/AuthContext'

export function TopBar() {
  const { user, logout } = useAuth()

  return (
    <div className="flex h-10 items-center justify-between border-b border-slate-800 bg-slate-900 px-4">
      <div className="flex items-center gap-4">
        <span className="text-sm text-slate-400">Search</span>
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
