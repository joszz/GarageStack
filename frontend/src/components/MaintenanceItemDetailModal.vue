<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import DetailModal from './DetailModal.vue'
import { useMaintenanceStore } from '@/stores/maintenance'
import { useVehicleStore } from '@/stores/vehicle'
import type { MaintenanceItem } from '@/services/maintenanceApi'

const props = defineProps<{
  open: boolean
  vin: string
  item: MaintenanceItem | null
}>()

const emit = defineEmits<{ (e: 'close'): void; (e: 'edit', item: MaintenanceItem): void }>()

const { t } = useI18n()
const store = useMaintenanceStore()
const vehicleStore = useVehicleStore()

const performedAt = ref('')
const odometerKm = ref<number | null>(null)
const logNotes = ref('')
const logSaving = ref(false)
const logError = ref<string | null>(null)
const pendingDelete = ref(false)

const currentItem = computed(() => store.items.find((i) => i.id === props.item?.id) ?? props.item)
const logEntries = computed(() => (props.item ? (store.logEntries[props.item.id] ?? []) : []))

watch(
  () => props.open,
  (isOpen) => {
    if (!isOpen || !props.item) return
    pendingDelete.value = false
    logError.value = null
    performedAt.value = new Date().toISOString().slice(0, 10)
    odometerKm.value = vehicleStore.currentStatus?.odometerKm ?? null
    logNotes.value = ''
    store.fetchLog(props.vin, props.item.id)
  },
)

function intervalSummary(item: MaintenanceItem): string {
  const parts: string[] = []
  if (item.intervalKm != null)
    parts.push(t('maintenance.everyKm', { km: item.intervalKm.toLocaleString() }))
  if (item.intervalMonths != null)
    parts.push(t('maintenance.everyMonths', { months: item.intervalMonths }))
  return parts.join(` ${t('maintenance.or')} `)
}

function formatDate(value: string) {
  return new Date(value).toLocaleDateString()
}

async function submitLog() {
  if (!props.item) return
  logError.value = null
  logSaving.value = true
  try {
    await store.logService(props.vin, props.item.id, {
      performedAt: performedAt.value,
      odometerKm: odometerKm.value,
      notes: logNotes.value.trim() || null,
    })
    if (store.actionError) {
      logError.value = store.actionError
      return
    }
    logNotes.value = ''
  } finally {
    logSaving.value = false
  }
}

async function removeLogEntry(logId: number) {
  if (!props.item) return
  await store.deleteLogEntry(props.vin, props.item.id, logId)
}

function requestDelete() {
  pendingDelete.value = true
}

function cancelDelete() {
  pendingDelete.value = false
}

async function confirmDelete() {
  if (!props.item) return
  await store.deleteItem(props.vin, props.item.id)
  pendingDelete.value = false
  emit('close')
}
</script>

<template>
  <DetailModal
    :open="open && item !== null"
    :title="currentItem?.name ?? ''"
    wide
    @close="emit('close')"
  >
    <div v-if="currentItem" class="maintenance-detail">
      <div class="maintenance-detail__summary">
        <span
          class="badge"
          :class="`badge-${currentItem.dueStatus === 'dueSoon' ? 'warning' : currentItem.dueStatus === 'overdue' ? 'danger' : currentItem.dueStatus === 'ok' ? 'success' : 'secondary'}`"
        >
          {{ t(`maintenance.status.${currentItem.dueStatus}`) }}
        </span>
        <span class="text-muted text-sm">{{ intervalSummary(currentItem) }}</span>
      </div>

      <p v-if="currentItem.nextDueOdometerKm != null" class="text-sm">
        {{ t('maintenance.nextDueOdometer') }}:
        {{ Math.round(currentItem.nextDueOdometerKm).toLocaleString() }} {{ t('common.km') }}
      </p>
      <p v-if="currentItem.nextDueDate" class="text-sm">
        {{ t('maintenance.nextDueDate') }}: {{ formatDate(currentItem.nextDueDate) }}
      </p>
      <p v-if="currentItem.notes" class="text-muted text-sm">{{ currentItem.notes }}</p>

      <h4 class="maintenance-detail__heading">{{ t('maintenance.logService') }}</h4>
      <form class="maintenance-form maintenance-log-form" @submit.prevent="submitLog">
        <div class="maintenance-field-row">
          <div class="maintenance-field-group">
            <label for="log-performed-at">{{ t('maintenance.logForm.performedAt') }}</label>
            <input
              id="log-performed-at"
              v-model="performedAt"
              type="date"
              class="maintenance-field"
            />
          </div>
          <div class="maintenance-field-group">
            <label for="log-odometer">{{ t('maintenance.logForm.odometer') }}</label>
            <input
              id="log-odometer"
              v-model.number="odometerKm"
              type="number"
              min="0"
              class="maintenance-field"
            />
          </div>
        </div>
        <div class="maintenance-field-group">
          <label for="log-notes">{{ t('maintenance.logForm.notes') }}</label>
          <input
            id="log-notes"
            v-model="logNotes"
            type="text"
            class="maintenance-field"
            maxlength="1000"
          />
        </div>
        <p v-if="logError" class="text-danger text-sm">{{ logError }}</p>
        <button type="submit" class="btn btn-primary btn-sm" :disabled="logSaving">
          {{ t('maintenance.logForm.submit') }}
        </button>
      </form>

      <h4 class="maintenance-detail__heading">{{ t('maintenance.history') }}</h4>
      <p v-if="logEntries.length === 0" class="text-muted text-sm">
        {{ t('maintenance.noHistory') }}
      </p>
      <ul v-else class="maintenance-history">
        <li v-for="entry in logEntries" :key="entry.id" class="maintenance-history__row">
          <span>{{ formatDate(entry.performedAt) }}</span>
          <span v-if="entry.odometerKm != null" class="text-muted">
            {{ Math.round(entry.odometerKm).toLocaleString() }} {{ t('common.km') }}
          </span>
          <span v-if="entry.notes" class="text-muted">{{ entry.notes }}</span>
          <button
            type="button"
            class="btn btn-sm btn-outline-secondary maintenance-history__delete"
            :aria-label="t('maintenance.deleteLogEntry')"
            @click="removeLogEntry(entry.id)"
          >
            <font-awesome-icon icon="trash" />
          </button>
        </li>
      </ul>
    </div>

    <template #footer>
      <template v-if="pendingDelete">
        <span class="text-danger text-sm">{{ t('maintenance.deleteConfirm') }}</span>
        <button class="btn btn-outline-secondary" @click="cancelDelete">
          {{ t('common.cancel') }}
        </button>
        <button class="btn btn-danger" @click="confirmDelete">{{ t('common.confirm') }}</button>
      </template>
      <template v-else>
        <button class="btn btn-outline-secondary" @click="requestDelete">
          <font-awesome-icon icon="trash" />{{ t('maintenance.delete') }}
        </button>
        <button v-if="currentItem" class="btn btn-primary" @click="emit('edit', currentItem)">
          <font-awesome-icon icon="pen-to-square" />{{ t('maintenance.editItem') }}
        </button>
      </template>
    </template>
  </DetailModal>
</template>
