<script setup lang="ts">
import { computed, ref, toRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import DetailModal from './DetailModal.vue'
import DetailListItem from './DetailListItem.vue'
import StatsChartCard from './StatsChartCard.vue'
import StatusBadge from './StatusBadge.vue'
import { vehicleApi, type DailyStat } from '@/services/vehicleApi'
import { useVehicleStore } from '@/stores/vehicle'
import {
  useLevelChart,
  useStatisticsCharts,
  type LevelKind,
} from '@/composables/useStatisticsCharts'
import { useLevelDetails } from '@/composables/useLevelDetails'
import { useAsyncRequest } from '@/composables/useAsyncRequest'
import { browserTimeZone, localDateKey } from '@/utils/dates'
import { energyUnit } from '@/utils/energy'
import { bucketDays } from '@/utils/statistics'
import type { StatsGrouping } from '@/utils/statsPeriod'

// The tank's or the battery's level now and over the last month, opened from the car diagram,
// with what the trip computer counted used per day where its counter measures this level.
const props = defineProps<{ open: boolean; level: LevelKind }>()
const emit = defineEmits<{ close: [] }>()

const HISTORY_DAYS = 30

const { t } = useI18n()
const vehicleStore = useVehicleStore()
const level = toRef(props, 'level')

const days = ref<DailyStat[] | null>(null)
const { loading, error: loadError, run } = useAsyncRequest()

watch(
  () => props.open,
  (open) => {
    const vin = vehicleStore.activeVin
    if (!open || !vin) return
    const today = new Date()
    const first = new Date(
      today.getFullYear(),
      today.getMonth(),
      today.getDate() - HISTORY_DAYS + 1,
    )
    void run(
      () => vehicleApi.dailyStats(vin, localDateKey(first), localDateKey(today), browserTimeZone()),
      (result) => (days.value = result),
    )
  },
)

const details = useLevelDetails(level)

const grouping = ref<StatsGrouping>('day')
const buckets = computed(() => bucketDays(days.value ?? [], grouping.value))
const vehicleType = computed(() => vehicleStore.effectiveVehicleType)
const levelChart = useLevelChart(buckets, grouping, level)

// The daily counter holds fuel on a plain hybrid and the traction battery's energy on a car that
// plugs in (utils/energy), so it belongs with whichever of the two levels it counts.
const charts = useStatisticsCharts(buckets, grouping, vehicleType)
const usageChart = computed(() => {
  const chart = charts.value.find((c) => c.id === 'dailyKwhChart')!
  const countsFuel = energyUnit(vehicleType.value) === 'litres'
  const countsThisLevel = countsFuel === (level.value === 'fuel')
  const hasUsage = days.value?.some((d) => d.counterTotal !== null) ?? false
  return chart.vehicleApplicable && countsThisLevel && hasUsage ? chart : null
})
</script>

<template>
  <DetailModal :open="open" :title="t(`levelHistory.${level}.title`)" wide @close="emit('close')">
    <section v-if="details.length" class="detail-modal__section">
      <div class="detail-modal__section-title">{{ t('levelHistory.nowTitle') }}</div>
      <div class="detail-list">
        <DetailListItem
          v-for="row in details"
          :key="row.key"
          :icon="row.icon"
          :label="row.label"
          :value="row.value"
        >
          <template v-if="row.badge" #value>
            <StatusBadge :variant="row.badge">{{ row.value }}</StatusBadge>
          </template>
        </DetailListItem>
      </div>
    </section>

    <!-- The month loads under what the car reports now, which is there at once. -->
    <p v-if="loading" class="detail-modal__section text-muted text-sm" role="status">
      <font-awesome-icon icon="spinner" spin />
      {{ t('common.loading') }}
    </p>
    <p v-else-if="loadError" class="detail-modal__section text-danger text-sm" role="alert">
      {{ loadError }}
    </p>
    <template v-else-if="days">
      <section class="detail-modal__section form-stack">
        <p class="text-muted text-sm">
          {{ t(`levelHistory.${level}.chartDesc`, { days: HISTORY_DAYS }) }}
        </p>
        <StatsChartCard
          v-if="levelChart.hasReadings"
          :title="levelChart.title"
          :icon="levelChart.icon"
          :type="levelChart.type"
          :data="levelChart.data"
          :options="levelChart.options"
          :show-info="false"
        />
        <p v-else class="text-muted text-sm">
          {{ t(`levelHistory.${level}.noData`, { days: HISTORY_DAYS }) }}
        </p>
      </section>

      <section v-if="usageChart" class="detail-modal__section form-stack">
        <p class="text-muted text-sm">{{ t(`levelHistory.${level}.usageDesc`) }}</p>
        <StatsChartCard
          :title="usageChart.title"
          :icon="usageChart.icon"
          :type="usageChart.type"
          :data="usageChart.data"
          :options="usageChart.options"
          :show-info="false"
        />
      </section>
    </template>
  </DetailModal>
</template>
