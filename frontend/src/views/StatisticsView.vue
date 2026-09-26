<script setup lang="ts">
import '@/assets/statistics.css'
import { onMounted, computed, ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useI18n } from 'vue-i18n'
import { useVehicleStore } from '@/stores/vehicle'
import { useDashboardSettingsStore } from '@/stores/settingsDashboard'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { defaultStatsInsights, defaultStatsCharts } from '@/stores/settingsShared'
import type { StatsInsightId, StatsChartId } from '@/stores/settingsShared'
import type { Quantity } from '@/utils/units'
import { vehicleApi } from '@/services/vehicleApi'
import type { TelemetryHistoryPoint, VehicleAggregateStats } from '@/services/vehicleApi'
import type { ChartData, ChartOptions } from 'chart.js'
import { VueDraggable } from 'vue-draggable-plus'
import { LMap, LMarker } from '@vue-leaflet/vue-leaflet'
import { L, type LeafletMap } from '@/utils/leaflet'
import { useLeafletMap } from '@/composables/useLeafletMap'
import { useBasemap } from '@/composables/useBasemap'
import CardInfoWrap from '@/components/CardInfoWrap.vue'
import DetailModal from '@/components/DetailModal.vue'
import ToolbarPanel from '@/components/ToolbarPanel.vue'
import SkeletonCard from '@/components/SkeletonCard.vue'
import SkeletonChart from '@/components/SkeletonChart.vue'
import StatusCard from '@/components/StatusCard.vue'
import StatsChartCard, { type StatsChartType } from '@/components/StatsChartCard.vue'
import EditableCardSlot from '@/components/EditableCardSlot.vue'
import { formatNumber } from '@/utils/format'
import { dailyCounterTotal, energyUnit, litres } from '@/utils/energy'
import { startOfLocalDayDaysAgoIso } from '@/utils/dates'
import {
  averageMovingSpeedKmh,
  averageTripKm as averageTripDistanceKm,
  hasSpeedReadings,
  parkingSpots,
  peakDriveHour as peakTripHour,
  totalDistanceKm,
} from '@/utils/statistics'
import { useUnits } from '@/composables/useUnits'

const { t } = useI18n()
const store = useVehicleStore()
const settings = useDashboardSettingsStore()
const uiSettings = useUiSettingsStore()
const units = useUnits()

const editMode = ref(false)
const loading = ref(false)
const aggregateStats = ref<VehicleAggregateStats | null>(null)

// Plain ref on the store already (Composition-API-style defineStore) - storeToRefs gives a
// directly writable, reactive binding with no computed({get, set}) wrapper needed.
const { filterDays: days } = storeToRefs(uiSettings)

const vin = computed(() => store.activeVin)
const status = computed(() => store.currentStatus)

async function load() {
  loading.value = true
  try {
    await store.fetchVehicles()
    if (!vin.value) return
    const from = startOfLocalDayDaysAgoIso(days.value)
    const [, , , , stats] = await Promise.all([
      store.fetchHistory(vin.value, from),
      store.fetchTripSummaries(vin.value, from),
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
watch(() => uiSettings.filterDays, load)

// Trip just finished: refresh stats and trip list silently
watch(
  () => store.tripJustCompleted,
  (completed) => {
    if (completed) load()
  },
)

const vehicleType = computed(() => store.effectiveVehicleType)
const hasLargeEv = computed(
  () =>
    vehicleType.value === 'phev' || vehicleType.value === 'bev' || vehicleType.value === 'unknown',
)
const isHybrid = computed(() => vehicleType.value === 'hev' || vehicleType.value === 'phev')
const isPhev = computed(() => vehicleType.value === 'phev')

// ── Icons ────────────────────────────────────────────────────

const INSIGHT_ICONS: Record<StatsInsightId, string> = {
  periodDistance: 'route',
  avgTripLength: 'car-side',
  climateUsage: 'wind',
  commutePattern: 'circle-info',
  batteryVoltageTrend: 'battery-three-quarters',
  parkingLocations: 'location-dot',
  electricShare: 'leaf',
  avgSpeed: 'gauge-high',
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
  const buckets = new Map<string, TelemetryHistoryPoint[]>()

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

  for (const point of store.history) {
    const key = toLocalDateKey(new Date(point.recordedAt))
    const existing = buckets.get(key)
    if (existing) existing.push(point)
    else buckets.set(key, [point])
  }

  return Array.from(buckets.entries()).map(([key, points]) => ({
    key,
    label: new Date(`${key}T00:00:00`).toLocaleDateString(),
    points,
  }))
})

const chartLabels = computed(() => groupedHistory.value.map((d) => d.label))

// Daily average of one history field, in chart order.
function dailyAverages(read: (p: TelemetryHistoryPoint) => number | null) {
  return groupedHistory.value.map((d) => avg(d.points.map(read)))
}

// ── Insight computed values ───────────────────────────────────

// Unrounded: the unit formatter rounds once, in the unit it shows.
const periodDistanceKm = computed(() => totalDistanceKm(store.tripSummaries))
const averageTripKm = computed(() => averageTripDistanceKm(store.tripSummaries))

const climateUsagePct = computed(() => aggregateStats.value?.climateUsagePct ?? null)

const peakDriveHour = computed(() => peakTripHour(store.tripSummaries))

// Shared with the parking-locations modal below - parkingLocations is just this list's count.
const parkingCoordinates = computed(() => parkingSpots(store.tripSummaries))

const parkingLocations = computed(() =>
  store.tripSummaries.length ? parkingCoordinates.value.length : null,
)

const batteryVoltageTrend = computed(() => {
  const dailyAvg = dailyAverages((p) => p.batteryVoltage).filter((v): v is number => v !== null)
  if (dailyAvg.length < 2) return null
  const first = dailyAvg[0]!,
    last = dailyAvg[dailyAvg.length - 1]!
  const delta = round2(last - first)
  return `${delta > 0 ? '+' : ''}${delta} V`
})

const batteryVoltageDisplay = computed(() => {
  if (batteryVoltageTrend.value !== null) return batteryVoltageTrend.value
  const v = status.value?.batteryVoltage
  return v != null ? `${formatNumber(v)} V` : null
})

const avgSpeedKmh = computed(() => averageMovingSpeedKmh(store.tripSummaries))

const electricShareToday = computed(() => {
  const s = status.value
  if (!s?.mileageSinceLastCharge || !s?.mileageOfTheDay || s.mileageOfTheDay === 0) return null
  return Math.min(100, Math.round((s.mileageSinceLastCharge / s.mileageOfTheDay) * 100))
})

// ── Parking locations modal ───────────────────────────────────

const activeChartInfo = ref<StatsChartId | null>(null)

const parkingModalOpen = ref(false)
const { mapInstance: parkingMapInstance, bindMapReady: bindParkingMapReady } = useLeafletMap()
useBasemap(parkingMapInstance)

const parkingMapCenter = computed<[number, number]>(() =>
  parkingCoordinates.value.length
    ? [parkingCoordinates.value[0]!.lat, parkingCoordinates.value[0]!.lng]
    : [52.3676, 4.9041],
)

function onParkingMapReady(map: LeafletMap) {
  bindParkingMapReady(map, () => {
    const pts = parkingCoordinates.value
    if (!pts.length) return
    const bounds = L.latLngBounds(pts.map((c) => [c.lat, c.lng] as [number, number]))
    if (bounds.getNorthEast().equals(bounds.getSouthWest())) {
      map.setView(bounds.getCenter(), 15, { animate: false })
    } else {
      map.fitBounds(bounds, { padding: [24, 24], animate: false })
    }
  })
}

function onInsightClick(id: StatsInsightId) {
  if (id === 'parkingLocations') parkingModalOpen.value = true
}

// ── Insight card definitions ──────────────────────────────────

interface InsightDef {
  id: StatsInsightId
  icon: string
  title: string
  description: string
  value: string | null
  unit?: string
  vehicleApplicable: boolean
  applicable: boolean
  clickable?: boolean
}

// An insight's value and unit from a metric figure, in the browser's units.
function measuredInsight(
  quantity: Quantity,
  metric: number | null,
  decimals?: number,
): Pick<InsightDef, 'value' | 'unit'> {
  const m = units.value.measure(quantity, metric, { decimals })
  return { value: m?.value ?? null, unit: units.value.symbol(quantity) }
}

const insightDefs = computed((): InsightDef[] => [
  {
    id: 'periodDistance' as StatsInsightId,
    icon: INSIGHT_ICONS.periodDistance,
    title:
      days.value === 30
        ? t('statistics.insights.monthlyMileage')
        : t('statistics.insights.distanceInRange'),
    description: t('statistics.cardDesc.distanceInRange'),
    ...measuredInsight('distance', periodDistanceKm.value),
    vehicleApplicable: true,
    applicable: store.history.length > 0,
  },
  {
    id: 'avgTripLength' as StatsInsightId,
    icon: INSIGHT_ICONS.avgTripLength,
    title: t('statistics.insights.avgTripLength'),
    description: t('statistics.cardDesc.avgTripLength'),
    ...measuredInsight('distance', averageTripKm.value),
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
    clickable: true,
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
  {
    id: 'avgSpeed' as StatsInsightId,
    icon: INSIGHT_ICONS.avgSpeed,
    title: t('statistics.insights.avgSpeed'),
    description: t('statistics.cardDesc.avgSpeed'),
    // An average earns the decimal a live speed reading does without.
    ...measuredInsight('speed', avgSpeedKmh.value, 1),
    vehicleApplicable: true,
    applicable: hasSpeedReadings(store.tripSummaries),
  },
])

const insightDefMap = computed(() => new Map(insightDefs.value.map((d) => [d.id, d])))

// ── Chart data ────────────────────────────────────────────────

// Every line series shares the same shape and styling; only label, data and colour differ.
function lineDataset(label: string, data: Array<number | null>, color: string, fillColor?: string) {
  return {
    label,
    data,
    borderColor: color,
    backgroundColor: fillColor ?? 'transparent',
    fill: fillColor !== undefined,
    tension: 0.3,
    spanGaps: true,
    pointRadius: 2,
    pointHoverRadius: 4,
  }
}

const evChartData = computed(() => ({
  labels: chartLabels.value,
  datasets: [
    lineDataset(
      `${t('vehicle.evSoc')} (%)`,
      dailyAverages((p) => p.evSocPercent),
      '#10b981',
      'rgba(16,185,129,0.1)',
    ),
  ],
}))

const TYRE_SERIES: Array<{
  label: string
  read: (p: TelemetryHistoryPoint) => number | null
  color: string
}> = [
  { label: 'FL', read: (p) => p.tyrePressureFrontLeft, color: '#f59e0b' },
  { label: 'FR', read: (p) => p.tyrePressureFrontRight, color: '#ef4444' },
  { label: 'RL', read: (p) => p.tyrePressureRearLeft, color: '#8b5cf6' },
  { label: 'RR', read: (p) => p.tyrePressureRearRight, color: '#ec4899' },
]

const tyreChartData = computed(() => {
  const u = units.value
  return {
    labels: chartLabels.value,
    datasets: TYRE_SERIES.map(({ label, read, color }) =>
      lineDataset(
        `${label} (${u.symbol('pressure')})`,
        dailyAverages(read).map((bar) => (bar === null ? null : u.convert('pressure', bar))),
        color,
      ),
    ),
  }
})

const hybridSocChartData = computed(() => ({
  labels: chartLabels.value,
  datasets: [
    lineDataset(
      `${t('vehicle.evSoc')} (%)`,
      dailyAverages((p) => p.evSocPercent),
      '#10b981',
    ),
    lineDataset(
      `${t('vehicle.fuel')} (%)`,
      dailyAverages((p) => p.fuelLevelPercent),
      '#3b82f6',
    ),
  ],
}))

// The same counter, read in the unit this drivetrain actually reports: kWh out of the traction
// battery on a plug-in car, litres of fuel on a plain hybrid. See utils/energy.
const reportsFuelCounter = computed(() => energyUnit(vehicleType.value) === 'litres')

const dailyEnergyChartData = computed(() => ({
  labels: chartLabels.value,
  datasets: [
    {
      label: reportsFuelCounter.value ? units.value.symbol('volume') : t('common.kwh'),
      data: groupedHistory.value.map((d) => {
        const readings = d.points
          .slice()
          .sort((a, b) => new Date(a.recordedAt).getTime() - new Date(b.recordedAt).getTime())
          .map((p) => p.powerUsageOfDay)
          .filter((v): v is number => v !== null)
        const total = dailyCounterTotal(readings, d.key === toLocalDateKey(new Date()))
        const fuel = reportsFuelCounter.value ? litres(total) : null
        const used = fuel !== null ? units.value.convert('volume', fuel) : total
        return used !== null ? round2(used) : null
      }),
      borderColor: '#f59e0b',
      backgroundColor: 'rgba(245,158,11,0.7)',
    },
  ],
}))

// ── Chart options ─────────────────────────────────────────────

// All charts share the same responsive/axis setup; they differ only in aspect ratio, legend
// and y-axis range.
function chartOptions(aspectRatio: number, legend: boolean, y: { min: number; max?: number }) {
  return {
    responsive: true,
    maintainAspectRatio: true,
    aspectRatio,
    animation: false as const,
    plugins: { legend: { display: legend } },
    scales: {
      x: { ticks: { maxRotation: 0, autoSkip: true, maxTicksLimit: 8 } },
      y,
    },
  }
}

const percentOptions = chartOptions(2.6, false, { min: 0, max: 100 })
// Widens a range outward to round numbers, stepping by half its order of magnitude: 1.5 to 3.5
// bar stays as it is, and the same range reads 20 to 55 psi or 150 to 350 kPa rather than
// starting the axis at 21.76.
function roundedRange(min: number, max: number) {
  const step = 10 ** Math.floor(Math.log10(max - min)) / 2
  return { min: Math.floor(min / step) * step, max: Math.ceil(max / step) * step }
}

const pressureOptions = computed(() => {
  const u = units.value
  return chartOptions(
    2.3,
    true,
    roundedRange(u.convert('pressure', 1.5), u.convert('pressure', 3.5)),
  )
})
const hybridSocOptions = chartOptions(2.6, true, { min: 0, max: 100 })
const kwhOptions = chartOptions(2.6, false, { min: 0 })

// ── Chart definitions ─────────────────────────────────────────

interface ChartDef {
  id: StatsChartId
  icon: string
  title: string
  description: string
  vehicleApplicable: boolean
  applicable: boolean
  type: StatsChartType
  data: ChartData<StatsChartType>
  options: ChartOptions<StatsChartType>
}

const chartDefs = computed((): ChartDef[] => [
  {
    id: 'evChart',
    icon: CHART_ICONS.evChart,
    title: t('vehicle.evSoc'),
    description: t('statistics.chartDesc.evChart'),
    vehicleApplicable: hasLargeEv.value,
    applicable: hasLargeEv.value && store.history.length > 0,
    type: 'line',
    data: evChartData.value,
    options: percentOptions,
  },
  {
    id: 'tyreChart',
    icon: CHART_ICONS.tyreChart,
    title: t('vehicle.tyres'),
    description: t('statistics.chartDesc.tyreChart', {
      low: units.value.measure('pressure', 2)!.value,
      high: units.value.measure('pressure', 3)!.value,
      unit: units.value.symbol('pressure'),
    }),
    vehicleApplicable: true,
    applicable: store.history.length > 0,
    type: 'line',
    data: tyreChartData.value,
    options: pressureOptions.value,
  },
  {
    id: 'hybridSocChart',
    icon: CHART_ICONS.hybridSocChart,
    title: t('statistics.hybridSocChart'),
    description: t('statistics.chartDesc.hybridSocChart'),
    vehicleApplicable: isHybrid.value,
    applicable: isHybrid.value && store.history.length > 0,
    type: 'line',
    data: hybridSocChartData.value,
    options: hybridSocOptions,
  },
  {
    id: 'dailyKwhChart',
    icon: reportsFuelCounter.value ? 'gas-pump' : CHART_ICONS.dailyKwhChart,
    title: reportsFuelCounter.value
      ? t('statistics.dailyFuelChart', { unit: units.value.symbol('volume') })
      : t('statistics.dailyKwhChart'),
    description: reportsFuelCounter.value
      ? t('statistics.chartDesc.dailyFuelChart')
      : t('statistics.chartDesc.dailyKwhChart'),
    vehicleApplicable: isHybrid.value,
    applicable: isHybrid.value && store.history.length > 0,
    type: 'bar',
    data: dailyEnergyChartData.value,
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
        <ToolbarPanel>
          <div class="settings-toggle">
            <div class="settings-toggle__info">
              <span class="settings-toggle__label">
                <font-awesome-icon icon="calendar-check" class="settings-toggle__icon" />
                {{ t('trips.dateRange') }}
              </span>
              <span class="settings-toggle__desc">{{ t('trips.dateRangeDesc') }}</span>
            </div>
            <div class="settings-toggle__control">
              <select v-model="days" class="form-select form-select-sm">
                <option :value="7">{{ t('trips.last7days') }}</option>
                <option :value="30">{{ t('trips.last30days') }}</option>
                <option :value="90">{{ t('trips.last90days') }}</option>
              </select>
            </div>
          </div>
        </ToolbarPanel>
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
      <section class="stats-insights" :aria-label="t('statistics.insightsSectionLabel')">
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
      <div
        v-if="store.historyError && !status && !store.history.length"
        class="empty-state text-danger"
      >
        {{ store.historyError }}
      </div>
      <div v-else-if="!status && !store.history.length && !editMode" class="empty-state">
        {{ t('dashboard.noData') }}
      </div>

      <template v-else>
        <!-- ── Insights ──────────────────────────────────── -->
        <section class="stats-insights" :aria-label="t('statistics.insightsSectionLabel')">
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
            <EditableCardSlot
              v-for="item in settings.statsInsights"
              v-show="insightDefMap.get(item.id)?.vehicleApplicable !== false"
              :key="item.id"
              :visible="item.visible"
              @toggle-visible="item.visible = !item.visible"
            >
              <template
                v-if="
                  insightDefMap.get(item.id)?.applicable &&
                  insightDefMap.get(item.id)?.value !== null
                "
              >
                <StatusCard
                  :icon="insightDefMap.get(item.id)!.icon"
                  :label="insightDefMap.get(item.id)!.title"
                  :value="insightDefMap.get(item.id)!.value"
                  :unit="insightDefMap.get(item.id)!.unit"
                />
              </template>
              <div v-else class="card-slot__placeholder">
                <font-awesome-icon :icon="insightDefMap.get(item.id)?.icon ?? 'circle-info'" />
                <span>{{ insightDefMap.get(item.id)?.title }}</span>
              </div>
            </EditableCardSlot>
          </VueDraggable>

          <!-- Normal mode: visible + applicable insights -->
          <div v-else class="status-grid">
            <template v-for="item in settings.statsInsights" :key="item.id">
              <CardInfoWrap
                v-if="item.visible && insightDefMap.get(item.id)?.applicable"
                :title="insightDefMap.get(item.id)!.title"
                :description="insightDefMap.get(item.id)!.description"
              >
                <StatusCard
                  :icon="insightDefMap.get(item.id)!.icon"
                  :label="insightDefMap.get(item.id)!.title"
                  :value="insightDefMap.get(item.id)!.value"
                  :unit="insightDefMap.get(item.id)!.unit"
                  :clickable="insightDefMap.get(item.id)!.clickable === true"
                  @click="onInsightClick(item.id)"
                />
              </CardInfoWrap>
            </template>
          </div>
        </section>

        <!-- ── Charts ───────────────────────────────────── -->
        <template v-if="editMode || store.history.length">
          <div v-if="store.historyError" class="empty-state text-danger mt-2">
            {{ store.historyError }}
          </div>

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
            <EditableCardSlot
              v-for="item in settings.statsCharts"
              v-show="chartDefMap.get(item.id)?.vehicleApplicable !== false"
              :key="item.id"
              class="card-slot--chart"
              :visible="item.visible"
              @toggle-visible="item.visible = !item.visible"
            >
              <StatsChartCard
                v-if="chartDefMap.get(item.id)?.applicable && store.history.length"
                :title="chartDefMap.get(item.id)!.title"
                :type="chartDefMap.get(item.id)!.type"
                :data="chartDefMap.get(item.id)!.data"
                :options="chartDefMap.get(item.id)!.options"
                :show-info="uiSettings.showCardInfoIcons"
                @info="activeChartInfo = item.id"
              />
              <div v-else class="card-slot__placeholder card-slot__placeholder--chart">
                <font-awesome-icon :icon="chartDefMap.get(item.id)?.icon ?? 'chart-line'" />
                <span>{{ chartDefMap.get(item.id)?.title }}</span>
              </div>
            </EditableCardSlot>
          </VueDraggable>

          <!-- Normal mode: visible + applicable charts -->
          <div v-else class="stats-chart-grid">
            <template v-for="item in settings.statsCharts" :key="item.id">
              <StatsChartCard
                v-if="item.visible && chartDefMap.get(item.id)?.applicable"
                :title="chartDefMap.get(item.id)!.title"
                :type="chartDefMap.get(item.id)!.type"
                :data="chartDefMap.get(item.id)!.data"
                :options="chartDefMap.get(item.id)!.options"
                :show-info="uiSettings.showCardInfoIcons"
                @info="activeChartInfo = item.id"
              />
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

        <!-- Chart info modal -->
        <DetailModal
          :open="activeChartInfo !== null"
          :title="activeChartInfo ? chartDefMap.get(activeChartInfo)!.title : ''"
          @close="activeChartInfo = null"
        >
          <p class="card-info-desc">
            {{ activeChartInfo ? chartDefMap.get(activeChartInfo)!.description : '' }}
          </p>
        </DetailModal>

        <!-- Parking locations map modal -->
        <DetailModal
          :open="parkingModalOpen"
          :title="t('statistics.insights.parkingLocations')"
          wide
          @close="parkingModalOpen = false"
        >
          <div class="parking-modal-map">
            <LMap :zoom="13" :center="parkingMapCenter" @ready="onParkingMapReady">
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

@media (width >= 768px) {
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
}

.stats-chart-grid :deep(canvas) {
  max-width: 100% !important;
}

@media (width >= 992px) {
  .stats-chart-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 1.25rem;
  }
}

@media (width >= 1500px) {
  .stats-chart-grid {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }
}
</style>
