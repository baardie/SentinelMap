import { useToast } from '@/contexts/ToastContext'
import type { Toast } from '@/contexts/ToastContext'

const severityBorder: Record<Toast['severity'], string> = {
  success: 'border-green-600',
  error: 'border-red-600',
  info: 'border-sky-600',
}

export function ToastContainer() {
  const { toasts, dismissToast } = useToast()

  return (
    <div className="fixed top-4 right-4 z-50 flex flex-col gap-2 pointer-events-none">
      {toasts.map(toast => (
        <div
          key={toast.id}
          className={`
            pointer-events-auto
            flex items-center justify-between gap-3
            bg-slate-900 border-l-4 border border-slate-700 px-4 py-2
            font-mono text-xs uppercase tracking-widest text-slate-200
            shadow-lg
            animate-slide-in
            ${severityBorder[toast.severity]}
          `}
          style={{ borderRadius: '2px', minWidth: '260px', maxWidth: '360px' }}
        >
          <span className="truncate">{toast.message}</span>
          <button
            onClick={() => dismissToast(toast.id)}
            className="text-slate-400 hover:text-slate-100 text-base leading-none flex-shrink-0"
            aria-label="Dismiss"
          >
            ×
          </button>
        </div>
      ))}
    </div>
  )
}
