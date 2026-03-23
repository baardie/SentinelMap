import { useState, useRef, useEffect } from 'react'
import { apiFetch } from '../../lib/api'

export function ExportButton() {
  const [open, setOpen] = useState(false)
  const [exporting, setExporting] = useState(false)
  const dropdownRef = useRef<HTMLDivElement>(null)

  // Close dropdown when clicking outside
  useEffect(() => {
    if (!open) return
    const handler = (e: MouseEvent) => {
      if (dropdownRef.current && !dropdownRef.current.contains(e.target as Node)) {
        setOpen(false)
      }
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [open])

  const handleExport = async (format: 'csv' | 'geojson') => {
    setOpen(false)
    setExporting(true)
    try {
      const res = await apiFetch('/api/v1/export', {
        method: 'POST',
        body: JSON.stringify({ format, entityIds: null }),
      })

      if (!res.ok) {
        console.error('Export failed:', res.status, res.statusText)
        return
      }

      const blob = await res.blob()
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = `sentinelmap-export-${Date.now()}.${format === 'csv' ? 'csv' : 'geojson'}`
      document.body.appendChild(a)
      a.click()
      document.body.removeChild(a)
      URL.revokeObjectURL(url)
    } catch (err) {
      console.error('Export error:', err)
    } finally {
      setExporting(false)
    }
  }

  return (
    <div ref={dropdownRef} className="relative">
      <button
        onClick={() => setOpen(v => !v)}
        disabled={exporting}
        className={`px-2 py-1 font-mono text-xs tracking-widest border transition-colors ${
          open
            ? 'bg-slate-700 border-slate-500 text-slate-200'
            : 'bg-slate-900 border-slate-700 text-slate-500 hover:text-slate-300'
        } disabled:opacity-40 disabled:cursor-not-allowed`}
        style={{ borderRadius: '2px' }}
        title="Export entity data"
      >
        {exporting ? 'EXPORTING…' : 'EXPORT'}
      </button>

      {open && (
        <div
          className="absolute left-0 top-full mt-1 bg-slate-900 border border-slate-700 flex flex-col z-20"
          style={{ minWidth: '110px', borderRadius: '2px' }}
        >
          <button
            onClick={() => handleExport('csv')}
            className="px-3 py-1.5 font-mono text-xs text-slate-400 hover:text-slate-100 hover:bg-slate-800 text-left transition-colors"
          >
            CSV
          </button>
          <button
            onClick={() => handleExport('geojson')}
            className="px-3 py-1.5 font-mono text-xs text-slate-400 hover:text-slate-100 hover:bg-slate-800 text-left transition-colors border-t border-slate-800"
          >
            GEOJSON
          </button>
        </div>
      )}
    </div>
  )
}
