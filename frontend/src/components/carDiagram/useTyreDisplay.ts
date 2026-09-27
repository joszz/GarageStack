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

  function color(bar: number | null): string {
    const v = variant(bar)
    if (v === 'danger') return 'var(--tyre-danger, #dc3545)'
    if (v === 'warning') return 'var(--tyre-warning, #fd7e14)'
    if (v === 'ok') return 'var(--tyre-ok, #198754)'
    return 'var(--tyre-unknown, #6c757d)'
  }

  function reading(bar: number | null): string {
    return units.value.format('pressure', bar) ?? `- ${units.value.symbol('pressure')}`
  }

  return { variant, color, reading }
}
