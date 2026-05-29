<script setup lang="ts">
import { onMounted, computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useVehicleStore } from '@/stores/vehicle'
import { useSettingsStore } from '@/stores/settings'
import type { TelemetrySnapshot } from '@/services/api'
import { Line } from 'vue-chartjs'
import CardInfoWrap from '@/components/CardInfoWrap.vue'
import {
  Chart as ChartJS,
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  Title,
  Tooltip,
  Legend,
  Filler,
} from 'chart.js'

ChartJS.register(CategoryScale, LinearScale, PointElement, LineElement, Title, Tooltip, Legend, Filler)

const { t } = useI18n()
const store = useVehicleStore()
const settingsStore = useSettingsStore()
const days = ref(7)

const vin = computed(() => store.vehicles[0]?.vin ?? null)
const status = computed(() => store.currentStatus)

async function load() {
  await store.fetchVehicles()
  if (!vin.value) return
  const from = new Date(Date.now() - days.value * 86_400_000).toISOString()
  await Promise.all([
    store.fetchHistory(vin.value, from),
    store.fetchTrips(vin.value, from),
    store.fetchStatus(vin.value),
    store.fetchConfig(vin.value),
  ])
}

onMounted(load)

function watchDays() {
  load()
}

// Effective vehicle type: manual override wins, then auto-detected
const effectiveVehicleType = computed(() => {
  if (settingsStore.vehicleTypeOverride !== 'auto') return settingsStore.vehicleTypeOverride
  return store.detectedVehicleType
})

const hasFuel = computed(() => effectiveVehicleType.value !== 'bev')
const hasLargeEv = computed(() =>
  effectiveVehicleType.value === 'phev' ||
  effectiveVehicleType.value === 'bev' ||
  effectiveVehicleType.value === 'unknown',
)
const hasCharging = computed(() =>
  effectiveVehicleType.value === 'phev' ||
  effectiveVehicleType.value === 'bev' ||
  effectiveVehicleType.value === 'unknown',
)

// Efficiency computed from today's stats
const efficiencyWh = computed(() => {
  const s = status.value
  if (!s?.powerUsageOfDay || !s?.mileageOfTheDay || s.mileageOfTheDay === 0) return null
  return Math.round(s.powerUsageOfDay / s.mileageOfTheDay)
})

// Group history by local date; pre-fill every day in the range so charts always
// show the full requested window even when some days have no data.
function toLocalDateKey(date: Date) {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

function avg(values: Array<number | null>) {
  const valid = values.filter((v): v is number => v !== null)
  if (!valid.length) return null
  return valid.reduce((sum, v) => sum + v, 0) / valid.length
}

function round2(value: number) {
  return Math.round(value * 100) / 100
}

const groupedHistory = computed(() => {
  const buckets = new Map<string, TelemetrySnapshot[]>()

  // Pre-fill all days in the requested range (local calendar days)
  const startDay = new Date()
  startDay.setDate(startDay.getDate() - days.value)
  const endDay = new Date()
  for (
    let d = new Date(startDay.getFullYear(), startDay.getMonth(), startDay.getDate());
    d <= endDay;
    d.setDate(d.getDate() + 1)
  ) {
    buckets.set(toLocalDateKey(d), [])
  }

  for (const snapshot of store.history) {
    const key = toLocalDateKey(new Date(snapshot.recordedAt))
    const existing = buckets.get(key)
    if (existing) {
      existing.push(snapshot)
    } else {
      buckets.set(key, [snapshot])
    }
  }

  return Array.from(buckets.entries()).map(([key, snapshots]) => ({
    key,
    label: new Date(`${key}T00:00:00`).toLocaleDateString(),
    snapshots,
  }))
})

function chartLabels() {
  return groupedHistory.value.map((d) => d.label)
}

const periodDistanceKm = computed(() => {
  if (!store.trips.length) return null
  const total = store.trips.reduce((sum, trip) => sum + trip.distanceKm, 0)
  return round2(total)
})

const averageTripKm = computed(() => {
  if (!store.trips.length) return null
  const total = store.trips.reduce((sum, trip) => sum + trip.distanceKm, 0)
  return round2(total / store.trips.length)
})

const climateUsagePct = computed(() => {
  const known = store.history.filter((s) => s.climateOn !== null)
  if (!known.length) return null
  const onCount = known.filter((s) => s.climateOn === true).length
  return Math.round((onCount / known.length) * 100)
})

const peakDriveHour = computed(() => {
  if (!store.trips.length) return null
  const counts = new Map<number, number>()
  for (const trip of store.trips) {
    const hour = new Date(trip.startedAt).getHours()
    counts.set(hour, (counts.get(hour) ?? 0) + 1)
  }
  let bestHour = 0
  let bestCount = 0
  for (const [hour, count] of counts.entries()) {
    if (count > bestCount) {
      bestHour = hour
      bestCount = count
    }
  }
  return `${String(bestHour).padStart(2, '0')}:00`
})

const parkingLocations = computed(() => {
  if (!store.trips.length) return null
  const spots = new Set<string>()
  for (const trip of store.trips) {
    const lastPoint = trip.points[trip.points.length - 1]
    if (!lastPoint) continue
    const lat = lastPoint.latitude.toFixed(3)
    const lon = lastPoint.longitude.toFixed(3)
    spots.add(`${lat},${lon}`)
  }
  return spots.size
})

const batteryVoltageTrend = computed(() => {
  const dailyAvg = groupedHistory.value
    .map((d) => avg(d.snapshots.map((s) => s.batteryVoltage)))
    .filter((v): v is number => v !== null)

  if (dailyAvg.length < 2) return null

  const first = dailyAvg[0]
  const last = dailyAvg[dailyAvg.length - 1]
  if (first === undefined || last === undefined) return null

  const delta = round2(last - first)
  const sign = delta > 0 ? '+' : ''
  return `${sign}${delta} V`
})

// Falls back to current voltage when a trend can't be computed (< 2 days of data)
const batteryVoltageDisplay = computed(() => {
  if (batteryVoltageTrend.value !== null) return batteryVoltageTrend.value
  const v = status.value?.batteryVoltage
  return v != null ? `${v.toFixed(1)} V` : null
})

const fuelChartData = computed(() => ({
  labels: chartLabels(),
  datasets: [
    {
      label: `${t('vehicle.fuel')} (%)`,
      data: groupedHistory.value.map((d) => avg(d.snapshots.map((s) => s.fuelLevelPercent))),
      borderColor: '#3b82f6',
      backgroundColor: 'rgba(59,130,246,0.1)',
      fill: true,
      tension: 0.3,
      spanGaps: true,
      pointRadius: 2,
      pointHoverRadius: 4,
    },
  ],
}))

const evChartData = computed(() => ({
  labels: chartLabels(),
  datasets: [
    {
      label: `${t('vehicle.evSoc')} (%)`,
      data: groupedHistory.value.map((d) => avg(d.snapshots.map((s) => s.evSocPercent))),
      borderColor: '#10b981',
      backgroundColor: 'rgba(16,185,129,0.1)',
      fill: true,
      tension: 0.3,
      spanGaps: true,
      pointRadius: 2,
      pointHoverRadius: 4,
    },
  ],
}))

const tyreChartData = computed(() => ({
  labels: chartLabels(),
  datasets: [
    {
      label: `FL (${t('common.bar')})`,
      data: groupedHistory.value.map((d) => avg(d.snapshots.map((s) => s.tyrePressureFrontLeft))),
      borderColor: '#f59e0b',
      tension: 0.3,
      spanGaps: true,
      pointRadius: 2,
      pointHoverRadius: 4,
    },
    {
      label: `FR (${t('common.bar')})`,
      data: groupedHistory.value.map((d) => avg(d.snapshots.map((s) => s.tyrePressureFrontRight))),
      borderColor: '#ef4444',
      tension: 0.3,
      spanGaps: true,
      pointRadius: 2,
      pointHoverRadius: 4,
    },
    {
      label: `RL (${t('common.bar')})`,
      data: groupedHistory.value.map((d) => avg(d.snapshots.map((s) => s.tyrePressureRearLeft))),
      borderColor: '#8b5cf6',
      tension: 0.3,
      spanGaps: true,
      pointRadius: 2,
      pointHoverRadius: 4,
    },
    {
      label: `RR (${t('common.bar')})`,
      data: groupedHistory.value.map((d) => avg(d.snapshots.map((s) => s.tyrePressureRearRight))),
      borderColor: '#ec4899',
      tension: 0.3,
      spanGaps: true,
      pointRadius: 2,
      pointHoverRadius: 4,
    },
  ],
}))

const percentOptions = {
  responsive: true,
  maintainAspectRatio: true,
  aspectRatio: 2.6,
  animation: false as const,
  plugins: { legend: { display: false } },
  scales: {
    x: {
      ticks: {
        maxRotation: 0,
        autoSkip: true,
        maxTicksLimit: 8,
      },
    },
    y: { min: 0, max: 100 },
  },
}

const pressureOptions = {
  responsive: true,
  maintainAspectRatio: true,
  aspectRatio: 2.3,
  animation: false as const,
  plugins: { legend: { display: true } },
  scales: {
    x: {
      ticks: {
        maxRotation: 0,
        autoSkip: true,
        maxTicksLimit: 8,
      },
    },
    y: { min: 1.5, max: 3.5 },
  },
}
</script>

<template>
  <div class="view-container">
    <div class="view-header">
      <h1>{{ t('nav.statistics') }}</h1>
      <div class="view-header__actions">
        <select v-model="days" class="form-select form-select-sm stats-days-select" @change="watchDays">
          <option :value="1">24h</option>
          <option :value="7">7 days</option>
          <option :value="30">30 days</option>
        </select>
      </div>
    </div>

    <div v-if="store.loading && !status && !store.history.length" class="loading-state">
      <font-awesome-icon icon="spinner" spin />
      {{ t('common.loading') }}
    </div>

    <template v-else>
      <div v-if="store.error && !status && !store.history.length" class="empty-state text-danger">{{ store.error }}</div>
      <div v-else-if="!status && !store.history.length" class="empty-state">{{ t('dashboard.noData') }}</div>

      <template v-else>
        <section class="stats-insights" aria-label="Statistics insights">
          <div class="status-grid">
            <!-- Today's efficiency (shown when status is available) -->
            <CardInfoWrap v-if="status" :title="t('vehicle.efficiency.todayDistance')" :description="t('statistics.cardDesc.todayDistance')">
              <div class="status-card">
                <div class="status-card__body">
                  <span class="status-card__label">{{ t('vehicle.efficiency.todayDistance') }}</span>
                  <span class="status-card__value">
                    {{ status.mileageOfTheDay !== null ? Math.round(status.mileageOfTheDay) : '-' }}<span v-if="status.mileageOfTheDay !== null" class="status-card__unit"> {{ t('common.km') }}</span>
                  </span>
                </div>
              </div>
            </CardInfoWrap>

            <CardInfoWrap v-if="status" :title="t('vehicle.efficiency.todayEnergy')" :description="t('statistics.cardDesc.todayEnergy')">
              <div class="status-card">
                <div class="status-card__body">
                  <span class="status-card__label">{{ t('vehicle.efficiency.todayEnergy') }}</span>
                  <span class="status-card__value">
                    {{ status.powerUsageOfDay !== null ? Math.round(status.powerUsageOfDay) : '-' }}<span v-if="status.powerUsageOfDay !== null" class="status-card__unit"> {{ t('common.wh') }}</span>
                  </span>
                </div>
              </div>
            </CardInfoWrap>

            <CardInfoWrap v-if="status" :title="t('vehicle.efficiency.efficiency')" :description="t('statistics.cardDesc.efficiency')">
              <div class="status-card">
                <div class="status-card__body">
                  <span class="status-card__label">{{ t('vehicle.efficiency.efficiency') }}</span>
                  <span class="status-card__value">
                    {{ efficiencyWh !== null ? efficiencyWh : '-' }}<span v-if="efficiencyWh !== null" class="status-card__unit"> Wh/km</span>
                  </span>
                </div>
              </div>
            </CardInfoWrap>

            <CardInfoWrap v-if="status && hasCharging" :title="t('vehicle.efficiency.sinceCharge')" :description="t('statistics.cardDesc.sinceCharge')">
              <div class="status-card">
                <div class="status-card__body">
                  <span class="status-card__label">{{ t('vehicle.efficiency.sinceCharge') }}</span>
                  <span class="status-card__value">
                    {{ status.mileageSinceLastCharge !== null ? Math.round(status.mileageSinceLastCharge) : '-' }}<span v-if="status.mileageSinceLastCharge !== null" class="status-card__unit"> {{ t('common.km') }}</span>
                  </span>
                </div>
              </div>
            </CardInfoWrap>

            <!-- Period insights (shown when history is available) -->
            <template v-if="store.history.length">
              <CardInfoWrap
                :title="days === 30 ? t('statistics.insights.monthlyMileage') : t('statistics.insights.distanceInRange')"
                :description="t('statistics.cardDesc.distanceInRange')"
              >
                <div class="status-card">
                  <div class="status-card__body">
                    <span class="status-card__label">{{ days === 30 ? t('statistics.insights.monthlyMileage') : t('statistics.insights.distanceInRange') }}</span>
                    <span class="status-card__value">
                      {{ periodDistanceKm ?? '-' }}<span v-if="periodDistanceKm !== null" class="status-card__unit"> {{ t('common.km') }}</span>
                    </span>
                  </div>
                </div>
              </CardInfoWrap>

              <CardInfoWrap :title="t('statistics.insights.avgTripLength')" :description="t('statistics.cardDesc.avgTripLength')">
                <div class="status-card">
                  <div class="status-card__body">
                    <span class="status-card__label">{{ t('statistics.insights.avgTripLength') }}</span>
                    <span class="status-card__value">
                      {{ averageTripKm ?? '-' }}<span v-if="averageTripKm !== null" class="status-card__unit"> {{ t('common.km') }}</span>
                    </span>
                  </div>
                </div>
              </CardInfoWrap>

              <CardInfoWrap :title="t('statistics.insights.climateUsage')" :description="t('statistics.cardDesc.climateUsage')">
                <div class="status-card">
                  <div class="status-card__body">
                    <span class="status-card__label">{{ t('statistics.insights.climateUsage') }}</span>
                    <span class="status-card__value">{{ climateUsagePct !== null ? `${climateUsagePct}%` : '-' }}</span>
                  </div>
                </div>
              </CardInfoWrap>

              <CardInfoWrap :title="t('statistics.insights.commutePattern')" :description="t('statistics.cardDesc.commutePattern')">
                <div class="status-card">
                  <div class="status-card__body">
                    <span class="status-card__label">{{ t('statistics.insights.commutePattern') }}</span>
                    <span class="status-card__value">{{ peakDriveHour ?? '-' }}</span>
                  </div>
                </div>
              </CardInfoWrap>

              <CardInfoWrap :title="t('statistics.insights.batteryVoltageTrend')" :description="t('statistics.cardDesc.batteryVoltageTrend')">
                <div class="status-card">
                  <div class="status-card__body">
                    <span class="status-card__label">{{ t('statistics.insights.batteryVoltageTrend') }}</span>
                    <span class="status-card__value">{{ batteryVoltageDisplay ?? '-' }}</span>
                  </div>
                </div>
              </CardInfoWrap>

              <CardInfoWrap :title="t('statistics.insights.parkingLocations')" :description="t('statistics.cardDesc.parkingLocations')">
                <div class="status-card">
                  <div class="status-card__body">
                    <span class="status-card__label">{{ t('statistics.insights.parkingLocations') }}</span>
                    <span class="status-card__value">{{ parkingLocations ?? '-' }}</span>
                  </div>
                </div>
              </CardInfoWrap>
            </template>
          </div>
        </section>

        <template v-if="store.history.length">
          <div v-if="store.error" class="empty-state text-danger mt-2">{{ store.error }}</div>
          <div class="stats-chart-grid">
            <div v-if="hasFuel" class="chart-container">
              <h2>{{ t('vehicle.fuel') }}</h2>
              <Line :data="fuelChartData" :options="percentOptions" />
            </div>

            <div v-if="hasLargeEv" class="chart-container">
              <h2>{{ t('vehicle.evSoc') }}</h2>
              <Line :data="evChartData" :options="percentOptions" />
            </div>

            <div class="chart-container">
              <h2>{{ t('vehicle.tyres') }}</h2>
              <Line :data="tyreChartData" :options="pressureOptions" />
            </div>
          </div>
        </template>
      </template>
    </template>
  </div>
</template>

<style scoped>
.stats-days-select {
  width: auto;
}

.stats-insights {
  margin-bottom: 1rem;
}

.stats-chart-grid {
  display: grid;
  width: 100%;
  grid-template-columns: 1fr;
  gap: 1rem;
}

.stats-chart-grid .chart-container {
  min-width: 0;
  width: 100%;
  overflow: hidden;
}

.stats-chart-grid :deep(canvas) {
  max-width: 100% !important;
}

@media (min-width: 992px) {
  .stats-chart-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 1.25rem;
  }
}

@media (min-width: 1500px) {
  .stats-chart-grid {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }
}
</style>
