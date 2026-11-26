import { computed } from 'vue'
import { useVehicleStore } from '@/stores/vehicle'
import { useVehicleCommand } from '@/composables/useVehicleCommand'
import { useConnection } from '@/composables/useConnection'

/**
 * Asking the car for fresh data: the footer's Refresh button, and on a phone the menu's
 * "Fetched" line. Offered only while the server can be reached, as no command gets to the car
 * without it, and held back while a refresh is already running or no car is chosen.
 */
export function useRefresh() {
  const vehicleStore = useVehicleStore()
  const { sending, send } = useVehicleCommand()
  const connection = useConnection()

  const vin = computed(() => vehicleStore.activeVin)
  const canRefresh = computed(() => connection.reachable)
  const refreshing = computed(() => vehicleStore.loading || sending.value === 'refresh')
  const disabled = computed(() => refreshing.value || !vin.value)

  function refresh() {
    if (!vin.value) return
    send(vin.value, 'refresh', 'force')
  }

  return { canRefresh, refreshing, disabled, refresh }
}
