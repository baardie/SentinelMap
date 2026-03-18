import { theme } from '@/styles/theme'

type ClassificationLevel = 'official' | 'officialSensitive' | 'secret'

interface ClassificationBannerProps {
  level: ClassificationLevel
}

export function ClassificationBanner({ level }: ClassificationBannerProps) {
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
