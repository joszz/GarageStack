<script setup lang="ts">
import { computed, toRef } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ChargeSession, ChargeSessionPowerPoint } from '@/services/vehicleApi'
import { sincePreviousCharge, type ChargeEnergyContext } from '@/utils/chargeSessions'
import type { DetailFigure } from '@/utils/detailFigures'
import { formatNumber } from '@/utils/format'
import { ODOMETER_FORMAT } from '@/utils/units'
import { useUnits } from '@/composables/useUnits'
import { useChargeSession } from '@/composables/useChargeSession'
import { useChargePowerChart } from '@/composables/useChargePowerChart'
import DetailFigureList from './DetailFigureList.vue'
import StatsChartCard from './StatsChartCard.vue'
import ChargeSessionLocationMap from './ChargeSessionLocationMap.vue'

const props = defineProps<{
  session: ChargeSession
  /** The next older session, for what was driven and used since it; null for the oldest. */
  previous: ChargeSession | null
  energyContext: ChargeEnergyContext
  /** The session's power readings, for the chart; null while loading or unavailable. */
  powerPoints: readonly ChargeSessionPowerPoint[] | null
  powerLoading: boolean
  /** Why the power readings could not be loaded, already translated. */
  powerError: string | null
}>()

/** Asks for the power readings again, after they failed to load. */
const emit = defineEmits<{ (e: 'retry'): void }>()

const { t } = useI18n()
const units = useUnits()

const {
  kind,
  ongoing,
  icon,
  longDate,
  startTime,
  endTime,
  duration,
  stateOfCharge,
  maxPower,
  gained,
} = useChargeSession(toRef(props, 'session'), toRef(props, 'energyContext'))

const timeLabel = computed(() =>
  endTime.value ? `${startTime.value} - ${endTime.value}` : startTime.value,
)

// What this charge did. Only what the car reported, as the row shows.
const figures = computed((): DetailFigure[] => {
  const odometer = units.value.measure('distance', props.session.odometerKm, ODOMETER_FORMAT)
  const all: DetailFigure[] = [
    {
      key: 'energy',
      icon: 'bolt',
      label: t('chargeHistory.energyAdded'),
      measure: gained.value,
      title: t('chargeHistory.batteryEnergyGained'),
    },
    {
      key: 'duration',
      icon: 'clock',
      label: t('chargeHistory.duration'),
      measure: { value: duration.value ?? t('chargeHistory.ongoing'), unit: '' },
    },
    {
      key: 'soc',
      icon: 'battery-half',
      label: t('chargeHistory.stateOfCharge'),
      measure: stateOfCharge.value ? { value: stateOfCharge.value, unit: '' } : null,
    },
    {
      key: 'power',
      icon: 'gauge-high',
      label: t('chargeHistory.peakPower'),
      measure: maxPower.value,
      title: t('chargeHistory.maxPower'),
    },
    {
      key: 'odometer',
      icon: 'road',
      label: t('chargeHistory.odometer'),
      measure: odometer,
    },
  ]
  return all.filter((figure) => figure.measure !== null)
})

// The driving between the charge before and this one. Left out for the oldest session listed,
// which has none before it; a figure the car did not report reads "-".
const sinceFigures = computed((): DetailFigure[] => {
  if (!props.previous) return []
  const since = sincePreviousCharge(props.session, props.previous, props.energyContext)
  const notReported = t('chargeHistory.notReported')
  const kwh =
    since.energyKwh === null ? null : { value: formatNumber(since.energyKwh, 1), unit: 'kWh' }
  const all: DetailFigure[] = [
    {
      key: 'distance',
      icon: 'route',
      label: t('chargeHistory.distance'),
      measure: units.value.measure('distance', since.distanceKm, { grouped: true }),
      title: since.distanceKm === null ? notReported : undefined,
    },
    {
      key: 'energyUsed',
      icon: 'battery-quarter',
      label: t('chargeHistory.energyUsed'),
      measure: kwh,
      title: kwh === null ? notReported : undefined,
    },
  ]
  // Only a BEV's: a plug-in hybrid's kilometres include petrol ones.
  if (props.energyContext.vehicleType === 'bev') {
    const consumption = units.value.measure('energyPerDistance', since.whPerKm)
    all.push({
      key: 'consumption',
      icon: 'leaf',
      label: t('chargeHistory.consumption'),
      measure: consumption,
      title: consumption === null ? notReported : undefined,
    })
  }
  return all
})

const hasLocation = computed(() => {
  const { latitude, longitude } = props.session
  return (
    latitude !== null &&
    longitude !== null &&
    Number.isFinite(latitude) &&
    Number.isFinite(longitude) &&
    Math.abs(latitude) <= 90 &&
    Math.abs(longitude) <= 180
  )
})

const powerChart = useChargePowerChart(toRef(props, 'powerPoints'))
</script>

<template>
  <article class="list-detail-item charge-history-detail">
    <header class="list-detail-item__header">
      <h2 class="list-detail-item__title">{{ longDate }}</h2>
      <div class="list-detail-item__status">
        <span class="list-detail-item__time text-sm text-muted">{{ timeLabel }}</span>
        <span v-if="ongoing" class="list-detail-item__badge badge badge-success">{{
          t('chargeHistory.ongoing')
        }}</span>
        <span
          v-if="kind"
          class="list-detail-item__badge charge-history-detail__kind badge"
          :class="kind === 'DC' ? 'badge-info' : 'badge-secondary'"
        >
          <font-awesome-icon :icon="icon" />{{ kind }}
        </span>
      </div>
    </header>

    <ChargeSessionLocationMap
      v-if="hasLocation"
      :key="session.startedAt"
      :latitude="session.latitude ?? 0"
      :longitude="session.longitude ?? 0"
      :label="t('chargeHistory.locationMap')"
    />
    <div v-else class="charge-history-map charge-history-map--empty text-sm">
      {{ t('chargeHistory.locationUnavailable') }}
    </div>

    <DetailFigureList :figures="figures" />

    <section
      v-if="sinceFigures.length > 0"
      class="charge-history-detail__since"
      :aria-label="t('chargeHistory.sinceLastCharge')"
    >
      <h3 class="list-detail-item__subtitle">{{ t('chargeHistory.sinceLastCharge') }}</h3>
      <DetailFigureList :figures="sinceFigures" />
    </section>

    <StatsChartCard
      v-if="powerChart"
      class="list-detail-item__chart"
      :title="t('chargeHistory.power')"
      icon="chart-line"
      type="line"
      :data="powerChart.data"
      :options="powerChart.options"
      :show-info="false"
    />
    <p v-else-if="powerLoading" class="text-muted text-sm" aria-live="polite">
      {{ t('common.loading') }}
    </p>
    <div v-else-if="powerError" class="text-danger text-sm" role="alert">
      <p>{{ powerError }}</p>
      <button type="button" class="btn btn-outline-secondary btn-sm" @click="emit('retry')">
        {{ t('common.retry') }}
      </button>
    </div>
    <p v-else class="text-muted text-sm">{{ t('chargeHistory.noPowerData') }}</p>
  </article>
</template>
