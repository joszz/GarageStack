<script setup lang="ts">
import { computed, ref, useId } from 'vue'
import { useI18n } from 'vue-i18n'
import { vehicleApi, type ChargeSession, type ChargeSessionPowerPoint } from '@/services/vehicleApi'
import { formatDateTime, formatNumber } from '@/utils/format'
import { formatDurationMs } from '@/utils/tripRows'
import {
  energyGainedKwh,
  sincePreviousCharge,
  type ChargeEnergyContext,
} from '@/utils/chargeSessions'
import { useUnits } from '@/composables/useUnits'
import { useErrorMessage } from '@/composables/useErrorMessage'
import { useChargePowerChart } from '@/composables/useChargePowerChart'
import StatsChartCard from './StatsChartCard.vue'
import ChargeSessionLocationMap from './ChargeSessionLocationMap.vue'

const props = defineProps<{
  vin: string
  session: ChargeSession
  /** The next older session, for what was driven and used since it. */
  previous: ChargeSession | null
  energyContext: ChargeEnergyContext
}>()

const { t } = useI18n()
const units = useUnits()
const errorMessage = useErrorMessage()
const panelId = useId()

const expanded = ref(false)
const powerPoints = ref<ChargeSessionPowerPoint[] | null>(null)
const powerLoading = ref(false)
const powerError = ref<unknown>(null)
const powerChart = useChargePowerChart(powerPoints)

const isDc = computed(() => props.session.chargingType?.toUpperCase() === 'DC')

const duration = computed(() => {
  const { startedAt, endedAt } = props.session
  if (!endedAt) return t('chargeHistory.ongoing')
  return formatDurationMs(new Date(endedAt).getTime() - new Date(startedAt).getTime(), t)
})

function percent(value: number | null): string {
  return value === null ? t('chargeHistory.notReported') : `${formatNumber(value, 0)}%`
}

function kwh(value: number | null): string {
  return value === null ? t('chargeHistory.notReported') : `${formatNumber(value, 1)} kWh`
}

function distance(km: number | null): string {
  const measured = km === null ? null : units.value.measure('distance', km, { grouped: true })
  return measured ? `${measured.value} ${measured.unit}` : t('chargeHistory.notReported')
}

const stateOfCharge = computed(
  () => `${percent(props.session.startSocPercent)} → ${percent(props.session.endSocPercent)}`,
)

const odometer = computed(() => {
  const km = props.session.odometerKm
  const measured =
    km === null ? null : units.value.measure('distance', km, { decimals: 0, grouped: true })
  return measured ? `${measured.value} ${measured.unit}` : t('chargeHistory.notReported')
})

const gained = computed(() => kwh(energyGainedKwh(props.session, props.energyContext)))

const maxPower = computed(() =>
  props.session.maxPowerKw === null
    ? t('chargeHistory.notReported')
    : `${formatNumber(props.session.maxPowerKw, 1)} kW`,
)

const since = computed(() =>
  sincePreviousCharge(props.session, props.previous, props.energyContext),
)

const consumption = computed(() => {
  const value = since.value.whPerKm
  const measured = value === null ? null : units.value.measure('energyPerDistance', value)
  return measured ? `${measured.value} ${measured.unit}` : t('chargeHistory.notReported')
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

async function loadPower(): Promise<void> {
  powerLoading.value = true
  powerError.value = null
  try {
    powerPoints.value = await vehicleApi.chargePower(
      props.vin,
      props.session.startedAt,
      props.session.endedAt ?? undefined,
    )
  } catch (error) {
    powerError.value = error
  } finally {
    powerLoading.value = false
  }
}

function toggle(): void {
  expanded.value = !expanded.value
  if (expanded.value && powerPoints.value === null && !powerLoading.value) void loadPower()
}
</script>

<template>
  <li class="charge-history-item">
    <div class="charge-history-item__heading">
      <h2>{{ formatDateTime(session.startedAt) }}</h2>
      <div class="charge-history-item__badges">
        <span
          class="charge-history-item__status"
          :class="{ 'charge-history-item__status--active': !session.endedAt }"
        >
          {{ session.endedAt ? t('chargeHistory.completed') : t('chargeHistory.ongoing') }}
        </span>
        <span
          v-if="session.chargingType"
          class="charge-history-item__type"
          :class="{ 'charge-history-item__type--dc': isDc }"
        >
          <font-awesome-icon v-if="isDc" icon="bolt" aria-hidden="true" />
          {{ session.chargingType }}
        </span>
      </div>
    </div>

    <dl class="charge-history-item__details">
      <div>
        <dt>{{ t('chargeHistory.duration') }}</dt>
        <dd>{{ duration }}</dd>
      </div>
      <div>
        <dt>{{ t('chargeHistory.stateOfCharge') }}</dt>
        <dd>{{ stateOfCharge }}</dd>
      </div>
      <div>
        <dt>{{ t('chargeHistory.odometer') }}</dt>
        <dd>{{ odometer }}</dd>
      </div>
      <div>
        <dt>{{ t('chargeHistory.batteryEnergyGained') }}</dt>
        <dd>{{ gained }}</dd>
      </div>
      <div>
        <dt>{{ t('chargeHistory.maxPower') }}</dt>
        <dd>{{ maxPower }}</dd>
      </div>
      <div v-if="session.endedAt">
        <dt>{{ t('chargeHistory.ended') }}</dt>
        <dd>{{ formatDateTime(session.endedAt) }}</dd>
      </div>
    </dl>

    <button
      type="button"
      class="btn btn-outline-secondary btn-sm charge-history-item__toggle"
      :aria-expanded="expanded"
      :aria-controls="panelId"
      @click="toggle"
    >
      <font-awesome-icon icon="chart-line" aria-hidden="true" />
      {{ expanded ? t('chargeHistory.hideDetails') : t('chargeHistory.showDetails') }}
    </button>

    <div v-if="expanded" :id="panelId" class="charge-history-detail">
      <div class="charge-history-detail__chart">
        <StatsChartCard
          v-if="powerChart"
          class="charge-history-detail__chart-card"
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
          <p>{{ errorMessage(powerError) }}</p>
          <button type="button" class="btn btn-outline-secondary btn-sm" @click="loadPower">
            {{ t('common.retry') }}
          </button>
        </div>
        <p v-else class="text-muted text-sm">{{ t('chargeHistory.noPowerData') }}</p>
      </div>

      <div class="charge-history-detail__aside">
        <dl class="charge-history-detail__metrics">
          <div>
            <dt>{{ t('chargeHistory.distanceSincePreviousCharge') }}</dt>
            <dd>{{ distance(since.distanceKm) }}</dd>
          </div>
          <div>
            <dt>{{ t('chargeHistory.energyUsedSincePreviousCharge') }}</dt>
            <dd>{{ kwh(since.energyKwh) }}</dd>
          </div>
          <div v-if="energyContext.vehicleType === 'bev'">
            <dt>{{ t('chargeHistory.consumptionSincePreviousCharge') }}</dt>
            <dd>{{ consumption }}</dd>
          </div>
        </dl>
        <ChargeSessionLocationMap
          v-if="hasLocation"
          :latitude="session.latitude ?? 0"
          :longitude="session.longitude ?? 0"
          :label="t('chargeHistory.locationMap')"
        />
        <div v-else class="charge-history-map charge-history-map--empty">
          {{ t('chargeHistory.locationUnavailable') }}
        </div>
      </div>
    </div>
  </li>
</template>
