import { useRef } from 'react'
import { AuthProvider, useAuth } from '@/contexts/AuthContext'
import { ClassificationBanner } from '@/components/layout/ClassificationBanner'
import { TopBar } from '@/components/layout/TopBar'
import { StatusBar } from '@/components/layout/StatusBar'
import { MapContainer } from '@/components/map/MapContainer'
import { AlertFeed } from '@/components/alerts/AlertFeed'
import { useTrackHub } from '@/hooks/useTrackHub'
import { LoginPage } from '@/pages/LoginPage'
import type { MapContainerHandle } from '@/components/map/MapContainer'

function LoadingScreen() {
  return (
    <div className="flex h-screen flex-col bg-slate-950">
      <ClassificationBanner />
      <div className="flex flex-1 items-center justify-center">
        <span className="font-mono text-xs tracking-widest text-slate-500 uppercase animate-pulse">
          INITIALISING…
        </span>
      </div>
    </div>
  )
}

function CopLayout() {
  const { tracks, alerts } = useTrackHub()
  const mapRef = useRef<MapContainerHandle>(null)

  const handleAlertClick = (entityId: string) => {
    mapRef.current?.flyToEntity(entityId)
  }

  return (
    <div className="flex h-screen flex-col bg-slate-950 text-slate-100">
      <ClassificationBanner />
      <TopBar />
      <main className="flex-1 overflow-hidden">
        <MapContainer ref={mapRef} tracks={tracks} />
      </main>
      <AlertFeed alerts={alerts} onAlertClick={handleAlertClick} />
      <StatusBar />
    </div>
  )
}

function AppContent() {
  const { isAuthenticated, isLoading } = useAuth()

  if (isLoading) return <LoadingScreen />
  if (!isAuthenticated) return <LoginPage />
  return <CopLayout />
}

export default function App() {
  return (
    <AuthProvider>
      <AppContent />
    </AuthProvider>
  )
}
