import { ref, watch, getCurrentInstance, onUnmounted } from 'vue'
import { vehicleApi, type CommandResult, type TelemetrySnapshot } from '@/services/vehicleApi'
import { useVehicleStore } from '@/stores/vehicle'

// The gateway answers once the SAIC API has confirmed the command, which it polls for up to 30s,
// so an answer can land a few seconds past that. Matches the API's VehicleCommandGate hold.
const PENDING_TIMEOUT_MS = 45_000

export interface SendOptions {
  /** Whether live telemetry shows the command took effect. */
  isConfirmed?: (snapshot: TelemetrySnapshot) => boolean
  /**
   * Called once the command is confirmed: by telemetry when `isConfirmed` is given, otherwise by
   * the gateway reporting that the car carried it out.
   */
  onConfirmed?: () => void
  /** Called when the gateway reports that the car refused the command. */
  onRejected?: () => void
}

export interface CommandOutcome {
  key: string
  ok: boolean
  /** The gateway's reason when the car refused the command, otherwise null. */
  detail: string | null
}

// 'sent': the gateway has not answered, so it may still be busy with the command. 'accepted': the
// car carried it out and the gateway is free again; only telemetry showing it is still awaited.
type PendingState = 'sent' | 'accepted'

export function useVehicleCommand() {
  const store = useVehicleStore()
  const sending = ref<string | null>(null)
  const lastResult = ref<CommandOutcome | null>(null)
  const pendingSet = ref<Record<string, PendingState>>({})
  const timers = new Map<string, ReturnType<typeof setTimeout>>()
  const watchers = new Map<string, () => void>()
  const sendOptions = new Map<string, SendOptions>()

  function isPending(key: string): boolean {
    return key in pendingSet.value
  }

  function setPending(key: string, state: PendingState) {
    pendingSet.value = { ...pendingSet.value, [key]: state }
  }

  // The real vehicle API processes one command at a time and can take up to ~30s per
  // command, so callers that need to send several commands in one batch must wait for
  // each to settle (answered, confirmed or timed out) before sending the next - otherwise
  // they queue up behind each other on the gateway and miss their own confirmation window.
  function waitUntilSettled(key: string): Promise<void> {
    if (pendingSet.value[key] !== 'sent') return Promise.resolve()
    return new Promise((resolve) => {
      const stop = watch(
        () => pendingSet.value[key],
        (state) => {
          if (state !== 'sent') {
            stop()
            resolve()
          }
        },
      )
    })
  }

  function clearPending(key: string) {
    const t = timers.get(key)
    if (t) {
      clearTimeout(t)
      timers.delete(key)
    }
    const stopWatcher = watchers.get(key)
    if (stopWatcher) {
      stopWatcher()
      watchers.delete(key)
    }
    sendOptions.delete(key)
    if (key in pendingSet.value) {
      const { [key]: _, ...rest } = pendingSet.value
      pendingSet.value = rest
    }
  }

  // An answer names a command, not a request, so only one this instance sent and is still waiting
  // on is taken as its own: the same command from another tab, or one already given up on, passes by.
  function applyGatewayAnswer(result: CommandResult) {
    const key = result.command
    if (pendingSet.value[key] !== 'sent') return
    const { isConfirmed, onConfirmed, onRejected } = sendOptions.get(key) ?? {}

    if (!result.success) {
      clearPending(key)
      lastResult.value = { key, ok: false, detail: result.detail }
      onRejected?.()
    } else if (isConfirmed) {
      setPending(key, 'accepted')
    } else {
      clearPending(key)
      onConfirmed?.()
    }
  }

  // Synchronous, so two answers arriving in one tick (SignalR can deliver several messages in one
  // frame) are both seen rather than only the last.
  watch(
    () => store.lastCommandResult,
    (result) => {
      if (result) applyGatewayAnswer(result)
    },
    { flush: 'sync' },
  )

  if (getCurrentInstance()) {
    onUnmounted(() => {
      timers.forEach((t) => clearTimeout(t))
      timers.clear()
      watchers.forEach((stop) => stop())
      watchers.clear()
    })
  }

  async function send(
    vin: string | null | undefined,
    command: string,
    value: string,
    options: SendOptions = {},
  ): Promise<boolean> {
    if (!vin || isPending(command)) return false
    sending.value = command
    lastResult.value = null
    store.sendingCount++
    try {
      await vehicleApi.sendCommand(vin, command, value)
      lastResult.value = { key: command, ok: true, detail: null }
      setPending(command, 'sent')
      sendOptions.set(command, options)
      timers.set(
        command,
        setTimeout(() => clearPending(command), PENDING_TIMEOUT_MS),
      )
      const { isConfirmed, onConfirmed } = options
      if (isConfirmed) {
        const stopWatch = watch(
          () => store.currentStatus,
          (snapshot) => {
            if (snapshot && isConfirmed(snapshot)) {
              clearPending(command)
              onConfirmed?.()
            }
          },
        )
        watchers.set(command, stopWatch)
      }
      return true
    } catch {
      lastResult.value = { key: command, ok: false, detail: null }
      return false
    } finally {
      sending.value = null
      store.sendingCount--
    }
  }

  return { sending, lastResult, isPending, clearPending, send, waitUntilSettled }
}
