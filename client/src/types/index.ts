import type { Feature, Point } from 'geojson'

export type ClassificationLevel = 'official' | 'officialSensitive' | 'secret'

export type EntityType = 'Vessel' | 'Aircraft' | 'Unknown'

export type EntityStatus = 'Active' | 'Stale' | 'Dark' | 'Lost'

export type VesselType = 'Cargo' | 'Tanker' | 'Passenger' | 'Fishing' | 'Unknown'

export type AircraftType = 'Commercial' | 'Cargo' | 'Private' | 'Military' | 'Helicopter' | 'Unknown'

export interface TrackUpdate {
  entityId: string
  position: [number, number]
  heading: number | null
  speed: number | null
  entityType: EntityType
  status: EntityStatus
  timestamp: string
  displayName: string | null
  vesselType: string | null
  aircraftType: string | null
}

export interface TrackProperties {
  entityId: string
  heading: number | null
  speed: number | null
  entityType: EntityType
  status: EntityStatus
  vesselType: VesselType
  aircraftType: AircraftType
  displayName: string
  lastUpdated: string
  staleness: number
}

export type TrackFeature = Feature<Point, TrackProperties>

export interface AlertNotification {
  alertId: string
  type: 'GeofenceBreach' | 'WatchlistMatch' | 'AisDark' | 'SpeedAnomaly' | 'TransponderSwap' | 'CorrelationLink'
  severity: 'Low' | 'Medium' | 'High' | 'Critical'
  entityId: string | null
  summary: string
  createdAt: string
}

export interface GeofenceData {
  id: string
  name: string
  coordinates: number[][]
  fenceType: string
  isActive: boolean
  color?: string
}
