<script setup lang="ts">
import { computed } from 'vue'
import { isSamePeriod, type Period, type PeriodOptions } from '@/utils/period'
import ToolbarPanel from './ToolbarPanel.vue'
import PeriodFilter from './PeriodFilter.vue'

// The filters button of a page whose only filter is its period (the statistics, the charge
// history). The map has more filters, so it puts PeriodFilter in a panel of its own.
const period = defineModel<Period>({ required: true })
const props = defineProps<{ options: PeriodOptions; desc: string }>()

// The badge on the button: the period counts once it is not the one the page starts on.
const activeCount = computed(() => (isSamePeriod(period.value, props.options.fallback) ? 0 : 1))
</script>

<template>
  <ToolbarPanel :count="activeCount">
    <PeriodFilter v-model="period" :options="options" :desc="desc" />
  </ToolbarPanel>
</template>
