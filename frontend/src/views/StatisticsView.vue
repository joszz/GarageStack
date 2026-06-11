<script setup lang="ts">
import { onMounted, computed, ref, watch, shallowRef, nextTick } from 'vue'
import { useI18n } from 'vue-i18n'
import { useVehicleStore } from '@/stores/vehicle'
import { useSettingsStore } from '@/stores/settings'
import { defaultStatsInsights, defaultStatsCharts } from '@/stores/settings'
import type { StatsInsightId, StatsChartId } from '@/stores/settings'
import { vehicleApi } from '@/services/api'
import type { TelemetrySnapshot, VehicleAggregateStats } from '@/services/api'
import { Line, Bar } from 'vue-chartjs'
import { VueDraggable } from 'vue-draggable-plus'
import { LMap, LTileLayer, LMarker } from '@vue-leaflet/vue-leaflet'
import * as LModule from 'leaflet'
import type { Map as LeafletMap } from 'leaflet'
import CardInfoWrap from '@/components/CardInfoWrap.vue'
import DetailModal from '@/components/DetailModal.vue'
import FiltersPanel from '@/components/FiltersPanel.vue'
import SkeletonCard from '@/components/SkeletonCard.vue'
import SkeletonChart from '@/components/SkeletonChart.vue'
import StatusCard from '@/components/StatusCard.vue'
import {
  Chart as ChartJS,
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  BarElement,
  BarController,
  Title,
  Tooltip,
  Legend,
  Filler,
} from 'chart.js'

ChartJS.register(
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  BarElement,
  BarController,
  Title,
  Tooltip,
  Legend,
  Filler,
)

const L = ((LModule as unknown as { default?: typeof LModule }).default ??
  LModule) as typeof LModule

const { t } = useI18n()
const store = useVehicleStore()
const settings = useSettingsStore()

const editMode = ref(false)
const loading = ref(false)
const aggregateStats = ref<VehicleAggregateStats | null>(null)

const days = computed({
  get: () => settings.filterDays,
  set: (v: number) => {
    settings.filterDays = v
  },
})

const vin = computed(() => store.vehicles[0]?.vin ?? null)
const status = computed(() => store.currentStatus)

async function load() {
  loading.value = true
  try {
    await store.fetchVehicles()
    if (!vin.value) return
    const startDay = new Date()
    startDay.setDate(startDay.getDate() - days.value)
    const from = new Date(
      startDay.getFullYear(),
      startDay.getMonth(),
      startDay.getDate(),
    ).toISOString()
    const [, , , , stats] = await Promise.all([
      store.fetchHistory(vin.value, from),
      store.fetchTrips(vin.value, from),
      store.fetchStatus(vin.value),
      store.fetchConfig(vin.value),
      vehicleApi.stats(vin.value, from),
    ])
    aggregateStats.value = stats
  } finally {
    loading.value = false
  }
}

onMounted(load)
watch(() => settings.filterDays, load)

const effectiveVehicleType = computed(() => {
  if (settings.vehicleTypeOverride !== 'auto') return settings.vehicleTypeOverride
  return store.detectedVehicleType
})

const hasLargeEv = computed(
  () =>
    effectiveVehicleType.value === 'phev' ||
    effectiveVehicleType.value === 'bev' ||
    effectiveVehicleType.value === 'unknown',
)
const isHybrid = computed(
  () => effectiveVehicleType.value === 'hev' || effectiveVehicleType.value === 'phev',
)
const isPhev = computed(() => effectiveVehicleType.value === 'phev')

// ── Icons ────────────────────────────────────────────────────

const INSIGHT_ICONS: Record<StatsInsightId, string> = {
  periodDistance: 'route',
  avgTripLength: 'car-side',
  climateUsage: 'wind',
  commutePattern: 'circle-info',
  batteryVoltageTrend: 'battery-three-quarters',
  parkingLocations: 'location-dot',
  electricShare: 'leaf',
}

const CHART_ICONS: Record<StatsChartId, string> = {
  evChart: 'bolt',
  tyreChart: 'gauge',
  hybridSocChart: 'wave-square',
  dailyKwhChart: 'bolt-lightning',
}

// ── History grouping ─────────────────────────────────────────

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
    if (existing) existing.push(snapshot)
    else buckets.set(key, [snapshot])
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

// ── Insight computed values ───────────────────────────────────

const periodDistanceKm = computed(() => {
  if (!store.trips.length) return null
  return round2(store.trips.reduce((sum, trip) => sum + trip.distanceKm, 0))
})

const averageTripKm = computed(() => {
  if (!store.trips.length) return null
  return round2(store.trips.reduce((sum, trip) => sum + trip.distanceKm, 0) / store.trips.length)
})

const climateUsagePct = computed(() => aggregateStats.value?.climateUsagePct ?? null)

const peakDriveHour = computed(() => {
  if (!store.trips.length) return null
  const counts = new Map<number, number>()
  for (const trip of store.trips) {
    const hour = new Date(trip.startedAt).getHours()
    counts.set(hour, (counts.get(hour) ?? 0) + 1)
  }
  let bestHour = 0,
    bestCount = 0
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
    const last = trip.points[trip.points.length - 1]
    if (last) spots.add(`${last.latitude.toFixed(3)},${last.longitude.toFixed(3)}`)
  }
  return spots.size
})

const batteryVoltageTrend = computed(() => {
  const dailyAvg = groupedHistory.value
    .map((d) => avg(d.snapshots.map((s) => s.batteryVoltage)))
    .filter((v): v is number => v !== null)
  if (dailyAvg.length < 2) return null
  const first = dailyAvg[0]!,
    last = dailyAvg[dailyAvg.length - 1]!
  const delta = round2(last - first)
  return `${delta > 0 ? '+' : ''}${delta} V`
})

const batteryVoltageDisplay = computed(() => {
  if (batteryVoltageTrend.value !== null) return batteryVoltageTrend.value
  const v = status.value?.batteryVoltage
  return v != null ? `${v.toFixed(1)} V` : null
})

const electricShareToday = computed(() => {
  const s = status.value
  if (!s?.mileageSinceLastCharge || !s?.mileageOfTheDay || s.mileageOfTheDay === 0) return null
  return Math.min(100, Math.round((s.mileageSinceLastCharge / s.mileageOfTheDay) * 100))
})

// ── Parking locations modal ───────────────────────────────────

const parkingModalOpen = ref(false)
const parkingMapInstance = shallowRef<LeafletMap | null>(null)

const parkingCoordinates = computed<Array<{ lat: number; lng: number }>>(() => {
  if (!store.trips.length) return []
  const spots = new Map<string, { lat: number; lng: number }>()
  for (const trip of store.trips) {
    const last = trip.points[trip.points.length - 1]
    if (last) {
      const key = `${last.latitude.toFixed(3)},${last.longitude.toFixed(3)}`
      if (!spots.has(key)) spots.set(key, { lat: last.latitude, lng: last.longitude })
    }
  }
  return Array.from(spots.values())
})

const parkingMapCenter = computed<[number, number]>(() =>
  parkingCoordinates.value.length
    ? [parkingCoordinates.value[0]!.lat, parkingCoordinates.value[0]!.lng]
    : [52.3676, 4.9041],
)

function onParkingMapReady(map: LeafletMap) {
  parkingMapInstance.value = map
  nextTick(() => {
    requestAnimationFrame(() => {
      map.invalidateSize()
      const pts = parkingCoordinates.value
      if (!pts.length) return
      const bounds = L.latLngBounds(pts.map((c) => [c.lat, c.lng] as [number, number]))
      if (bounds.getNorthEast().equals(bounds.getSouthWest())) {
        map.setView(bounds.getCenter(), 15, { animate: false })
      } else {
        map.fitBounds(bounds, { padding: [24, 24], animate: false })
      }
    })
  })
}

// ── Insight card definitions ──────────────────────────────────

const insightDefs = computed(() => [
  {
    id: 'periodDistance' as StatsInsightId,
    icon: INSIGHT_ICONS.periodDistance,
    title:
      days.value === 30
        ? t('statistics.insights.monthlyMileage')
        : t('statistics.insights.distanceInRange'),
    description: t('statistics.cardDesc.distanceInRange'),
    value: periodDistanceKm.value !== null ? String(periodDistanceKm.value) : null,
    unit: t('common.km'),
    vehicleApplicable: true,
    applicable: store.history.length > 0,
  },
  {
    id: 'avgTripLength' as StatsInsightId,
    icon: INSIGHT_ICONS.avgTripLength,
    title: t('statistics.insights.avgTripLength'),
    description: t('statistics.cardDesc.avgTripLength'),
    value: averageTripKm.value !== null ? String(averageTripKm.value) : null,
    unit: t('common.km'),
    vehicleApplicable: true,
    applicable: store.history.length > 0,
  },
  {
    id: 'climateUsage' as StatsInsightId,
    icon: INSIGHT_ICONS.climateUsage,
    title: t('statistics.insights.climateUsage'),
    description: t('statistics.cardDesc.climateUsage'),
    value: climateUsagePct.value !== null ? `${climateUsagePct.value}%` : null,
    vehicleApplicable: true,
    applicable: store.history.length > 0,
  },
  {
    id: 'commutePattern' as StatsInsightId,
    icon: INSIGHT_ICONS.commutePattern,
    title: t('statistics.insights.commutePattern'),
    description: t('statistics.cardDesc.commutePattern'),
    value: peakDriveHour.value,
    vehicleApplicable: true,
    applicable: store.history.length > 0,
  },
  {
    id: 'batteryVoltageTrend' as StatsInsightId,
    icon: INSIGHT_ICONS.batteryVoltageTrend,
    title: t('statistics.insights.batteryVoltageTrend'),
    description: t('statistics.cardDesc.batteryVoltageTrend'),
    value: batteryVoltageDisplay.value,
    vehicleApplicable: true,
    applicable: store.history.length > 0 || status.value?.batteryVoltage != null,
  },
  {
    id: 'parkingLocations' as StatsInsightId,
    icon: INSIGHT_ICONS.parkingLocations,
    title: t('statistics.insights.parkingLocations'),
    description: t('statistics.cardDesc.parkingLocations'),
    value: parkingLocations.value !== null ? String(parkingLocations.value) : null,
    vehicleApplicable: true,
    applicable: store.history.length > 0,
  },
  {
    id: 'electricShare' as StatsInsightId,
    icon: INSIGHT_ICONS.electricShare,
    title: t('statistics.insights.electricShare'),
    description: t('statistics.cardDesc.electricShare'),
    value: electricShareToday.value !== null ? `${electricShareToday.value}%` : null,
    vehicleApplicable: isPhev.value,
    applicable: isPhev.value && status.value != null,
  },
])

const insightDefMap = computed(() => new Map(insightDefs.value.map((d) => [d.id, d])))

// ── Chart data ────────────────────────────────────────────────

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

const hybridSocChartData = computed(() => ({
  labels: chartLabels(),
  datasets: [
    {
      label: `${t('vehicle.evSoc')} (%)`,
      data: groupedHistory.value.map((d) => avg(d.snapshots.map((s) => s.evSocPercent))),
      borderColor: '#10b981',
      backgroundColor: 'rgba(16,185,129,0)',
      fill: false,
      tension: 0.3,
      spanGaps: true,
      pointRadius: 2,
      pointHoverRadius: 4,
    },
    {
      label: `${t('vehicle.fuel')} (%)`,
      data: groupedHistory.value.map((d) => avg(d.snapshots.map((s) => s.fuelLevelPercent))),
      borderColor: '#3b82f6',
      backgroundColor: 'rgba(59,130,246,0)',
      fill: false,
      tension: 0.3,
      spanGaps: true,
      pointRadius: 2,
      pointHoverRadius: 4,
    },
  ],
}))

const dailyKwhChartData = computed(() => ({
  labels: chartLabels(),
  datasets: [
    {
      label: 'kWh',
      data: groupedHistory.value.map((d) => {
        const vals = d.snapshots
          .map((s) => s.powerUsageOfDay)
          .filter((v): v is number => v !== null)
        if (!vals.length) return null

        // Historical completed days: cumulative peak = day's total
        if (d.key !== toLocalDateKey(new Date())) {
          return round2(Math.max(...vals) / 1000)
        }

        // Today (partial day): sort by time and detect a true counter reset so we don't
        // show yesterday's carryover when no driving has happened yet after midnight
        const sorted = d.snapshots
          .slice()
          .sort((a, b) => new Date(a.recordedAt).getTime() - new Date(b.recordedAt).getTime())
          .map((s) => s.powerUsageOfDay)
          .filter((v): v is number => v !== null)

        // True reset: counter drops to <5% of recent peak (min 50 Wh to ignore noise)
        let lastResetIdx = -1
        let peak = sorted[0] ?? 0
        for (let i = 1; i < sorted.length; i++) {
          peak = Math.max(peak, sorted[i - 1]!)
          if (sorted[i]! < Math.max(peak * 0.05, 50)) lastResetIdx = i
        }

        if (lastResetIdx >= 0) {
          const usage = Math.max(...sorted.slice(lastResetIdx))
          return usage > 0 ? round2(usage / 1000) : null
        }

        // No reset yet: net increase only (carryover with no driving → delta 0 → null)
        const delta = Math.max(...sorted) - Math.min(...sorted)
        return delta > 0 ? round2(delta / 1000) : null
      }),
      borderColor: '#f59e0b',
      backgroundColor: 'rgba(245,158,11,0.7)',
    },
  ],
}))

// ── Chart options ─────────────────────────────────────────────

const percentOptions = {
  responsive: true,
  maintainAspectRatio: true,
  aspectRatio: 2.6,
  animation: false as const,
  plugins: { legend: { display: false } },
  scales: {
    x: { ticks: { maxRotation: 0, autoSkip: true, maxTicksLimit: 8 } },
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
    x: { ticks: { maxRotation: 0, autoSkip: true, maxTicksLimit: 8 } },
    y: { min: 1.5, max: 3.5 },
  },
}
const hybridSocOptions = {
  responsive: true,
  maintainAspectRatio: true,
  aspectRatio: 2.6,
  animation: false as const,
  plugins: { legend: { display: true } },
  scales: {
    x: { ticks: { maxRotation: 0, autoSkip: true, maxTicksLimit: 8 } },
    y: { min: 0, max: 100 },
  },
}
const kwhOptions = {
  responsive: true,
  maintainAspectRatio: true,
  aspectRatio: 2.6,
  animation: false as const,
  plugins: { legend: { display: false } },
  scales: { x: { ticks: { maxRotation: 0, autoSkip: true, maxTicksLimit: 8 } }, y: { min: 0 } },
}

// ── Chart definitions ─────────────────────────────────────────

const chartDefs = computed(() => [
  {
    id: 'evChart' as StatsChartId,
    icon: CHART_ICONS.evChart,
    title: t('vehicle.evSoc'),
    vehicleApplicable: hasLargeEv.value,
    applicable: hasLargeEv.value && store.history.length > 0,
    isBar: false,
    data: evChartData.value,
    options: percentOptions,
  },
  {
    id: 'tyreChart' as StatsChartId,
    icon: CHART_ICONS.tyreChart,
    title: t('vehicle.tyres'),
    vehicleApplicable: true,
    applicable: store.history.length > 0,
    isBar: false,
    data: tyreChartData.value,
    options: pressureOptions,
  },
  {
    id: 'hybridSocChart' as StatsChartId,
    icon: CHART_ICONS.hybridSocChart,
    title: t('statistics.hybridSocChart'),
    vehicleApplicable: isHybrid.value,
    applicable: isHybrid.value && store.history.length > 0,
    isBar: false,
    data: hybridSocChartData.value,
    options: hybridSocOptions,
  },
  {
    id: 'dailyKwhChart' as StatsChartId,
    icon: CHART_ICONS.dailyKwhChart,
    title: t('statistics.dailyKwhChart'),
    vehicleApplicable: isHybrid.value,
    applicable: isHybrid.value && store.history.length > 0,
    isBar: true,
    data: dailyKwhChartData.value,
    options: kwhOptions,
  },
])

const chartDefMap = computed(() => new Map(chartDefs.value.map((d) => [d.id, d])))

// ── Layout reset ──────────────────────────────────────────────

function resetStatsLayout() {
  settings.statsInsights = defaultStatsInsights()
  settings.statsCharts = defaultStatsCharts()
}

const skeletonInsights = computed(() =>
  settings.statsInsights.filter(
    (i) => i.visible && insightDefMap.value.get(i.id)?.vehicleApplicable !== false,
  ),
)
const skeletonChartCount = computed(
  () =>
    settings.statsCharts.filter(
      (c) => c.visible && chartDefMap.value.get(c.id)?.vehicleApplicable !== false,
    ).length || 3,
)
</script>

<template>
  <div class="view-container">
    <div class="view-header">
      <h1>{{ t('nav.statistics') }}</h1>
      <div class="view-header__actions">
        <FiltersPanel>
          <div class="settings-toggle">
            <div class="settings-toggle__info">
              <span class="settings-toggle__label">{{ t('trips.dateRange') }}</span>
            </div>
            <div class="settings-toggle__control">
              <select v-model="days" class="form-select form-select-sm">
                <option :value="7">{{ t('trips.last7days') }}</option>
                <option :value="30">{{ t('trips.last30days') }}</option>
                <option :value="90">{{ t('trips.last90days') }}</option>
              </select>
            </div>
          </div>
        </FiltersPanel>
        <button
          class="btn btn-sm"
          :class="editMode ? 'btn-primary' : 'btn-outline-secondary'"
          @click="editMode = !editMode"
        >
          <font-awesome-icon :icon="editMode ? 'check' : 'pen-to-square'" />
          {{ editMode ? t('dashboard.doneEditing') : t('dashboard.editLayout') }}
        </button>
      </div>
    </div>

    <template v-if="loading">
      <section class="stats-insights" aria-label="Statistics insights">
        <div class="status-grid">
          <SkeletonCard
            v-for="item in skeletonInsights"
            :key="item.id"
            :icon="INSIGHT_ICONS[item.id]"
          />
        </div>
      </section>
      <div class="stats-chart-grid">
        <SkeletonChart v-for="n in skeletonChartCount" :key="n" />
      </div>
    </template>

    <template v-else>
      <div v-if="store.error && !status && !store.history.length" class="empty-state text-danger">
        {{ store.error }}
      </div>
      <div v-else-if="!status && !store.history.length && !editMode" class="empty-state">
        {{ t('dashboard.noData') }}
      </div>

      <template v-else>
        <!-- ── Insights ──────────────────────────────────── -->
        <section class="stats-insights" aria-label="Statistics insights">
          <!-- Edit mode: draggable card slots -->
          <VueDraggable
            v-if="editMode"
            v-model="settings.statsInsights"
            class="status-grid status-grid--edit"
            :animation="200"
            ghost-class="card-slot--ghost"
            chosen-class="card-slot--chosen"
            handle=".card-slot__handle"
          >
            <div
              v-for="item in settings.statsInsights"
              v-show="insightDefMap.get(item.id)?.vehicleApplicable !== false"
              :key="item.id"
              class="card-slot"
              :class="{ 'card-slot--hidden': !item.visible }"
            >
              <div class="card-slot__handle">
                <font-awesome-icon icon="grip-lines" />
              </div>
              <div class="card-slot__content">
                <template
                  v-if="
                    insightDefMap.get(item.id)?.applicable &&
                    insightDefMap.get(item.id)?.value !== null
                  "
                >
                  <div class="status-card">
                    <div class="status-card__icon">
                      <font-awesome-icon :icon="insightDefMap.get(item.id)!.icon" />
                    </div>
                    <div class="status-card__body">
                      <span class="status-card__label">{{
                        insightDefMap.get(item.id)!.title
                      }}</span>
                      <span class="status-card__value">
                        {{ insightDefMap.get(item.id)!.value }}
                        <span v-if="insightDefMap.get(item.id)!.unit" class="status-card__unit">
                          {{ insightDefMap.get(item.id)!.unit }}</span
                        >
                      </span>
                    </div>
                  </div>
                </template>
                <div v-else class="card-slot__placeholder">
                  <font-awesome-icon :icon="insightDefMap.get(item.id)?.icon ?? 'circle-info'" />
                  <span>{{ insightDefMap.get(item.id)?.title }}</span>
                </div>
              </div>
              <button
                class="card-slot__badge"
                :class="item.visible ? 'card-slot__badge--hide' : 'card-slot__badge--show'"
                :aria-label="item.visible ? t('dashboard.hideCard') : t('dashboard.showCard')"
                @click.stop="item.visible = !item.visible"
              >
                <font-awesome-icon :icon="item.visible ? 'xmark' : 'plus'" />
              </button>
            </div>
          </VueDraggable>

          <!-- Normal mode: visible + applicable insights -->
          <div v-else class="status-grid">
            <template v-for="item in settings.statsInsights" :key="item.id">
              <template v-if="item.visible && insightDefMap.get(item.id)?.applicable">
                <template v-if="item.id === 'parkingLocations'">
                  <StatusCard
                    :icon="insightDefMap.get(item.id)!.icon"
                    :label="insightDefMap.get(item.id)!.title"
                    :value="insightDefMap.get(item.id)!.value"
                    clickable
                    @click="parkingModalOpen = true"
                  />
                </template>
                <template v-else>
                  <CardInfoWrap
                    :title="insightDefMap.get(item.id)!.title"
                    :description="insightDefMap.get(item.id)!.description"
                  >
                    <div class="status-card">
                      <div class="status-card__icon">
                        <font-awesome-icon :icon="insightDefMap.get(item.id)!.icon" />
                      </div>
                      <div class="status-card__body">
                        <span class="status-card__label">{{
                          insightDefMap.get(item.id)!.title
                        }}</span>
                        <span class="status-card__value">
                          {{ insightDefMap.get(item.id)!.value ?? '-' }}
                          <span
                            v-if="
                              insightDefMap.get(item.id)!.value !== null &&
                              insightDefMap.get(item.id)!.unit
                            "
                            class="status-card__unit"
                          >
                            {{ insightDefMap.get(item.id)!.unit }}</span
                          >
                        </span>
                      </div>
                    </div>
                  </CardInfoWrap>
                </template>
              </template>
            </template>
          </div>
        </section>

        <!-- ── Charts ───────────────────────────────────── -->
        <template v-if="editMode || store.history.length">
          <div v-if="store.error" class="empty-state text-danger mt-2">{{ store.error }}</div>

          <!-- Edit mode: draggable chart slots -->
          <VueDraggable
            v-if="editMode"
            v-model="settings.statsCharts"
            class="stats-chart-grid"
            :animation="200"
            ghost-class="card-slot--ghost"
            chosen-class="card-slot--chosen"
            handle=".card-slot__handle"
          >
            <div
              v-for="item in settings.statsCharts"
              v-show="chartDefMap.get(item.id)?.vehicleApplicable !== false"
              :key="item.id"
              class="card-slot card-slot--chart"
              :class="{ 'card-slot--hidden': !item.visible }"
            >
              <div class="card-slot__handle">
                <font-awesome-icon icon="grip-lines" />
              </div>
              <div class="card-slot__content">
                <div
                  v-if="chartDefMap.get(item.id)?.applicable && store.history.length"
                  class="chart-container"
                >
                  <h2>{{ chartDefMap.get(item.id)!.title }}</h2>
                  <Bar
                    v-if="chartDefMap.get(item.id)!.isBar"
                    :data="chartDefMap.get(item.id)!.data"
                    :options="chartDefMap.get(item.id)!.options"
                  />
                  <Line
                    v-else
                    :data="chartDefMap.get(item.id)!.data"
                    :options="chartDefMap.get(item.id)!.options"
                  />
                </div>
                <div v-else class="card-slot__placeholder card-slot__placeholder--chart">
                  <font-awesome-icon :icon="chartDefMap.get(item.id)?.icon ?? 'chart-line'" />
                  <span>{{ chartDefMap.get(item.id)?.title }}</span>
                </div>
              </div>
              <button
                class="card-slot__badge"
                :class="item.visible ? 'card-slot__badge--hide' : 'card-slot__badge--show'"
                :aria-label="item.visible ? t('dashboard.hideCard') : t('dashboard.showCard')"
                @click.stop="item.visible = !item.visible"
              >
                <font-awesome-icon :icon="item.visible ? 'xmark' : 'plus'" />
              </button>
            </div>
          </VueDraggable>

          <!-- Normal mode: visible + applicable charts -->
          <div v-else class="stats-chart-grid">
            <template v-for="item in settings.statsCharts" :key="item.id">
              <div
                v-if="item.visible && chartDefMap.get(item.id)?.applicable"
                class="chart-container"
              >
                <h2>{{ chartDefMap.get(item.id)!.title }}</h2>
                <Bar
                  v-if="chartDefMap.get(item.id)!.isBar"
                  :data="chartDefMap.get(item.id)!.data"
                  :options="chartDefMap.get(item.id)!.options"
                />
                <Line
                  v-else
                  :data="chartDefMap.get(item.id)!.data"
                  :options="chartDefMap.get(item.id)!.options"
                />
              </div>
            </template>
          </div>
        </template>

        <!-- Reset + done editing -->
        <template v-if="editMode">
          <button class="btn btn-outline-secondary mt-3" @click="resetStatsLayout">
            <font-awesome-icon icon="rotate-left" />
            {{ t('dashboard.resetLayout') }}
          </button>
        </template>

        <!-- Parking locations map modal -->
        <DetailModal
          :open="parkingModalOpen"
          :title="t('statistics.insights.parkingLocations')"
          wide
          @close="parkingModalOpen = false"
        >
          <div class="parking-modal-map">
            <LMap :zoom="13" :center="parkingMapCenter" @ready="onParkingMapReady">
              <LTileLayer
                url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
                attribution="&copy; OpenStreetMap contributors"
              />
              <LMarker
                v-for="(spot, i) in parkingCoordinates"
                :key="i"
                :lat-lng="[spot.lat, spot.lng]"
              />
            </LMap>
          </div>
        </DetailModal>
      </template>
    </template>
  </div>
</template>

<style scoped>
.parking-modal-map {
  height: 280px;
  width: 100%;
  border-radius: var(--radius-sm, 4px);
  overflow: hidden;
}

@media (min-width: 768px) {
  .parking-modal-map {
    height: 420px;
  }
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
