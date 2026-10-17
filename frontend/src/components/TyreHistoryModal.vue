<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import DetailModal from './DetailModal.vue'
import StatsChartCard from './StatsChartCard.vue'
import { vehicleApi, type TyreHistory } from '@/services/vehicleApi'
import { useVehicleStore } from '@/stores/vehicle'
import { useStatisticsCharts } from '@/composables/useStatisticsCharts'
import { useTyrePressureThresholds } from '@/composables/useTyrePressureThresholds'
import { useUnits } from '@/composables/useUnits'
import { useAsyncRequest } from '@/composables/useAsyncRequest'
import { browserTimeZone } from '@/utils/dates'
import { bucketDays } from '@/utils/statistics'
import type { StatsGrouping } from '@/utils/statsPeriod'
import type { TyrePosition } from './carDiagram/useTyreDisplay'

// A month of each tyre's pressure, and how each tyre moved against the other three: what the
// slow-puncture alert looks at, shown where the tyres are.
const props = defineProps<{ open: boolean }>()
const emit = defineEmits<{ close: [] }>()

const { t } = useI18n()
const vehicleStore = useVehicleStore()
const units = useUnits()
const thresholds = useTyrePressureThresholds()

const POSITIONS: TyrePosition[] = ['frontLeft', 'frontRight', 'rearLeft', 'rearRight']

const history = ref<TyreHistory | null>(null)
const { loading, error: loadError, run } = useAsyncRequest()

watch(
  () => props.open,
  (open) => {
    const vin = vehicleStore.activeVin
    if (!open || !vin) return
    void run(
      () => vehicleApi.tyreHistory(vin, browserTimeZone()),
      (result) => (history.value = result),
    )
  },
)

// The statistics page's own tyre chart, over these days.
const grouping = ref<StatsGrouping>('day')
const buckets = computed(() => bucketDays(history.value?.days ?? [], grouping.value))
const charts = useStatisticsCharts(
  buckets,
  grouping,
  computed(() => vehicleStore.effectiveVehicleType),
)
const tyreChart = computed(() => charts.value.find((c) => c.id === 'tyreChart')!)
const hasReadings = computed(
  () =>
    history.value?.days.some(
      (d) =>
        d.tyrePressureFrontLeft !== null ||
        d.tyrePressureFrontRight !== null ||
        d.tyrePressureRearLeft !== null ||
        d.tyrePressureRearRight !== null,
    ) ?? false,
)

// A difference of a few hundredths of a bar is the point here, so one decimal more than psi and
// kPa readings show.
function pressureChange(bar: number): string {
  const decimals = Math.max(units.value.decimals('pressure'), 1)
  const text = units.value.format('pressure', Math.abs(bar), { decimals })!
  return bar < 0 ? `−${text}` : `+${text}`
}

const trendDesc = computed(() =>
  t('tyreHistory.trendDesc', {
    days: history.value?.trendDays ?? 0,
    drop: units.value.format('pressure', thresholds.value.dropBar, {
      decimals: Math.max(units.value.decimals('pressure'), 1),
    }),
  }),
)

const trendRows = computed(() => {
  const trend = history.value?.trend
  if (!trend) return []
  const losing = history.value?.losing ?? []
  return POSITIONS.map((position) => ({
    position,
    change: pressureChange(trend[position]),
    losing: losing.includes(position),
  }))
})
</script>

<template>
  <DetailModal :open="open" :title="t('tyreHistory.title')" wide @close="emit('close')">
    <p v-if="loading" class="text-muted text-sm" role="status">
      <font-awesome-icon icon="spinner" spin />
      {{ t('common.loading') }}
    </p>
    <p v-else-if="loadError" class="text-danger text-sm" role="alert">
      {{ loadError }}
    </p>
    <template v-else-if="history">
      <section class="detail-modal__section form-stack">
        <p class="text-muted text-sm">{{ t('tyreHistory.chartDesc') }}</p>
        <StatsChartCard
          v-if="hasReadings"
          :title="tyreChart.title"
          :icon="tyreChart.icon"
          :type="tyreChart.type"
          :data="tyreChart.data"
          :options="tyreChart.options"
          :show-info="false"
        />
        <p v-else class="text-muted text-sm">{{ t('tyreHistory.noData') }}</p>
      </section>

      <section class="detail-modal__section">
        <div class="detail-modal__section-title">{{ t('tyreHistory.trendTitle') }}</div>
        <div class="form-stack">
          <p class="text-muted text-sm">{{ trendDesc }}</p>
          <div v-if="history.trend" class="detail-list">
            <div
              v-for="row in trendRows"
              :key="row.position"
              class="detail-list__item"
              :class="{ 'detail-list__item--alert': row.losing }"
            >
              <font-awesome-icon
                :icon="row.losing ? 'arrow-trend-down' : 'check'"
                class="detail-list__item-icon"
              />
              <span class="detail-list__item-label">{{
                t(`vehicle.diagram.tyrePosition.${row.position}`)
              }}</span>
              <span class="detail-list__item-value">
                {{ row.change }} ·
                {{ row.losing ? t('tyreHistory.losing') : t('tyreHistory.keepingUp') }}
              </span>
            </div>
          </div>
          <p v-else class="text-muted text-sm">{{ t('tyreHistory.noTrend') }}</p>
        </div>
      </section>
    </template>
  </DetailModal>
</template>
