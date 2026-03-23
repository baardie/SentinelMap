import { createContext, useContext, useState, useCallback, type ReactNode } from 'react'

export interface Toast {
  id: string
  message: string
  severity: 'success' | 'error' | 'info'
}

interface ToastContextType {
  showToast: (message: string, severity?: 'success' | 'error' | 'info') => void
  toasts: Toast[]
  dismissToast: (id: string) => void
}

const ToastContext = createContext<ToastContextType>(null!)

export function useToast(): ToastContextType {
  return useContext(ToastContext)
}

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([])

  const dismissToast = useCallback((id: string) => {
    setToasts(prev => prev.filter(t => t.id !== id))
  }, [])

  const showToast = useCallback((message: string, severity: 'success' | 'error' | 'info' = 'info') => {
    const id = `${Date.now()}-${Math.random().toString(36).slice(2)}`
    setToasts(prev => {
      const next = [...prev, { id, message, severity }]
      return next.slice(-5) // max 5 visible
    })
    setTimeout(() => {
      setToasts(prev => prev.filter(t => t.id !== id))
    }, 3000)
  }, [])

  return (
    <ToastContext.Provider value={{ showToast, toasts, dismissToast }}>
      {children}
    </ToastContext.Provider>
  )
}
