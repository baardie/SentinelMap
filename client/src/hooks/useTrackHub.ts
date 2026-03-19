import { useState, useEffect } from 'react'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import type { TrackUpdate, TrackFeature, TrackProperties } from '../types'

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
      vesselType: 'Unknown',
      displayName: '',
      lastUpdated: update.timestamp,
    } satisfies TrackProperties,
  }
}

/**
 * Connects to the SignalR TrackHub at /hubs/tracks.
 * Returns the current set of track features as a GeoJSON Feature array.
 */
export function useTrackHub(): TrackFeature[] {
  const [tracks, setTracks] = useState<Map<string, TrackFeature>>(new Map())

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

    connection.start().catch(err => {
      console.warn('TrackHub connection failed:', err)
    })

    return () => {
      connection.stop()
    }
  }, [])

  return Array.from(tracks.values())
}
