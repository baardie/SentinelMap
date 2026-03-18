import { ClassificationBanner } from '@/components/layout/ClassificationBanner'
import { TopBar } from '@/components/layout/TopBar'
import { StatusBar } from '@/components/layout/StatusBar'
import { MapContainer } from '@/components/map/MapContainer'

export default function App() {
  return (
    <div className="flex h-screen flex-col bg-slate-950 text-slate-100">
      <ClassificationBanner level="official" />
      <TopBar />
      <main className="flex-1">
        <MapContainer />
      </main>
      <StatusBar />
    </div>
  )
}
