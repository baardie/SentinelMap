import { useRef } from 'react'
import { ClassificationBanner } from '@/components/layout/ClassificationBanner'
import { TopBar } from '@/components/layout/TopBar'
import { StatusBar } from '@/components/layout/StatusBar'
import { MapContainer } from '@/components/map/MapContainer'
import { AlertFeed } from '@/components/alerts/AlertFeed'
import { useTrackHub } from '@/hooks/useTrackHub'
import type { MapContainerHandle } from '@/components/map/MapContainer'

export default function App() {
  const { tracks, alerts } = useTrackHub()
  const mapRef = useRef<MapContainerHandle>(null)

  const handleAlertClick = (entityId: string) => {
    mapRef.current?.flyToEntity(entityId)
  }

  return (
    <div className="flex h-screen flex-col bg-slate-950 text-slate-100">
      <ClassificationBanner level="official" />
      <TopBar />
      <main className="flex-1 overflow-hidden">
        <MapContainer ref={mapRef} tracks={tracks} />
      </main>
      <AlertFeed alerts={alerts} onAlertClick={handleAlertClick} />
      <StatusBar />
    </div>
  )
}
