export type ClassificationLevel = 'official' | 'officialSensitive' | 'secret'

export type EntityType = 'Vessel' | 'Aircraft' | 'Unknown'

export type EntityStatus = 'Active' | 'Stale' | 'Dark' | 'Lost'

export interface TrackUpdate {
  entityId: string
  position: [number, number]
  heading: number
  speed: number
  entityType: EntityType
  timestamp: string
}
