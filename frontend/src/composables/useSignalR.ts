import { ref, onUnmounted } from 'vue'
import * as signalR from '@microsoft/signalr'
import type { TelemetrySnapshot } from '@/services/api'

const BASE_URL = import.meta.env.VITE_API_URL ?? ''

export function useSignalR(onTelemetryUpdated: (snapshot: TelemetrySnapshot) => void) {
  const connected = ref(false)
  let connection: signalR.HubConnection | null = null

  async function start(vehicleId: number) {
    connection = new signalR.HubConnectionBuilder()
      .withUrl(`${BASE_URL}/hubs/telemetry`, { withCredentials: true })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build()

    connection.on('telemetryUpdated', (snapshot: TelemetrySnapshot) => {
      onTelemetryUpdated(snapshot)
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
