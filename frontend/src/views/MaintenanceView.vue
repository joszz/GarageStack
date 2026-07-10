<script setup lang="ts">
import '@/assets/maintenance.css'
import { onMounted, computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useVehicleStore } from '@/stores/vehicle'
import { useMaintenanceStore } from '@/stores/maintenance'
import StatusCard from '@/components/StatusCard.vue'
import MaintenanceItemFormModal from '@/components/MaintenanceItemFormModal.vue'
import MaintenanceItemDetailModal from '@/components/MaintenanceItemDetailModal.vue'
import type { MaintenanceItem, MaintenanceDueStatus } from '@/services/maintenanceApi'

const { t } = useI18n()
const vehicleStore = useVehicleStore()
const store = useMaintenanceStore()

const vin = computed(() => vehicleStore.vehicles[0]?.vin ?? null)

const formOpen = ref(false)
const detailOpen = ref(false)
const editingItem = ref<MaintenanceItem | null>(null)
const selectedItem = ref<MaintenanceItem | null>(null)

async function load() {
  await vehicleStore.fetchVehicles()
  if (vin.value) await store.fetchItems(vin.value)
}

onMounted(load)

const STATUS_ORDER: Record<MaintenanceDueStatus, number> = {
  overdue: 0,
  dueSoon: 1,
  ok: 2,
  unknown: 3,
}

const sortedItems = computed(() =>
  [...store.items].sort((a, b) => {
    const order = STATUS_ORDER[a.dueStatus] - STATUS_ORDER[b.dueStatus]
    return order !== 0 ? order : a.name.localeCompare(b.name)
  }),
)

function statusVariant(status: MaintenanceDueStatus): 'success' | 'warning' | 'danger' | 'info' {
  if (status === 'overdue') return 'danger'
  if (status === 'dueSoon') return 'warning'
  if (status === 'ok') return 'success'
  return 'info'
}

function intervalSummary(item: MaintenanceItem): string {
  const parts: string[] = []
  if (item.intervalKm != null)
    parts.push(t('maintenance.everyKm', { km: item.intervalKm.toLocaleString() }))
  if (item.intervalMonths != null)
    parts.push(t('maintenance.everyMonths', { months: item.intervalMonths }))
  return parts.join(` ${t('maintenance.or')} `)
}

function openAdd() {
  editingItem.value = null
  formOpen.value = true
}

function openDetail(item: MaintenanceItem) {
  selectedItem.value = item
  detailOpen.value = true
}

function openEditFromDetail(item: MaintenanceItem) {
  detailOpen.value = false
  editingItem.value = item
  formOpen.value = true
}

watch(vin, (v) => {
  if (v) store.fetchItems(v)
})
</script>

<template>
  <div class="view-container">
    <div class="view-header">
      <h1>{{ t('maintenance.title') }}</h1>
      <div class="view-header__actions">
        <button class="btn btn-primary btn-sm" @click="openAdd">
          <font-awesome-icon icon="plus" />{{ t('maintenance.addItem') }}
        </button>
      </div>
    </div>

    <p class="text-muted mb-4">{{ t('maintenance.subtitle') }}</p>

    <div v-if="store.itemsError" class="empty-state text-danger">
      {{ store.itemsError }}
    </div>
    <div v-else-if="!store.loading && sortedItems.length === 0" class="empty-state">
      {{ t('maintenance.empty') }}
    </div>
    <div v-else class="maintenance-list">
      <StatusCard
        v-for="item in sortedItems"
        :key="item.id"
        icon="screwdriver-wrench"
        :label="item.name"
        :value="t(`maintenance.status.${item.dueStatus}`)"
        :subtitle="intervalSummary(item)"
        :variant="statusVariant(item.dueStatus)"
        clickable
        @click="openDetail(item)"
      />
    </div>

    <MaintenanceItemFormModal
      v-if="vin"
      :open="formOpen"
      :vin="vin"
      :item="editingItem"
      @close="formOpen = false"
    />
    <MaintenanceItemDetailModal
      v-if="vin"
      :open="detailOpen"
      :vin="vin"
      :item="selectedItem"
      @close="detailOpen = false"
      @edit="openEditFromDetail"
    />
  </div>
</template>
