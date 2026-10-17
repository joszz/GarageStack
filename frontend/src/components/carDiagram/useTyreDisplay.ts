import {
  pressureVariant,
  useTyrePressureThresholds,
  type PressureVariant,
} from '@/composables/useTyrePressureThresholds'
import { useUnits } from '@/composables/useUnits'

/** The four tyres by position, in bar as the car reports them. */
export interface TyrePressures {
  frontLeft: number | null
  frontRight: number | null
  rearLeft: number | null
  rearRight: number | null
}

export type TyrePosition = keyof TyrePressures

/** How the diagram shows a tyre's pressure: its band, the colour of that band, and the reading. */
export function useTyreDisplay() {
  const thresholds = useTyrePressureThresholds()
  const units = useUnits()

  function variant(bar: number | null): PressureVariant {
    return pressureVariant(bar, thresholds.value)
  }

  // The status colours the tyre labels' borders and the legend use, so all three agree per theme.
  function color(bar: number | null): string {
    const v = variant(bar)
    if (v === 'danger') return 'var(--color-danger)'
    if (v === 'warning') return 'var(--color-warning)'
    if (v === 'ok') return 'var(--color-success)'
    return 'var(--color-text-muted)'
  }

  function reading(bar: number | null): string {
    return units.value.format('pressure', bar) ?? `- ${units.value.symbol('pressure')}`
  }

  return { variant, color, reading }
}
