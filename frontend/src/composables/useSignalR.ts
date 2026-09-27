import { ref, onUnmounted } from 'vue'
import type { HubConnection } from '@microsoft/signalr'
import { apiUrl } from '@/services/apiCore'
import type { CommandResult, TelemetrySnapshot } from '@/services/vehicleApi'
import type { AppNotification } from '@/services/notificationsApi'

export interface SignalRCallbacks {
  onTelemetryUpdated: (snapshot: TelemetrySnapshot) => void
  onNotificationReceived: (notification: AppNotification) => void
  onTripCompleted: (vehicleId: number) => void
  onCommandResult: (result: CommandResult) => void
}

// Backoff between reconnect attempts, reused for both the automatic reconnect and our own
// restart loop. The last entry repeats for as long as the server stays unreachable.
const RETRY_DELAYS_MS = [0, 2_000, 5_000, 10_000, 30_000]

function retryDelay(attempt: number): number {
  return RETRY_DELAYS_MS[Math.min(attempt, RETRY_DELAYS_MS.length - 1)]!
}

type SignalRClient = typeof import('@microsoft/signalr')

// The client library is as large as the router, and no connection opens before the vehicle list
// has arrived, so it stays out of the entry bundle every page waits for. Its download starts as
// soon as a component sets up the connection, so it is normally in by the time start() runs.
let clientPromise: Promise<SignalRClient> | null = null

function loadClient(): Promise<SignalRClient> {
  clientPromise ??= import('@microsoft/signalr').catch((error: unknown) => {
    // Not cached when it fails, or one offline moment would end live updates until a reload.
    clientPromise = null
    throw error
  })
  return clientPromise
}

/**
 * Owns the SignalR connection to the `/hubs/telemetry` hub: connecting, joining the given
 * vehicle's group, reconnection, and dispatching the four server-pushed events (telemetry,
 * notifications, trip-completed, command results) to caller-supplied callbacks. This is the app's only real-time
 * channel - there is no REST-polling fallback, so it keeps retrying indefinitely rather than
 * giving up: SignalR's default policy stops after a handful of attempts, which left the
 * dashboard stale until a manual reload after something as ordinary as an API restart.
 */
export function useSignalR(callbacks: SignalRCallbacks) {
  const connected = ref(false)
  let connection: HubConnection | null = null
  let restartTimer: ReturnType<typeof setTimeout> | null = null
  let restartAttempt = 0
  // Set by stop() so a pending retry does not resurrect a connection the caller ended.
  let stopped = false
  // Bumped by every start() and stop(), so an attempt still waiting for the client library can
  // tell that the caller has since ended it or asked for another vehicle.
  let session = 0

  // Only warming the download here; a failure surfaces, and is retried, when a connection opens.
  loadClient().catch(() => {})

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

  function createConnection(client: SignalRClient, vehicleId: number): HubConnection {
    const created = new client.HubConnectionBuilder()
      .withUrl(apiUrl('/hubs/telemetry'), { withCredentials: true })
      // Never returns null, so the client keeps trying instead of giving up.
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (ctx) => retryDelay(ctx.previousRetryCount),
      })
      .configureLogging(client.LogLevel.Warning)
      .build()

    created.on('telemetryUpdated', (snapshot: TelemetrySnapshot) => {
      callbacks.onTelemetryUpdated(snapshot)
    })

    created.on('notificationReceived', (notification: AppNotification) => {
      callbacks.onNotificationReceived(notification)
    })

    created.on('tripCompleted', (vid: number) => {
      callbacks.onTripCompleted(vid)
    })

    created.on('commandResult', (result: CommandResult) => {
      callbacks.onCommandResult(result)
    })

    created.onreconnecting(() => {
      connected.value = false
    })
    created.onreconnected(async () => {
      connected.value = true
      await created.invoke('JoinVehicle', vehicleId)
    })
    created.onclose(() => {
      connected.value = false
      // Reached when the automatic reconnect itself fails to re-establish the connection.
      scheduleRestart(vehicleId)
    })

    return created
  }

  async function openConnection(vehicleId: number) {
    if (stopped) return
    const attempt = session
    try {
      if (!connection) {
        const client = await loadClient()
        if (attempt !== session) return
        connection = createConnection(client, vehicleId)
      }
      await connection.start()
      await connection.invoke('JoinVehicle', vehicleId)
      connected.value = true
      restartAttempt = 0
    } catch {
      if (attempt !== session) return
      // The server is unreachable (still starting, restarting, or the network is down), or the
      // client library could not be downloaded. onclose does not fire for a failed start, so the
      // retry is scheduled here.
      connected.value = false
      scheduleRestart(vehicleId)
    }
  }

  async function start(vehicleId: number) {
    if (connection) await stop()
    session += 1
    stopped = false
    restartAttempt = 0
    await openConnection(vehicleId)
  }

  async function stop() {
    stopped = true
    session += 1
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
