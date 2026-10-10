import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useUnits } from '@/composables/useUnits'
import {
  CLIMATE_TEMPERATURE_MAX_C,
  CLIMATE_TEMPERATURE_MIN_C,
  climateModeOptions,
} from '@/utils/climate'

/**
 * The labels the climate controls show, shared by the climate popup and the schedule form so
 * both read the same. The car takes whole degrees Celsius, so a temperature slider steps through
 * those whatever the display unit; in Fahrenheit each step is labelled with its rounded equivalent.
 */
export function useClimateLabels() {
  const { t } = useI18n()
  const units = useUnits()

  function wholeDegrees(celsius: number): string {
    return units.value.measure('temperature', celsius, { decimals: 0 })!.value
  }

  function temperatureText(celsius: number): string {
    return `${wholeDegrees(celsius)} ${units.value.symbol('temperature')}`
  }

  const temperatureEdges = computed((): [string, string] => [
    `${wholeDegrees(CLIMATE_TEMPERATURE_MIN_C)}°`,
    `${wholeDegrees(CLIMATE_TEMPERATURE_MAX_C)}°`,
  ])

  const seatLabels = computed(() => [
    t('control.seat.off'),
    t('control.seat.low'),
    t('control.seat.medium'),
    t('control.seat.high'),
  ])

  function seatText(level: number): string {
    return seatLabels.value[level] ?? ''
  }

  const modeOptions = computed(() => climateModeOptions(t))

  return { temperatureText, temperatureEdges, seatLabels, seatText, modeOptions }
}
