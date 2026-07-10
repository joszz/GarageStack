<script setup lang="ts">
import { computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useVehicleStore } from '@/stores/vehicle'
import { useMaintenanceStore } from '@/stores/maintenance'
import StatusCard from './StatusCard.vue'

const { t } = useI18n()
const router = useRouter()
const vehicleStore = useVehicleStore()
const store = useMaintenanceStore()

const vin = computed(() => vehicleStore.vehicles[0]?.vin ?? null)

watch(
  vin,
  (v) => {
    if (v) store.fetchItems(v)
  },
  { immediate: true },
)

const variant = computed((): 'success' | 'warning' | 'danger' => {
  if (store.overdueItems.length > 0) return 'danger'
  if (store.dueSoonItems.length > 0) return 'warning'
  return 'success'
})

const value = computed(() =>
  store.attentionItems.length === 0
    ? t('maintenance.status.ok')
    : String(store.attentionItems.length),
)

const subtitle = computed(() => {
  if (store.attentionItems.length === 0) return t('maintenance.dashboard.allOk')
  const names = store.attentionItems.slice(0, 2).map((i) => i.name)
  const extra = store.attentionItems.length - names.length
  return extra > 0
    ? `${names.join(', ')} ${t('maintenance.dashboard.moreCount', { count: extra })}`
    : names.join(', ')
})

function goToPage() {
  router.push({ name: 'maintenance' })
}
</script>

<template>
  <StatusCard
    icon="screwdriver-wrench"
    :label="t('maintenance.title')"
    :value="value"
    :subtitle="subtitle"
    :variant="variant"
    clickable
    @click="goToPage"
  />
</template>
