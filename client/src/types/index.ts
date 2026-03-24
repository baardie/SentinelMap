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

export interface CorrelationReview {
  id: string
  sourceEntityId: string
  sourceName: string | null
  sourceType: string | null
  targetEntityId: string
  targetName: string | null
  targetType: string | null
  confidence: number
  ruleScores: string | null
  status: string
  createdAt: string
}

export interface EntityDetail {
  id: string
  type: string
  displayName: string
  status: string
  lastKnownPosition: { longitude: number; latitude: number } | null
  lastSpeedKnots: number | null
  lastHeading: number | null
  lastSeen: string | null
  identifiers: { type: string; value: string; source: string }[]
  enrichment: {
    vesselType: string | null
    aircraftType: string | null
    photoUrl: string | null
    externalUrl: string | null
    flag: string | null
    imo: string | null
    callsign: string | null
    registration: string | null
    squawk: string | null
    altitude: number | null
  }
}

export interface TrackPosition {
  longitude: number
  latitude: number
  heading: number | null
  speedKnots: number | null
  observedAt: string
}

export interface TrackHistoryResponse {
  entityId: string
  positions: TrackPosition[]
  totalCount: number
}

export interface MapFeatureData {
  id: string
  featureType: string
  name: string
  longitude: number
  latitude: number
  icon: string | null
  color: string | null
  details: string | null
  source: string
  isActive: boolean
}
