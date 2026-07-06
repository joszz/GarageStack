import { ref, watch, getCurrentInstance, onUnmounted } from 'vue'
import { vehicleApi, type TelemetrySnapshot } from '@/services/vehicleApi'
import { useVehicleStore } from '@/stores/vehicle'

const PENDING_TIMEOUT_MS = 30_000

export function useVehicleCommand() {
  const store = useVehicleStore()
  const sending = ref<string | null>(null)
  const lastResult = ref<{ key: string; ok: boolean } | null>(null)
  const pendingSet = ref<Record<string, boolean>>({})
  const timers = new Map<string, ReturnType<typeof setTimeout>>()
  const watchers = new Map<string, () => void>()

  function isPending(key: string): boolean {
    return !!pendingSet.value[key]
  }

  // The real vehicle API processes one command at a time and can take up to ~30s per
  // command, so callers that need to send several commands in one batch must wait for
  // each to settle (confirmed or timed out) before sending the next - otherwise they
  // queue up behind each other on the gateway and miss their own confirmation window.
  function waitUntilSettled(key: string): Promise<void> {
    if (!isPending(key)) return Promise.resolve()
    return new Promise((resolve) => {
      const stop = watch(
        () => pendingSet.value[key],
        (pending) => {
          if (!pending) {
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
    if (key in pendingSet.value) {
      const { [key]: _, ...rest } = pendingSet.value
      pendingSet.value = rest
    }
  }

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
    isConfirmed?: (snapshot: TelemetrySnapshot) => boolean,
    onConfirmed?: () => void,
  ): Promise<boolean> {
    if (!vin || isPending(command)) return false
    sending.value = command
    lastResult.value = null
    store.sendingCount++
    try {
      await vehicleApi.sendCommand(vin, command, value)
      lastResult.value = { key: command, ok: true }
      pendingSet.value = { ...pendingSet.value, [command]: true }
      timers.set(
        command,
        setTimeout(() => clearPending(command), PENDING_TIMEOUT_MS),
      )
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
      lastResult.value = { key: command, ok: false }
      return false
    } finally {
      sending.value = null
      store.sendingCount--
    }
  }

  return { sending, lastResult, isPending, clearPending, send, waitUntilSettled }
}
