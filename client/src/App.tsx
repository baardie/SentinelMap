import { useCallback, useEffect, useRef, useState } from 'react'
import { AuthProvider, useAuth } from '@/contexts/AuthContext'
import { ClassificationBanner } from '@/components/layout/ClassificationBanner'
import { TopBar } from '@/components/layout/TopBar'
import { StatusBar } from '@/components/layout/StatusBar'
import { MapContainer } from '@/components/map/MapContainer'
import { AlertFeed } from '@/components/alerts/AlertFeed'
import { useTrackHub } from '@/hooks/useTrackHub'
import { LoginPage } from '@/pages/LoginPage'
import { apiFetch } from '@/lib/api'
import type { MapContainerHandle } from '@/components/map/MapContainer'
import type { GeofenceData } from '@/types'

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
  const { isAuthenticated } = useAuth()
  const { tracks, alerts, trackHistory, connectionStatus } = useTrackHub()
  const mapRef = useRef<MapContainerHandle>(null)
  const [searchTerm, setSearchTerm] = useState('')
  const [geofences, setGeofences] = useState<GeofenceData[]>([])

  const loadGeofences = useCallback(() => {
    if (!isAuthenticated) return
    apiFetch('/api/v1/geofences')
      .then(r => r.json())
      .then(setGeofences)
      .catch(() => {})
  }, [isAuthenticated])

  useEffect(() => {
    loadGeofences()
  }, [loadGeofences])

  const filteredTracks = searchTerm
    ? tracks.filter(t =>
        t.properties.displayName.toLowerCase().includes(searchTerm.toLowerCase()) ||
        t.properties.entityId.toLowerCase().includes(searchTerm.toLowerCase())
      )
    : tracks

  const vesselCount = tracks.filter(t => t.properties.entityType === 'Vessel').length
  const aircraftCount = tracks.filter(t => t.properties.entityType === 'Aircraft').length

  const handleAlertClick = (entityId: string) => {
    mapRef.current?.flyToEntity(entityId)
  }

  return (
    <div className="flex h-screen flex-col bg-slate-950 text-slate-100">
      <ClassificationBanner />
      <TopBar searchTerm={searchTerm} onSearch={setSearchTerm} />
      <main className="flex-1 overflow-hidden">
        <MapContainer
          ref={mapRef}
          tracks={filteredTracks}
          trackHistory={trackHistory}
          geofences={geofences}
          onGeofenceCreated={loadGeofences}
        />
      </main>
      <AlertFeed alerts={alerts} onAlertClick={handleAlertClick} />
      <StatusBar
        trackCount={tracks.length}
        vesselCount={vesselCount}
        aircraftCount={aircraftCount}
        alertCount={alerts.length}
        connectionStatus={connectionStatus}
      />
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
