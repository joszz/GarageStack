import { onScopeDispose, readonly, ref, watch, type Ref } from 'vue'

/**
 * How long the car has to stay offline before the app says so. The gateway calls the car offline
 * after three failed polls in a row, which while driving is about 90 seconds of a weak mobile
 * signal, and the next good poll brings it back: most of those flips are over within two minutes.
 */
export const OFFLINE_GRACE_MS = 2 * 60_000

/**
 * The car's online state as the app shows it: online at once, offline only once it has lasted
 * {@link OFFLINE_GRACE_MS}. A page that opens on a car already offline, or a switch to another
 * car, shows that car's state straight away; only a car going offline while watched waits.
 */
export function useSettledAvailability(
  available: () => boolean | null | undefined,
  vehicle: () => number | string | null | undefined,
  graceMs = OFFLINE_GRACE_MS,
): Readonly<Ref<boolean | null>> {
  const settled = ref<boolean | null>(available() ?? null)
  let timer: ReturnType<typeof setTimeout> | null = null

  function clearTimer() {
    if (timer !== null) {
      clearTimeout(timer)
      timer = null
    }
  }

  watch([available, vehicle], ([now, car], [, previousCar]) => {
    clearTimer()
    const value = now ?? null
    if (value === false && settled.value === true && car === previousCar) {
      timer = setTimeout(() => {
        timer = null
        settled.value = false
      }, graceMs)
      return
    }
    settled.value = value
  })

  onScopeDispose(clearTimer)

  return readonly(settled)
}
