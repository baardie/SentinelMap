import { useState, type FormEvent } from 'react'
import { ClassificationBanner } from '@/components/layout/ClassificationBanner'
import { useAuth } from '@/contexts/AuthContext'

export function LoginPage() {
  const { login } = useAuth()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setError(null)
    setIsSubmitting(true)
    try {
      await login(email, password)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Authentication failed')
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="flex h-screen flex-col bg-slate-950">
      <ClassificationBanner />
      <div className="flex flex-1 items-center justify-center">
        <div
          className="w-full max-w-sm border border-slate-700 bg-slate-900 p-8"
          style={{ borderRadius: '2px' }}
        >
          {/* Header */}
          <div className="mb-8 text-center">
            <h1 className="font-mono text-xl font-bold tracking-widest text-slate-300 uppercase">
              SENTINELMAP
            </h1>
            <p className="mt-1 font-mono text-xs tracking-wider text-slate-500 uppercase">
              COMMON OPERATING PICTURE
            </p>
          </div>

          {/* Form */}
          <form onSubmit={handleSubmit} className="flex flex-col gap-4">
            <div className="flex flex-col gap-1">
              <label
                htmlFor="email"
                className="font-mono text-xs tracking-wider text-slate-400 uppercase"
              >
                Email
              </label>
              <input
                id="email"
                type="email"
                value={email}
                onChange={e => setEmail(e.target.value)}
                required
                autoComplete="username"
                className="border border-slate-600 bg-slate-800 px-3 py-2 font-mono text-sm text-slate-200 outline-none focus:border-sky-600 focus:ring-0"
                style={{ borderRadius: '2px' }}
                placeholder="operator@sentinelmap.mil"
              />
            </div>

            <div className="flex flex-col gap-1">
              <label
                htmlFor="password"
                className="font-mono text-xs tracking-wider text-slate-400 uppercase"
              >
                Password
              </label>
              <input
                id="password"
                type="password"
                value={password}
                onChange={e => setPassword(e.target.value)}
                required
                autoComplete="current-password"
                className="border border-slate-600 bg-slate-800 px-3 py-2 font-mono text-sm text-slate-200 outline-none focus:border-sky-600 focus:ring-0"
                style={{ borderRadius: '2px' }}
              />
            </div>

            {error && (
              <p className="font-mono text-xs text-red-400">{error}</p>
            )}

            <button
              type="submit"
              disabled={isSubmitting}
              className="mt-2 bg-sky-700 px-4 py-2 font-mono text-sm font-bold tracking-widest text-white uppercase hover:bg-sky-600 disabled:cursor-not-allowed disabled:opacity-50"
              style={{ borderRadius: '2px' }}
            >
              {isSubmitting ? 'AUTHENTICATING…' : 'AUTHENTICATE'}
            </button>
          </form>
        </div>
      </div>
    </div>
  )
}
