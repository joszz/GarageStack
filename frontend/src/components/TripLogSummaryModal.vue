<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { PeriodSummary } from '@/utils/tripLog'
import { formatDurationMs } from '@/utils/tripRows'
import type { Measure } from '@/utils/units'
import { useUnits } from '@/composables/useUnits'
import DetailModal from './DetailModal.vue'

const props = defineProps<{
  open: boolean
  /** The period's visible trips, summed up. */
  summary: PeriodSummary
}>()

const emit = defineEmits<{ (e: 'close'): void }>()

const { t } = useI18n()
const units = useUnits()

function measured(m: Measure | null) {
  return { value: m?.value ?? null, unit: m?.unit ?? '' }
}

// Each figure with what it means spelled out under it, as the dialog has the room the cards had
// not.
const figures = computed(() => {
  const s = props.summary
  return [
    {
      key: 'drivingTime',
      icon: 'clock',
      value: s.trips > 0 ? formatDurationMs(s.drivingMs, t) : null,
      unit: '',
    },
    {
      key: 'averageTrip',
      icon: 'route',
      ...measured(units.value.measure('distance', s.averageTripKm)),
    },
    {
      key: 'longestTrip',
      icon: 'flag-checkered',
      ...measured(units.value.measure('distance', s.longestTripKm)),
    },
    {
      key: 'averageSpeed',
      icon: 'gauge',
      ...measured(units.value.measure('speed', s.averageSpeedKmh, { decimals: 0 })),
    },
  ].map((figure) => ({
    ...figure,
    label: t(`tripLog.summary.${figure.key}`),
    description: t(`tripLog.cardDesc.${figure.key}`),
  }))
})
</script>

<template>
  <DetailModal :open="open" :title="t('tripLog.summary.label')" @close="emit('close')">
    <dl class="trip-log-summary">
      <div v-for="figure in figures" :key="figure.key" class="trip-log-summary__item">
        <dt class="trip-log-summary__label">
          <font-awesome-icon :icon="figure.icon" class="trip-log-summary__icon" />{{ figure.label }}
        </dt>
        <dd class="trip-log-summary__value">
          {{ figure.value ?? '-'
          }}<span v-if="figure.unit && figure.value !== null" class="trip-log-summary__unit">
            {{ figure.unit }}</span
          >
        </dd>
        <dd class="trip-log-summary__description text-sm text-muted">{{ figure.description }}</dd>
      </div>
    </dl>
  </DetailModal>
</template>
