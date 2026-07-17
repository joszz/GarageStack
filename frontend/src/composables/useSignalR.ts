import { ref, onUnmounted } from 'vue'
import * as signalR from '@microsoft/signalr'
import type { TelemetrySnapshot } from '@/services/vehicleApi'
import type { AppNotification } from '@/services/notificationsApi'

const BASE_URL = import.meta.env.VITE_API_URL ?? ''

export interface SignalRCallbacks {
  onTelemetryUpdated: (snapshot: TelemetrySnapshot) => void
  onNotificationReceived: (notification: AppNotification) => void
  onTripCompleted: (vehicleId: number) => void
}

/**
 * Owns the SignalR connection to the `/hubs/telemetry` hub: connecting, joining the given
 * vehicle's group, automatic reconnection, and dispatching the three server-pushed events
 * (telemetry, notifications, trip-completed) to caller-supplied callbacks. This is the app's
 * only real-time channel - there is no REST-polling fallback, so if `start()` fails or the
 * connection drops without reconnecting, the UI simply goes stale until `start()` is called
 * again (next mount or a manual reload).
 */
export function useSignalR(callbacks: SignalRCallbacks) {
  const connected = ref(false)
  let connection: signalR.HubConnection | null = null

  async function start(vehicleId: number) {
    if (connection) await stop()

    connection = new signalR.HubConnectionBuilder()
      .withUrl(`${BASE_URL}/hubs/telemetry`, { withCredentials: true })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build()

    connection.on('telemetryUpdated', (snapshot: TelemetrySnapshot) => {
      callbacks.onTelemetryUpdated(snapshot)
    })

    connection.on('notificationReceived', (notification: AppNotification) => {
      callbacks.onNotificationReceived(notification)
    })

    connection.on('tripCompleted', (vid: number) => {
      callbacks.onTripCompleted(vid)
    })

    connection.onreconnecting(() => {
      connected.value = false
    })
    connection.onreconnected(async () => {
      connected.value = true
      await connection!.invoke('JoinVehicle', vehicleId)
    })
    connection.onclose(() => {
      connected.value = false
    })

    try {
      await connection.start()
      await connection.invoke('JoinVehicle', vehicleId)
      connected.value = true
    } catch {
      // SignalR failed to start. There is no polling fallback anywhere in this app,
      // so connected stays false and the UI simply goes stale until start() is called
      // again (e.g. on next mount or a manual reload).
    }
  }

  async function stop() {
    if (connection) {
      await connection.stop()
      connection = null
      connected.value = false
    }
  }

  onUnmounted(stop)

  return { connected, start, stop }
}
