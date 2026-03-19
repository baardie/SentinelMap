import type { Feature, Point } from 'geojson'

export type ClassificationLevel = 'official' | 'officialSensitive' | 'secret'

export type EntityType = 'Vessel' | 'Aircraft' | 'Unknown'

export type EntityStatus = 'Active' | 'Stale' | 'Dark' | 'Lost'

export type VesselType = 'Cargo' | 'Tanker' | 'Passenger' | 'Fishing' | 'Unknown'

export interface TrackUpdate {
  entityId: string
  position: [number, number]
  heading: number | null
  speed: number | null
  entityType: EntityType
  status: EntityStatus
  timestamp: string
}

export interface TrackProperties {
  entityId: string
  heading: number | null
  speed: number | null
  entityType: EntityType
  status: EntityStatus
  vesselType: VesselType
  displayName: string
  lastUpdated: string
}

export type TrackFeature = Feature<Point, TrackProperties>
