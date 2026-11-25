import { computed, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ChargeSession } from '@/services/vehicleApi'
import { DAY_MONTH, formatDate, formatNumber, formatTime } from '@/utils/format'
import { formatDurationMs } from '@/utils/tripRows'
import { chargingKind, energyGainedKwh, type ChargeEnergyContext } from '@/utils/chargeSessions'
import type { Measure } from '@/utils/units'

function percent(value: number): string {
  return `${formatNumber(value, 0)}%`
}

/** A measure as one piece of text, for a row that shows it as one. */
export function measureText(measure: Measure): string {
  return measure.unit ? `${measure.value} ${measure.unit}` : measure.value
}

/**
 * A charge session as its row and its detail show it: when, what kind of charge, and the figures
 * the car reported, each null when it did not report it.
 */
export function useChargeSession(
  session: Readonly<Ref<ChargeSession>>,
  energyContext: Readonly<Ref<ChargeEnergyContext>>,
) {
  const { t } = useI18n()

  const kind = computed(() => chargingKind(session.value.chargingType))
  const ongoing = computed(() => session.value.endedAt === null)

  /** The icon, its colour and its name: green while charging, blue for DC, grey for AC. */
  const icon = computed(() => (kind.value === 'DC' ? 'bolt' : 'plug'))
  const badgeClass = computed(() =>
    ongoing.value ? 'badge-success' : kind.value === 'DC' ? 'badge-info' : 'badge-secondary',
  )
  const kindLabel = computed(() =>
    kind.value ? t('chargeHistory.kindCharge', { kind: kind.value }) : t('chargeHistory.session'),
  )

  // The year only when it is not this one: the longest period reaches back a year.
  const shortDate = computed(() => {
    const started = new Date(session.value.startedAt)
    const year = started.getFullYear() === new Date().getFullYear() ? {} : { year: 'numeric' }
    return formatDate(started, {
      weekday: 'short',
      ...DAY_MONTH,
      ...year,
    } as Intl.DateTimeFormatOptions)
  })

  const longDate = computed(() =>
    formatDate(session.value.startedAt, {
      weekday: 'long',
      day: 'numeric',
      month: 'long',
      year: 'numeric',
    }),
  )

  const startTime = computed(() => formatTime(session.value.startedAt))
  const endTime = computed(() => (session.value.endedAt ? formatTime(session.value.endedAt) : null))

  const duration = computed(() => {
    const { startedAt, endedAt } = session.value
    if (!endedAt) return null
    return formatDurationMs(new Date(endedAt).getTime() - new Date(startedAt).getTime(), t)
  })

  const stateOfCharge = computed(() => {
    const { startSocPercent: from, endSocPercent: to } = session.value
    if (from !== null && to !== null) return `${percent(from)} → ${percent(to)}`
    if (to !== null) return percent(to)
    return null
  })

  const maxPower = computed((): Measure | null =>
    session.value.maxPowerKw === null
      ? null
      : { value: formatNumber(session.value.maxPowerKw, 1), unit: 'kW' },
  )

  // The headline figure, as a trip's distance: the energy that went in, or where the car reports
  // no kWh worth trusting (a plug-in hybrid, issue #571), the percentage points it gained.
  const gained = computed((): Measure | null => {
    const energy = energyGainedKwh(session.value, energyContext.value)
    if (energy !== null) return { value: formatNumber(energy, 1), unit: 'kWh' }
    const { startSocPercent: from, endSocPercent: to } = session.value
    return from !== null && to !== null && to >= from
      ? { value: `+${percent(to - from)}`, unit: '' }
      : null
  })

  return {
    kind,
    ongoing,
    icon,
    badgeClass,
    kindLabel,
    shortDate,
    longDate,
    startTime,
    endTime,
    duration,
    stateOfCharge,
    maxPower,
    gained,
  }
}
