import { ref, watch, getCurrentInstance, onUnmounted } from 'vue'
import { vehicleApi, type TelemetrySnapshot } from '@/services/api'
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

  return { sending, lastResult, isPending, clearPending, send }
}
