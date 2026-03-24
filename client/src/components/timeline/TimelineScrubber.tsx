import { useCallback, useEffect, useRef, useState } from 'react'
import { apiFetch } from '../../lib/api'
import type { TrackHistoryResponse, TrackPosition } from '../../types'

interface TimelineScrubberProps {
  visible: boolean
  entityId: string | null
  onTimeChange: (time: Date) => void
  onTrackData: (data: TrackPosition[]) => void
  onClose: () => void
}

const SPEEDS = [1, 2, 5, 10] as const

function formatTimeShort(date: Date): string {
  return date.toLocaleTimeString(undefined, {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
}

export function TimelineScrubber({ visible, entityId, onTimeChange, onTrackData, onClose }: TimelineScrubberProps) {
  const [trackData, setTrackData] = useState<TrackPosition[]>([])
  const [playing, setPlaying] = useState(false)
  const [speedIndex, setSpeedIndex] = useState(0)
  const [sliderValue, setSliderValue] = useState(0)
  const [loading, setLoading] = useState(false)
  const animFrameRef = useRef<number>(0)
  const lastTickRef = useRef<number>(0)

  const speed = SPEEDS[speedIndex]

  // Compute time range from track data
  const startTime = trackData.length > 0 ? new Date(trackData[0].observedAt).getTime() : 0
  const endTime = trackData.length > 0 ? new Date(trackData[trackData.length - 1].observedAt).getTime() : 0
  const duration = endTime - startTime

  // Fetch track data when entityId changes
  useEffect(() => {
    if (!visible || !entityId) return

    setLoading(true)
    setPlaying(false)
    setSliderValue(0)

    const now = new Date()
    const from = new Date(now.getTime() - 60 * 60 * 1000) // 1 hour ago

    apiFetch(`/api/v1/entities/${entityId}/track?from=${from.toISOString()}&to=${now.toISOString()}`)
      .then(r => r.json())
      .then((data: TrackHistoryResponse) => {
        setTrackData(data.positions)
        onTrackData(data.positions)
        if (data.positions.length > 0) {
          onTimeChange(new Date(data.positions[0].observedAt))
        }
      })
      .catch(() => {
        setTrackData([])
        onTrackData([])
      })
      .finally(() => setLoading(false))
  }, [visible, entityId]) // eslint-disable-line react-hooks/exhaustive-deps

  // Playback animation
  useEffect(() => {
    if (!playing || duration <= 0) return

    lastTickRef.current = performance.now()

    const tick = (now: number) => {
      const elapsed = now - lastTickRef.current
      lastTickRef.current = now

      // Advance slider based on playback speed
      const advance = (elapsed / 1000) * speed * (1000 / duration) * 1000
      setSliderValue(prev => {
        const next = Math.min(1000, prev + advance)
        if (next >= 1000) {
          setPlaying(false)
          return 1000
        }
        return next
      })

      animFrameRef.current = requestAnimationFrame(tick)
    }

    animFrameRef.current = requestAnimationFrame(tick)

    return () => {
      if (animFrameRef.current) cancelAnimationFrame(animFrameRef.current)
    }
  }, [playing, speed, duration])

  // Emit time changes when slider moves
  useEffect(() => {
    if (duration <= 0) return
    const currentTime = new Date(startTime + (sliderValue / 1000) * duration)
    onTimeChange(currentTime)
  }, [sliderValue]) // eslint-disable-line react-hooks/exhaustive-deps

  const handleSliderChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    setSliderValue(Number(e.target.value))
  }, [])

  const togglePlay = useCallback(() => {
    if (sliderValue >= 1000) {
      setSliderValue(0)
    }
    setPlaying(p => !p)
  }, [sliderValue])

  const cycleSpeed = useCallback(() => {
    setSpeedIndex(i => (i + 1) % SPEEDS.length)
  }, [])

  if (!visible) return null

  const currentTimeStr = duration > 0
    ? formatTimeShort(new Date(startTime + (sliderValue / 1000) * duration))
    : '--:--:--'

  return (
    <div className="h-12 bg-slate-900 border-t border-slate-700 flex items-center px-3 gap-3 font-mono text-xs">
      {/* Play/Pause */}
      <button
        onClick={togglePlay}
        disabled={trackData.length === 0}
        className="text-slate-300 hover:text-slate-100 disabled:text-slate-600 w-6 text-center text-sm"
        aria-label={playing ? 'Pause' : 'Play'}
      >
        {playing ? '\u275A\u275A' : '\u25B6'}
      </button>

      {/* Speed */}
      <button
        onClick={cycleSpeed}
        className="text-slate-400 hover:text-slate-200 tracking-widest w-8 text-center"
        title="Playback speed"
      >
        {speed}x
      </button>

      {/* Timeline slider */}
      <div className="flex-1 flex flex-col justify-center relative">
        <input
          type="range"
          min={0}
          max={1000}
          value={sliderValue}
          onChange={handleSliderChange}
          disabled={trackData.length === 0}
          className="w-full h-1 appearance-none bg-slate-700 cursor-pointer disabled:cursor-not-allowed [&::-webkit-slider-thumb]:appearance-none [&::-webkit-slider-thumb]:w-3 [&::-webkit-slider-thumb]:h-3 [&::-webkit-slider-thumb]:bg-slate-300 [&::-webkit-slider-thumb]:border-0"
          style={{ borderRadius: 0 }}
        />
        <div className="flex justify-between mt-0.5">
          <span className="text-slate-500 text-[10px]">
            {duration > 0 ? formatTimeShort(new Date(startTime)) : ''}
          </span>
          <span className="text-slate-500 text-[10px]">
            {duration > 0 ? formatTimeShort(new Date(endTime)) : ''}
          </span>
        </div>
      </div>

      {/* Current time display */}
      <span className="text-slate-300 tracking-wider w-20 text-center">
        {loading ? 'LOADING' : currentTimeStr}
      </span>

      {/* Close button */}
      <button
        onClick={onClose}
        className="text-slate-400 hover:text-slate-100 text-lg leading-none ml-1"
        aria-label="Close timeline"
      >
        x
      </button>
    </div>
  )
}
