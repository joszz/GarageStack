import { ref, onUnmounted } from 'vue'
import * as signalR from '@microsoft/signalr'
import type { TelemetrySnapshot, AppNotification } from '@/services/api'

const BASE_URL = import.meta.env.VITE_API_URL ?? ''

export interface SignalRCallbacks {
  onTelemetryUpdated: (snapshot: TelemetrySnapshot) => void
  onNotificationReceived: (notification: AppNotification) => void
  onTripCompleted: (vehicleId: number) => void
}

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
      // SignalR unavailable — dashboard falls back to polling
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
