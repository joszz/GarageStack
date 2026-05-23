import { ref } from 'vue'
import { vehicleApi } from '@/services/api'

const PENDING_TIMEOUT_MS = 30_000

export function useVehicleCommand() {
  const sending = ref<string | null>(null)
  const lastResult = ref<{ key: string; ok: boolean } | null>(null)
  const pendingSet = ref<Record<string, boolean>>({})
  const timers = new Map<string, ReturnType<typeof setTimeout>>()

  function isPending(key: string): boolean {
    return !!pendingSet.value[key]
  }

  function clearPending(key: string) {
    const t = timers.get(key)
    if (t) { clearTimeout(t); timers.delete(key) }
    if (key in pendingSet.value) {
      const { [key]: _, ...rest } = pendingSet.value
      pendingSet.value = rest
    }
  }

  async function send(vin: string | null | undefined, command: string, value: string) {
    if (!vin || isPending(command)) return
    sending.value = command
    lastResult.value = null
    try {
      await vehicleApi.sendCommand(vin, command, value)
      lastResult.value = { key: command, ok: true }
      pendingSet.value = { ...pendingSet.value, [command]: true }
      timers.set(command, setTimeout(() => clearPending(command), PENDING_TIMEOUT_MS))
    } catch {
      lastResult.value = { key: command, ok: false }
    } finally {
      sending.value = null
    }
  }

  return { sending, lastResult, isPending, clearPending, send }
}
