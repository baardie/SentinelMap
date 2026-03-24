import { useState, useEffect } from 'react'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import type { TrackUpdate, TrackFeature, TrackProperties, AlertNotification, VesselType, AircraftType } from '../types'

export type ConnectionStatus = 'connected' | 'reconnecting' | 'disconnected'

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
      staleness: 0,
      emergency: update.emergency ?? null,
      isMilitary: update.isMilitary ?? false,
    } satisfies TrackProperties,
  }
}

/**
 * Connects to the SignalR TrackHub at /hubs/tracks.
 * Returns the current set of track features, incoming alert notifications,
 * track history positions, and connection status.
 */
export function useTrackHub(): {
  tracks: TrackFeature[]
  alerts: AlertNotification[]
  trackHistory: Map<string, [number, number][]>
  connectionStatus: ConnectionStatus
} {
  const [tracks, setTracks] = useState<Map<string, TrackFeature>>(new Map())
  const [alerts, setAlerts] = useState<AlertNotification[]>([])
  const [trackHistory, setTrackHistory] = useState<Map<string, [number, number][]>>(new Map())
  const [connectionStatus, setConnectionStatus] = useState<ConnectionStatus>('disconnected')

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/tracks')
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    connection.onreconnecting(() => setConnectionStatus('reconnecting'))
    connection.onreconnected(() => setConnectionStatus('connected'))
    connection.onclose(() => setConnectionStatus('disconnected'))

    connection.on('TrackUpdate', (update: TrackUpdate) => {
      setTracks(prev => {
        const next = new Map(prev)
        next.set(update.entityId, trackUpdateToFeature(update))
        return next
      })
      setTrackHistory(prev => {
        const next = new Map(prev)
        const history = next.get(update.entityId) ?? []
        const newHistory = [...history, update.position]
        if (newHistory.length > 100) newHistory.shift()
        next.set(update.entityId, newHistory as [number, number][])
        return next
      })
    })

    connection.on('AlertTriggered', (alert: AlertNotification) => {
      setAlerts(prev => [alert, ...prev].slice(0, 100))
    })

    connection.start()
      .then(() => setConnectionStatus('connected'))
      .catch(err => {
        console.warn('TrackHub connection failed:', err)
        setConnectionStatus('disconnected')
      })

    return () => {
      connection.stop()
    }
  }, [])

  // Recalculate staleness every 10 seconds
  useEffect(() => {
    const interval = setInterval(() => {
      setTracks(prev => {
        const next = new Map(prev)
        const now = Date.now()
        for (const [id, feature] of next) {
          const age = (now - new Date(feature.properties.lastUpdated).getTime()) / (5 * 60 * 1000)
          const updated: TrackFeature = {
            ...feature,
            properties: {
              ...feature.properties,
              staleness: Math.min(1, Math.max(0, age)),
            },
          }
          next.set(id, updated)
        }
        return next
      })
    }, 10000)
    return () => clearInterval(interval)
  }, [])

  return {
    tracks: Array.from(tracks.values()),
    alerts,
    trackHistory,
    connectionStatus,
  }
}
