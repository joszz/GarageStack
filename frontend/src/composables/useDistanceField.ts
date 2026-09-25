import { reactive, ref, type Ref } from 'vue'
import type { UnitFormatter } from '@/utils/units'

/**
 * A form field for a distance the API keeps in kilometres, typed in whole units of the browser's
 * choice. A value loaded into the field and saved untouched goes back as the exact kilometres it
 * came from: rounding 15,000 km to 9,321 mi and back would otherwise store 15,000.7 km for a form
 * that was only opened to rename something.
 */
export function useDistanceField(units: Ref<UnitFormatter>) {
  /** Bound to the input with v-model.number, which leaves an emptied field as ''. */
  const shown = ref<number | '' | null>(null)
  let loadedKm: number | null = null
  let loadedShown: number | null = null

  function load(km: number | null) {
    loadedKm = km
    loadedShown = km === null ? null : Math.round(units.value.convert('distance', km))
    shown.value = loadedShown
  }

  /** What to send: null for an empty field, the loaded kilometres for an untouched one. */
  function km(): number | null {
    const value = shown.value
    if (typeof value !== 'number' || !Number.isFinite(value)) return null
    if (value === loadedShown) return loadedKm
    return units.value.toMetric('distance', value)
  }

  // Reactive, so a template reaches the field as `field.shown` rather than `field.shown.value`.
  return reactive({ shown, load, km })
}
