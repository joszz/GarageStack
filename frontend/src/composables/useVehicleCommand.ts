import { ref } from 'vue'
import { vehicleApi } from '@/services/api'

export function useVehicleCommand() {
  const sending = ref<string | null>(null)
  const lastResult = ref<{ key: string; ok: boolean } | null>(null)

  async function send(vin: string | null | undefined, command: string, value: string) {
    if (!vin) return
    sending.value = command
    lastResult.value = null
    try {
      await vehicleApi.sendCommand(vin, command, value)
      lastResult.value = { key: command, ok: true }
    } catch {
      lastResult.value = { key: command, ok: false }
    } finally {
      sending.value = null
    }
  }

  return { sending, lastResult, send }
}
