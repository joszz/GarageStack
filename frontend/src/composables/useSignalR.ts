import { ref, onUnmounted } from 'vue'
import * as signalR from '@microsoft/signalr'
import { apiUrl } from '@/services/apiCore'
import type { TelemetrySnapshot } from '@/services/vehicleApi'
import type { AppNotification } from '@/services/notificationsApi'

export interface SignalRCallbacks {
  onTelemetryUpdated: (snapshot: TelemetrySnapshot) => void
  onNotificationReceived: (notification: AppNotification) => void
  onTripCompleted: (vehicleId: number) => void
}

// Backoff between reconnect attempts, reused for both the automatic reconnect and our own
// restart loop. The last entry repeats for as long as the server stays unreachable.
const RETRY_DELAYS_MS = [0, 2_000, 5_000, 10_000, 30_000]

function retryDelay(attempt: number): number {
  return RETRY_DELAYS_MS[Math.min(attempt, RETRY_DELAYS_MS.length - 1)]!
}

/**
 * Owns the SignalR connection to the `/hubs/telemetry` hub: connecting, joining the given
 * vehicle's group, reconnection, and dispatching the three server-pushed events (telemetry,
 * notifications, trip-completed) to caller-supplied callbacks. This is the app's only real-time
 * channel - there is no REST-polling fallback, so it keeps retrying indefinitely rather than
 * giving up: SignalR's default policy stops after a handful of attempts, which left the
 * dashboard stale until a manual reload after something as ordinary as an API restart.
 */
export function useSignalR(callbacks: SignalRCallbacks) {
  const connected = ref(false)
  let connection: signalR.HubConnection | null = null
  let restartTimer: ReturnType<typeof setTimeout> | null = null
  let restartAttempt = 0
  // Set by stop() so a pending retry does not resurrect a connection the caller ended.
  let stopped = false

  function clearRestartTimer() {
    if (restartTimer !== null) {
      clearTimeout(restartTimer)
      restartTimer = null
    }
  }

  function scheduleRestart(vehicleId: number) {
    if (stopped || restartTimer !== null) return
    const delay = retryDelay(restartAttempt++)
    restartTimer = setTimeout(() => {
      restartTimer = null
      void openConnection(vehicleId)
    }, delay)
  }

  async function openConnection(vehicleId: number) {
    if (stopped || !connection) return
    try {
      await connection.start()
      await connection.invoke('JoinVehicle', vehicleId)
      connected.value = true
      restartAttempt = 0
    } catch {
      // The server is unreachable (still starting, restarting, or the network is down).
      // onclose does not fire for a failed start, so the retry is scheduled here.
      connected.value = false
      scheduleRestart(vehicleId)
    }
  }

  async function start(vehicleId: number) {
    if (connection) await stop()
    stopped = false
    restartAttempt = 0

    connection = new signalR.HubConnectionBuilder()
      .withUrl(apiUrl('/hubs/telemetry'), { withCredentials: true })
      // Never returns null, so the client keeps trying instead of giving up.
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (ctx) => retryDelay(ctx.previousRetryCount),
      })
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
      // Reached when the automatic reconnect itself fails to re-establish the connection.
      scheduleRestart(vehicleId)
    })

    await openConnection(vehicleId)
  }

  async function stop() {
    stopped = true
    clearRestartTimer()
    if (connection) {
      const closing = connection
      connection = null
      connected.value = false
      await closing.stop()
    }
  }

  onUnmounted(stop)

  return { connected, start, stop }
}
