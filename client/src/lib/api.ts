let accessToken: string | null = null
let refreshToken: string | null = null

export function setTokens(access: string | null, refresh: string | null) {
  accessToken = access
  refreshToken = refresh
  if (refresh) localStorage.setItem('refreshToken', refresh)
  else localStorage.removeItem('refreshToken')
}

export function getStoredRefreshToken(): string | null {
  return localStorage.getItem('refreshToken')
}

export function getAccessToken(): string | null {
  return accessToken
}

export async function apiFetch(path: string, options: RequestInit = {}): Promise<Response> {
  const headers = new Headers(options.headers)
  if (accessToken) headers.set('Authorization', `Bearer ${accessToken}`)
  if (!headers.has('Content-Type') && options.body) headers.set('Content-Type', 'application/json')

  const res = await fetch(path, { ...options, headers })

  if (res.status === 401 && refreshToken) {
    // Try refresh
    const refreshRes = await fetch('/api/v1/auth/refresh', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken }),
    })
    if (refreshRes.ok) {
      const data = await refreshRes.json()
      setTokens(data.accessToken, data.refreshToken)
      headers.set('Authorization', `Bearer ${data.accessToken}`)
      return fetch(path, { ...options, headers })
    }
    // Refresh failed — clear tokens
    setTokens(null, null)
  }

  return res
}
