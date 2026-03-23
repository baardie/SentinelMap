import { useCallback, useEffect, useState } from 'react'
import { apiFetch } from '@/lib/api'
import type { CorrelationReview } from '@/types'

interface RuleScore {
  RuleId: string
  Confidence: number
  Reason: string
}

function ConfidenceBadge({ confidence }: { confidence: number }) {
  const pct = Math.round(confidence * 100)
  let color = 'text-red-400'
  if (pct >= 50) color = 'text-green-400'
  else if (pct >= 40) color = 'text-amber-400'

  return (
    <span className={`font-mono text-xs font-bold ${color}`}>
      {pct}%
    </span>
  )
}

function ReviewCard({
  review,
  onApprove,
  onReject,
  isActioning,
}: {
  review: CorrelationReview
  onApprove: (id: string) => void
  onReject: (id: string) => void
  isActioning: boolean
}) {
  const [expanded, setExpanded] = useState(false)
  let ruleScores: RuleScore[] = []
  try {
    if (review.ruleScores) ruleScores = JSON.parse(review.ruleScores)
  } catch { /* ignore */ }

  return (
    <div className="border border-slate-700 bg-slate-800 p-3 mb-2">
      <div className="flex items-start justify-between gap-2 mb-2">
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 mb-1">
            <span className="font-mono text-[10px] text-slate-500 uppercase tracking-wider">SOURCE</span>
            <span className="font-mono text-xs text-slate-200 truncate">
              {review.sourceName ?? 'Unknown'}
            </span>
            <span className="font-mono text-[10px] text-slate-500 bg-slate-700 px-1">
              {review.sourceType}
            </span>
          </div>
          <div className="font-mono text-[10px] text-slate-600 mb-2 truncate">{review.sourceEntityId}</div>

          <div className="flex items-center gap-2 mb-1">
            <span className="font-mono text-[10px] text-slate-500 uppercase tracking-wider">TARGET</span>
            <span className="font-mono text-xs text-slate-200 truncate">
              {review.targetName ?? 'Unknown'}
            </span>
            <span className="font-mono text-[10px] text-slate-500 bg-slate-700 px-1">
              {review.targetType}
            </span>
          </div>
          <div className="font-mono text-[10px] text-slate-600 truncate">{review.targetEntityId}</div>
        </div>
        <div className="flex flex-col items-end gap-1">
          <ConfidenceBadge confidence={review.confidence} />
          <span className="font-mono text-[10px] text-slate-600">
            {new Date(review.createdAt).toLocaleTimeString()}
          </span>
        </div>
      </div>

      {ruleScores.length > 0 && (
        <div className="mb-2">
          <button
            onClick={() => setExpanded(!expanded)}
            className="font-mono text-[10px] text-slate-500 hover:text-slate-300 uppercase tracking-wider"
          >
            {expanded ? '- HIDE RULES' : '+ SHOW RULES'}
          </button>
          {expanded && (
            <div className="mt-1 border-t border-slate-700 pt-1">
              {ruleScores.map((s, i) => (
                <div key={i} className="flex items-center justify-between py-0.5">
                  <span className="font-mono text-[10px] text-slate-400">{s.RuleId}</span>
                  <div className="flex items-center gap-2">
                    <span className="font-mono text-[10px] text-slate-500">{s.Reason}</span>
                    <ConfidenceBadge confidence={s.Confidence} />
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      <div className="flex gap-2">
        <button
          onClick={() => onApprove(review.id)}
          disabled={isActioning}
          className="flex-1 bg-green-900/50 border border-green-700 text-green-300 font-mono text-xs py-1 hover:bg-green-800/50 disabled:opacity-50 transition-colors"
        >
          APPROVE
        </button>
        <button
          onClick={() => onReject(review.id)}
          disabled={isActioning}
          className="flex-1 bg-red-900/50 border border-red-700 text-red-300 font-mono text-xs py-1 hover:bg-red-800/50 disabled:opacity-50 transition-colors"
        >
          REJECT
        </button>
      </div>
    </div>
  )
}

interface CorrelationReviewPanelProps {
  isOpen: boolean
  onClose: () => void
  onCountChange: (count: number) => void
}

export function CorrelationReviewPanel({ isOpen, onClose, onCountChange }: CorrelationReviewPanelProps) {
  const [reviews, setReviews] = useState<CorrelationReview[]>([])
  const [actioningId, setActioningId] = useState<string | null>(null)

  const loadReviews = useCallback(() => {
    apiFetch('/api/v1/correlations/pending')
      .then(r => r.json())
      .then((data: CorrelationReview[]) => {
        setReviews(data)
        onCountChange(data.length)
      })
      .catch(() => {})
  }, [onCountChange])

  useEffect(() => {
    loadReviews()
    const interval = setInterval(loadReviews, 30_000)
    return () => clearInterval(interval)
  }, [loadReviews])

  const handleApprove = async (id: string) => {
    setActioningId(id)
    try {
      const res = await apiFetch(`/api/v1/correlations/${id}/approve`, { method: 'POST' })
      if (res.ok) {
        setReviews(prev => prev.filter(r => r.id !== id))
        onCountChange(reviews.length - 1)
      }
    } finally {
      setActioningId(null)
    }
  }

  const handleReject = async (id: string) => {
    setActioningId(id)
    try {
      const res = await apiFetch(`/api/v1/correlations/${id}/reject`, { method: 'POST' })
      if (res.ok) {
        setReviews(prev => prev.filter(r => r.id !== id))
        onCountChange(reviews.length - 1)
      }
    } finally {
      setActioningId(null)
    }
  }

  if (!isOpen) return null

  return (
    <div className="absolute top-10 right-0 z-50 w-96 max-h-[80vh] flex flex-col bg-slate-900 border border-slate-700 shadow-2xl">
      <div className="flex items-center justify-between border-b border-slate-700 px-3 py-2">
        <div className="flex items-center gap-2">
          <span className="font-mono text-xs font-bold text-slate-200 uppercase tracking-wider">
            CORRELATION REVIEW QUEUE
          </span>
          {reviews.length > 0 && (
            <span className="font-mono text-[10px] font-bold bg-amber-600 text-slate-900 px-1.5 py-0.5">
              {reviews.length}
            </span>
          )}
        </div>
        <button
          onClick={onClose}
          className="font-mono text-xs text-slate-500 hover:text-slate-300"
        >
          CLOSE
        </button>
      </div>

      <div className="flex-1 overflow-y-auto p-2">
        {reviews.length === 0 ? (
          <div className="flex items-center justify-center py-8">
            <span className="font-mono text-xs text-slate-600">NO PENDING REVIEWS</span>
          </div>
        ) : (
          reviews.map(review => (
            <ReviewCard
              key={review.id}
              review={review}
              onApprove={handleApprove}
              onReject={handleReject}
              isActioning={actioningId === review.id}
            />
          ))
        )}
      </div>
    </div>
  )
}
