import { useContext } from 'react'
import { theme } from '@/styles/theme'
import { AuthContext } from '@/contexts/AuthContext'

type ClassificationLevel = 'official' | 'officialSensitive' | 'secret'

export function ClassificationBanner() {
  // AuthContext may be null if rendered outside AuthProvider (shouldn't happen, but be safe)
  const auth = useContext(AuthContext)
  const level: ClassificationLevel = auth?.user?.clearance ?? 'official'
  const config = theme.classification[level]

  return (
    <div
      className="flex h-7 items-center justify-between px-4 font-mono text-xs font-bold tracking-wider"
      style={{ backgroundColor: config.bg, color: config.text }}
    >
      <span>{config.label}</span>
      <span>SentinelMap</span>
    </div>
  )
}
