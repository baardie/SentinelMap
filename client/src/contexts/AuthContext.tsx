import { createContext, useContext, useState, useEffect, useCallback, type ReactNode } from 'react'
import { setTokens, getStoredRefreshToken, getAccessToken } from '../lib/api'

interface User {
  email: string
  role: string
  clearance: 'official' | 'officialSensitive' | 'secret'
}

interface AuthContextType {
  isAuthenticated: boolean
  isLoading: boolean
  user: User | null
  accessToken: string | null
  login: (email: string, password: string) => Promise<void>
  logout: () => Promise<void>
}

export const AuthContext = createContext<AuthContextType>(null!)

export function useAuth() { return useContext(AuthContext) }

function parseJwt(token: string): Record<string, unknown> {
  const base64Url = token.split('.')[1]
  const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/')
  return JSON.parse(atob(base64))
}

function extractUser(token: string): User {
  const payload = parseJwt(token)
  const clearanceMap: Record<string, User['clearance']> = {
    'Official': 'official',
    'OfficialSensitive': 'officialSensitive',
    'Secret': 'secret',
  }
  return {
    email: (payload.email as string) ?? '',
    role: (payload.role as string) ?? 'Viewer',
    clearance: clearanceMap[(payload.clearance as string)] ?? 'official',
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null)
  const [isLoading, setIsLoading] = useState(true)

  const login = useCallback(async (email: string, password: string) => {
    const res = await fetch('/api/v1/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password }),
    })
    if (!res.ok) {
      const text = await res.text()
      throw new Error(text || 'Authentication failed')
    }
    const data = await res.json()
    setTokens(data.accessToken, data.refreshToken)
    setUser(extractUser(data.accessToken))
  }, [])

  const logout = useCallback(async () => {
    const token = getAccessToken()
    if (token) {
      try {
        await fetch('/api/v1/auth/revoke', {
          method: 'POST',
          headers: { 'Authorization': `Bearer ${token}` },
        })
      } catch { /* ignore */ }
    }
    setTokens(null, null)
    setUser(null)
  }, [])

  // Try to restore session from stored refresh token on mount
  useEffect(() => {
    const storedRefresh = getStoredRefreshToken()
    if (!storedRefresh) { setIsLoading(false); return }

    fetch('/api/v1/auth/refresh', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken: storedRefresh }),
    })
      .then(res => res.ok ? res.json() : null)
      .then(data => {
        if (data) {
          setTokens(data.accessToken, data.refreshToken)
          setUser(extractUser(data.accessToken))
        } else {
          setTokens(null, null)
        }
      })
      .catch(() => setTokens(null, null))
      .finally(() => setIsLoading(false))
  }, [])

  // Auto-refresh timer — refresh 1 minute before expiry (14 minutes after login)
  useEffect(() => {
    if (!user) return
    const timer = setInterval(async () => {
      const storedRefresh = getStoredRefreshToken()
      if (!storedRefresh) return
      try {
        const res = await fetch('/api/v1/auth/refresh', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ refreshToken: storedRefresh }),
        })
        if (res.ok) {
          const data = await res.json()
          setTokens(data.accessToken, data.refreshToken)
          setUser(extractUser(data.accessToken))
        }
      } catch { /* ignore */ }
    }, 14 * 60 * 1000)
    return () => clearInterval(timer)
  }, [user])

  return (
    <AuthContext.Provider value={{
      isAuthenticated: !!user,
      isLoading,
      user,
      accessToken: getAccessToken(),
      login,
      logout,
    }}>
      {children}
    </AuthContext.Provider>
  )
}
