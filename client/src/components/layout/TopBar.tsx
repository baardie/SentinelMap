export function TopBar() {
  return (
    <div className="flex h-10 items-center justify-between border-b border-slate-800 bg-slate-900 px-4">
      <div className="flex items-center gap-4">
        <span className="text-sm text-slate-400">Search</span>
      </div>
      <div className="flex items-center gap-4">
        <span className="font-mono text-xs text-slate-500">Simulated</span>
        <span className="text-sm text-slate-400">System Status</span>
      </div>
    </div>
  )
}
