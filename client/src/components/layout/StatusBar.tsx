export function StatusBar() {
  return (
    <div className="flex h-6 items-center justify-between border-t border-slate-800 bg-slate-950 px-4 font-mono text-xs text-slate-500">
      <span>Disconnected</span>
      <span>0 tracks</span>
      <span>—</span>
    </div>
  )
}
