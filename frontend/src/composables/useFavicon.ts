import { watch } from 'vue'
import { useVehicleStore } from '@/stores/vehicle'

const FAVICON_IDLE = '/logo.svg'
const FAVICON_LOADING = '/favicon-loading.svg'
const MIN_VISIBLE_MS = 600

export function useFavicon() {
  const vehicleStore = useVehicleStore()

  const link = document.querySelector<HTMLLinkElement>('link[rel="icon"][type="image/svg+xml"]')
  if (!link) return

  let resetTimer: ReturnType<typeof setTimeout> | null = null

  watch(
    () => vehicleStore.loading || vehicleStore.anySending,
    (isActive) => {
      if (isActive) {
        if (resetTimer) {
          clearTimeout(resetTimer)
          resetTimer = null
        }
        link.href = FAVICON_LOADING
      } else {
        resetTimer = setTimeout(() => {
          link.href = FAVICON_IDLE
          resetTimer = null
        }, MIN_VISIBLE_MS)
      }
    },
    { immediate: true },
  )
}
