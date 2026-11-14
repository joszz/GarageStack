<script setup lang="ts">
import { computed, ref, useId } from 'vue'
import { useI18n } from 'vue-i18n'
import { vehicleApi, type ChargeSession, type ChargeSessionPowerPoint } from '@/services/vehicleApi'
import { DAY_MONTH, formatDate, formatNumber, formatTime } from '@/utils/format'
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
const ongoing = computed(() => props.session.endedAt === null)

// The year only when it is not this one: the longest period reaches back a year.
const dateLabel = computed(() => {
  const started = new Date(props.session.startedAt)
  const year = started.getFullYear() === new Date().getFullYear() ? {} : { year: 'numeric' }
  return formatDate(started, {
    weekday: 'short',
    ...DAY_MONTH,
    ...year,
  } as Intl.DateTimeFormatOptions)
})

const startTime = computed(() => formatTime(props.session.startedAt))
const endTime = computed(() => (props.session.endedAt ? formatTime(props.session.endedAt) : null))

const duration = computed(() => {
  const { startedAt, endedAt } = props.session
  if (!endedAt) return null
  return formatDurationMs(new Date(endedAt).getTime() - new Date(startedAt).getTime(), t)
})

function percent(value: number): string {
  return `${formatNumber(value, 0)}%`
}

function kwh(value: number | null): string {
  return value === null ? t('chargeHistory.notReported') : `${formatNumber(value, 1)} kWh`
}

function distance(km: number | null): string {
  const measured = km === null ? null : units.value.measure('distance', km, { grouped: true })
  return measured ? `${measured.value} ${measured.unit}` : t('chargeHistory.notReported')
}

// The row's second line names only what the car reported: a compact row has no room for a
// "Not reported" in every column.
const stateOfCharge = computed(() => {
  const { startSocPercent: from, endSocPercent: to } = props.session
  if (from !== null && to !== null) return `${percent(from)} → ${percent(to)}`
  if (to !== null) return percent(to)
  return null
})

const odometer = computed(() => {
  const km = props.session.odometerKm
  const measured =
    km === null ? null : units.value.measure('distance', km, { decimals: 0, grouped: true })
  return measured ? `${measured.value} ${measured.unit}` : null
})

const maxPower = computed(() =>
  props.session.maxPowerKw === null ? null : `${formatNumber(props.session.maxPowerKw, 1)} kW`,
)

// The row's headline figure, as a trip's distance: the energy that went in, or where the car
// reports no kWh worth trusting (a plug-in hybrid, issue #571), the percentage points it gained.
const gained = computed(() => {
  const energy = energyGainedKwh(props.session, props.energyContext)
  if (energy !== null) return kwh(energy)
  const { startSocPercent: from, endSocPercent: to } = props.session
  return from !== null && to !== null && to >= from ? `+${percent(to - from)}` : null
})

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
  <li class="charge-history-item" :class="{ 'charge-history-item--expanded': expanded }">
    <!-- The whole row opens the session, laid out as a trip log row: the kind of charge down the
         left, when on the first line, the readings on the second, and the energy on the right. -->
    <button
      type="button"
      class="charge-history-item__toggle"
      :aria-expanded="expanded"
      :aria-controls="panelId"
      @click="toggle"
    >
      <span
        class="charge-history-item__icon badge"
        :class="ongoing ? 'badge-success' : isDc ? 'badge-info' : 'badge-secondary'"
        aria-hidden="true"
      >
        <font-awesome-icon :icon="isDc ? 'bolt' : 'plug'" />
      </span>

      <span class="charge-history-item__when">
        <span class="charge-history-item__date">{{ dateLabel }}</span>
        <span class="charge-history-item__time text-xs text-muted">
          {{ startTime
          }}<span v-if="endTime" class="charge-history-item__end-time"> - {{ endTime }}</span>
        </span>
        <span v-if="duration" class="charge-history-item__pill text-xs">{{ duration }}</span>
        <span
          v-else
          class="charge-history-item__pill charge-history-item__status--active text-xs"
          >{{ t('chargeHistory.ongoing') }}</span
        >
        <span
          v-if="session.chargingType"
          class="charge-history-item__pill charge-history-item__type text-xs"
          :class="{ 'charge-history-item__type--dc': isDc }"
          >{{ session.chargingType }}</span
        >
      </span>

      <span
        v-if="gained"
        class="charge-history-item__energy"
        :title="t('chargeHistory.batteryEnergyGained')"
        >{{ gained }}</span
      >

      <span class="charge-history-item__readings text-sm">
        <span
          v-if="stateOfCharge"
          class="charge-history-item__reading charge-history-item__soc"
          :title="t('chargeHistory.stateOfCharge')"
        >
          <font-awesome-icon
            icon="battery-half"
            role="img"
            :aria-label="t('chargeHistory.stateOfCharge')"
          />{{ stateOfCharge }}
        </span>
        <span
          v-if="maxPower"
          class="charge-history-item__reading charge-history-item__power"
          :title="t('chargeHistory.maxPower')"
        >
          <font-awesome-icon
            icon="gauge-high"
            role="img"
            :aria-label="t('chargeHistory.maxPower')"
          />{{ maxPower }}
        </span>
        <span
          v-if="odometer"
          class="charge-history-item__reading charge-history-item__odometer"
          :title="t('chargeHistory.odometer')"
        >
          <font-awesome-icon icon="road" role="img" :aria-label="t('chargeHistory.odometer')" />{{
            odometer
          }}
        </span>
      </span>

      <font-awesome-icon
        icon="chevron-down"
        class="charge-history-item__chevron"
        aria-hidden="true"
      />
    </button>

    <div v-if="expanded" :id="panelId" class="charge-history-detail">
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
  </li>
</template>
