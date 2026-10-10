import { reactive, ref, type Ref } from 'vue'
import type { Quantity, UnitFormatter } from '@/utils/units'

/**
 * A form field for a quantity the API keeps in metric (kilometres, degrees Celsius), typed in
 * whole units of the browser's choice. A value loaded into the field and saved untouched goes back
 * as the exact metric value it came from: rounding 15,000 km to 9,321 mi and back would otherwise
 * store 15,000.7 km for a form that was only opened to rename something.
 */
export function useMeasureField(units: Ref<UnitFormatter>, quantity: Quantity) {
  /** Bound to the input with v-model.number, which leaves an emptied field as ''. */
  const shown = ref<number | '' | null>(null)
  let loadedMetric: number | null = null
  let loadedShown: number | null = null

  function load(metric: number | null) {
    loadedMetric = metric
    loadedShown = metric === null ? null : Math.round(units.value.convert(quantity, metric))
    shown.value = loadedShown
  }

  /** What to send: null for an empty field, the loaded metric value for an untouched one. */
  function metric(): number | null {
    const value = shown.value
    if (typeof value !== 'number' || !Number.isFinite(value)) return null
    if (value === loadedShown) return loadedMetric
    return units.value.toMetric(quantity, value)
  }

  // Reactive, so a template reaches the field as `field.shown` rather than `field.shown.value`.
  return reactive({ shown, load, metric })
}
