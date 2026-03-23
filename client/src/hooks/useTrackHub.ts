import { useState, useEffect } from 'react'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import type { TrackUpdate, TrackFeature, TrackProperties, AlertNotification, VesselType, AircraftType } from '../types'

function trackUpdateToFeature(update: TrackUpdate): TrackFeature {
  return {
    type: 'Feature',
    geometry: {
      type: 'Point',
      coordinates: update.position,
    },
    properties: {
      entityId: update.entityId,
      heading: update.heading,
      speed: update.speed,
      entityType: update.entityType,
      status: update.status,
      vesselType: (update.vesselType as VesselType) ?? 'Unknown',
      aircraftType: (update.aircraftType as AircraftType) ?? 'Unknown',
      displayName: update.displayName ?? '',
      lastUpdated: update.timestamp,
    } satisfies TrackProperties,
  }
}

/**
 * Connects to the SignalR TrackHub at /hubs/tracks.
 * Returns the current set of track features and incoming alert notifications.
 */
export function useTrackHub(): { tracks: TrackFeature[]; alerts: AlertNotification[] } {
  const [tracks, setTracks] = useState<Map<string, TrackFeature>>(new Map())
  const [alerts, setAlerts] = useState<AlertNotification[]>([])

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/tracks')
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('TrackUpdate', (update: TrackUpdate) => {
      setTracks(prev => {
        const next = new Map(prev)
        next.set(update.entityId, trackUpdateToFeature(update))
        return next
      })
    })

    connection.on('AlertTriggered', (alert: AlertNotification) => {
      setAlerts(prev => [alert, ...prev].slice(0, 100))
    })

    connection.start().catch(err => {
      console.warn('TrackHub connection failed:', err)
    })

    return () => {
      connection.stop()
    }
  }, [])

  return { tracks: Array.from(tracks.values()), alerts }
}
