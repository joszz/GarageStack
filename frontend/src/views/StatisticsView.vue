<script setup lang="ts">
import '@/assets/statistics.css'
import { onMounted, computed, defineAsyncComponent, ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useI18n } from 'vue-i18n'
import { useVehicleStore } from '@/stores/vehicle'
import { useDashboardSettingsStore } from '@/stores/settingsDashboard'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { defaultStatsInsights, defaultStatsCharts } from '@/stores/settingsShared'
import type { StatsInsightId, StatsChartId } from '@/stores/settingsShared'
import type { Quantity } from '@/utils/units'
import { vehicleApi } from '@/services/vehicleApi'
import type { VehicleAggregateStats } from '@/services/vehicleApi'
import { LMap, LMarker } from '@vue-leaflet/vue-leaflet'
import { DEFAULT_MAP_CENTER, fitToPoints, type LeafletMap } from '@/utils/leaflet'
import { useLeafletMap } from '@/composables/useLeafletMap'
import { useBasemap } from '@/composables/useBasemap'
import CardInfoWrap from '@/components/CardInfoWrap.vue'
import DetailModal from '@/components/DetailModal.vue'
import ToolbarPanel from '@/components/ToolbarPanel.vue'
import DateRangeFilter from '@/components/DateRangeFilter.vue'
import SkeletonCard from '@/components/SkeletonCard.vue'
import SkeletonChart from '@/components/SkeletonChart.vue'
import StatusCard from '@/components/StatusCard.vue'
import StatsChartCard from '@/components/StatsChartCard.vue'
import type EditableCardGridComponent from '@/components/EditableCardGrid.vue'
import { formatHour, formatNumber } from '@/utils/format'
import { startOfLocalDayDaysAgoIso } from '@/utils/dates'
import {
  averageMovingSpeedKmh,
  averageTripKm as averageTripDistanceKm,
  batteryVoltageChange,
  hasSpeedReadings,
  historyByDay,
  parkingSpots,
  peakDriveHour as peakTripHour,
  totalDistanceKm,
} from '@/utils/statistics'
import { useUnits } from '@/composables/useUnits'
import { useErrorMessage } from '@/composables/useErrorMessage'
import { useStatisticsCharts } from '@/composables/useStatisticsCharts'

// Only edit mode rearranges the cards; the drag-and-drop library loads when it opens. The cast
// restores the grid's generic item type, which defineAsyncComponent does not carry over.
const EditableCardGrid = defineAsyncComponent(
  () => import('@/components/EditableCardGrid.vue'),
) as unknown as typeof EditableCardGridComponent

const { t } = useI18n()
const store = useVehicleStore()
const settings = useDashboardSettingsStore()
const uiSettings = useUiSettingsStore()
const units = useUnits()
const errorMessage = useErrorMessage()

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

// The period's readings by local day, which the voltage trend and every chart are drawn from.
const historyDays = computed(() => historyByDay(store.history, days.value))

// ── Insight computed values ───────────────────────────────────

// Unrounded: the unit formatter rounds once, in the unit it shows.
const periodDistanceKm = computed(() => totalDistanceKm(store.tripSummaries))
const averageTripKm = computed(() => averageTripDistanceKm(store.tripSummaries))

const climateUsagePct = computed(() => aggregateStats.value?.climateUsagePct ?? null)

const peakDriveHour = computed(() => {
  const hour = peakTripHour(store.tripSummaries)
  return hour === null ? null : formatHour(hour)
})

// Shared with the parking-locations modal below - parkingLocations is just this list's count.
const parkingCoordinates = computed(() => parkingSpots(store.tripSummaries))

const parkingLocations = computed(() =>
  store.tripSummaries.length ? parkingCoordinates.value.length : null,
)

const batteryVoltageTrend = computed(() => {
  const delta = batteryVoltageChange(historyDays.value)
  return delta === null ? null : `${delta > 0 ? '+' : ''}${delta} V`
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
    : DEFAULT_MAP_CENTER,
)

function onParkingMapReady(map: LeafletMap) {
  bindParkingMapReady(map, () => {
    fitToPoints(
      map,
      parkingCoordinates.value.map((c) => [c.lat, c.lng] as [number, number]),
      24,
    )
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

// Each insight in the user's order, with what it shows.
const insightRows = computed(() =>
  settings.statsInsights.map((item) => ({ item, def: insightDefMap.value.get(item.id)! })),
)

// ── Charts ────────────────────────────────────────────────

const chartDefs = useStatisticsCharts(historyDays, vehicleType)
const chartDefMap = computed(() => new Map(chartDefs.value.map((d) => [d.id, d])))

// Each chart in the user's order, with what it shows.
const chartRows = computed(() =>
  settings.statsCharts.map((item) => ({ item, def: chartDefMap.value.get(item.id)! })),
)

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
          <DateRangeFilter />
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
        {{ errorMessage(store.historyError) }}
      </div>
      <div v-else-if="!status && !store.history.length && !editMode" class="empty-state">
        {{ t('dashboard.noData') }}
      </div>

      <template v-else>
        <!-- ── Insights ──────────────────────────────────── -->
        <section class="stats-insights" :aria-label="t('statistics.insightsSectionLabel')">
          <!-- Edit mode: draggable card slots -->
          <EditableCardGrid
            v-if="editMode"
            v-model="settings.statsInsights"
            class="status-grid status-grid--edit"
            :is-shown="(item) => insightDefMap.get(item.id)?.vehicleApplicable !== false"
            @toggle-visible="(item) => (item.visible = !item.visible)"
          >
            <template #default="{ item }">
              <StatusCard
                v-if="
                  insightDefMap.get(item.id)?.applicable &&
                  insightDefMap.get(item.id)?.value !== null
                "
                :icon="insightDefMap.get(item.id)!.icon"
                :label="insightDefMap.get(item.id)!.title"
                :value="insightDefMap.get(item.id)!.value"
                :unit="insightDefMap.get(item.id)!.unit"
              />
              <div v-else class="card-slot__placeholder">
                <font-awesome-icon :icon="insightDefMap.get(item.id)?.icon ?? 'circle-info'" />
                <span>{{ insightDefMap.get(item.id)?.title }}</span>
              </div>
            </template>
          </EditableCardGrid>

          <!-- Normal mode: visible + applicable insights -->
          <div v-else class="status-grid">
            <template v-for="{ item, def } in insightRows" :key="item.id">
              <CardInfoWrap
                v-if="item.visible && def.applicable"
                :title="def.title"
                :description="def.description"
              >
                <StatusCard
                  :icon="def.icon"
                  :label="def.title"
                  :value="def.value"
                  :unit="def.unit"
                  :clickable="def.clickable === true"
                  @click="onInsightClick(item.id)"
                />
              </CardInfoWrap>
            </template>
          </div>
        </section>

        <!-- ── Charts ───────────────────────────────────── -->
        <template v-if="editMode || store.history.length">
          <div v-if="store.historyError" class="empty-state text-danger mt-2">
            {{ errorMessage(store.historyError) }}
          </div>

          <!-- Edit mode: draggable chart slots -->
          <EditableCardGrid
            v-if="editMode"
            v-model="settings.statsCharts"
            class="stats-chart-grid"
            slot-class="card-slot--chart"
            :is-shown="(item) => chartDefMap.get(item.id)?.vehicleApplicable !== false"
            @toggle-visible="(item) => (item.visible = !item.visible)"
          >
            <template #default="{ item }">
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
            </template>
          </EditableCardGrid>

          <!-- Normal mode: visible + applicable charts -->
          <div v-else class="stats-chart-grid">
            <template v-for="{ item, def } in chartRows" :key="item.id">
              <StatsChartCard
                v-if="item.visible && def.applicable"
                :title="def.title"
                :type="def.type"
                :data="def.data"
                :options="def.options"
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
