<script setup lang="ts">
import { onMounted, computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useVehicleStore } from '@/stores/vehicle'
import type { TelemetrySnapshot } from '@/services/api'
import { Line } from 'vue-chartjs'
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
const days = ref(7)

const vin = computed(() => store.vehicles[0]?.vin ?? null)
const status = computed(() => store.currentStatus)

async function load() {
  await store.fetchVehicles()
  if (!vin.value) return
  const from = new Date(Date.now() - days.value * 86_400_000).toISOString()
  await Promise.all([
    store.fetchHistory(vin.value, from),
    store.fetchStatus(vin.value),
  ])
}

onMounted(load)

function watchDays() {
  load()
}

// Efficiency computed from today's stats
const efficiencyWh = computed(() => {
  const s = status.value
  if (!s?.powerUsageOfDay || !s?.mileageOfTheDay || s.mileageOfTheDay === 0) return null
  return Math.round(s.powerUsageOfDay / s.mileageOfTheDay)
})

// Group history by local date to avoid overcrowded datetime labels on charts.
function toDateKey(isoTimestamp: string) {
  const d = new Date(isoTimestamp)
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

function avg(values: Array<number | null>) {
  const valid = values.filter((v): v is number => v !== null)
  if (!valid.length) return null
  return valid.reduce((sum, v) => sum + v, 0) / valid.length
}

const groupedHistory = computed(() => {
  const buckets = new Map<string, TelemetrySnapshot[]>()

  for (const snapshot of store.history) {
    const key = toDateKey(snapshot.recordedAt)
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
  maintainAspectRatio: false,
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
  maintainAspectRatio: false,
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
        <select v-model="days" class="form-select form-select-sm" style="width:auto" @change="watchDays">
          <option :value="1">24h</option>
          <option :value="7">7 days</option>
          <option :value="30">30 days</option>
        </select>
      </div>
    </div>

    <div v-if="store.loading" class="loading-state">
      <font-awesome-icon icon="spinner" spin />
      {{ t('common.loading') }}
    </div>

    <template v-else>
      <!-- Today's efficiency summary -->
      <div v-if="status" class="status-grid mb-4">
        <div class="status-card">
          <div class="status-card__icon"><font-awesome-icon icon="road" /></div>
          <div class="status-card__body">
            <span class="status-card__label">{{ t('vehicle.efficiency.todayDistance') }}</span>
            <span class="status-card__value">
              {{ status.mileageOfTheDay !== null ? Math.round(status.mileageOfTheDay) : '-' }}
              <span v-if="status.mileageOfTheDay !== null" class="status-card__unit"> {{ t('common.km') }}</span>
            </span>
          </div>
        </div>

        <div class="status-card">
          <div class="status-card__icon"><font-awesome-icon icon="bolt" /></div>
          <div class="status-card__body">
            <span class="status-card__label">{{ t('vehicle.efficiency.todayEnergy') }}</span>
            <span class="status-card__value">
              {{ status.powerUsageOfDay !== null ? Math.round(status.powerUsageOfDay) : '-' }}
              <span v-if="status.powerUsageOfDay !== null" class="status-card__unit"> {{ t('common.wh') }}</span>
            </span>
          </div>
        </div>

        <div class="status-card">
          <div class="status-card__icon"><font-awesome-icon icon="gauge-high" /></div>
          <div class="status-card__body">
            <span class="status-card__label">{{ t('vehicle.efficiency.efficiency') }}</span>
            <span class="status-card__value">
              {{ efficiencyWh !== null ? efficiencyWh : '-' }}
              <span v-if="efficiencyWh !== null" class="status-card__unit"> Wh/km</span>
            </span>
          </div>
        </div>

        <div class="status-card">
          <div class="status-card__icon"><font-awesome-icon icon="plug" /></div>
          <div class="status-card__body">
            <span class="status-card__label">{{ t('vehicle.efficiency.sinceCharge') }}</span>
            <span class="status-card__value">
              {{ status.mileageSinceLastCharge !== null ? Math.round(status.mileageSinceLastCharge) : '-' }}
              <span v-if="status.mileageSinceLastCharge !== null" class="status-card__unit"> {{ t('common.km') }}</span>
            </span>
          </div>
        </div>
      </div>

      <div v-if="store.error" class="empty-state text-danger">{{ store.error }}</div>
      <div v-else-if="!store.history.length" class="empty-state">{{ t('dashboard.noData') }}</div>

      <template v-else>
        <div class="chart-container">
          <h2>{{ t('vehicle.fuel') }}</h2>
          <Line :data="fuelChartData" :options="percentOptions" />
        </div>

        <div class="chart-container">
          <h2>{{ t('vehicle.evSoc') }}</h2>
          <Line :data="evChartData" :options="percentOptions" />
        </div>

        <div class="chart-container">
          <h2>{{ t('vehicle.tyres') }}</h2>
          <Line :data="tyreChartData" :options="pressureOptions" />
        </div>
      </template>
    </template>
  </div>
</template>
