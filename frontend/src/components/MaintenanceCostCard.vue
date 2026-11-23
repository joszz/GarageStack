<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { MaintenanceCost } from '@/services/maintenanceApi'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { formatCurrency } from '@/utils/currency'
import { costTotals } from '@/utils/maintenance'
import StatusCard from './StatusCard.vue'

const props = defineProps<{
  /** Every service of the car that has a cost. */
  costs: readonly MaintenanceCost[]
}>()

const { t } = useI18n()
const settings = useUiSettingsStore()

const totals = computed(() => costTotals(props.costs))
const money = (amount: number) => formatCurrency(amount, settings.effectiveCurrency)
</script>

<template>
  <!-- Only once a service has a cost: a history without any shows no row of zeroes. -->
  <StatusCard
    v-if="totals"
    icon="coins"
    :label="t('maintenance.costs.thisYear')"
    :value="money(totals.thisYear)"
    :note="
      t('maintenance.costs.note', {
        lastYear: money(totals.lastYear),
        allTime: money(totals.allTime),
      })
    "
    :subtitle="t('maintenance.costs.hint')"
  />
</template>
