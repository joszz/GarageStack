<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import DetailModal from './DetailModal.vue'
import { useMaintenanceStore } from '@/stores/maintenance'
import { useVehicleStore } from '@/stores/vehicle'
import type { MaintenanceItem } from '@/services/maintenanceApi'
import { formatIntervalSummary } from '@/utils/maintenance'
import { localDateKey } from '@/utils/dates'
import { formatDate } from '@/utils/format'
import { ODOMETER_FORMAT } from '@/utils/units'
import { useUnits } from '@/composables/useUnits'
import { useMeasureField } from '@/composables/useMeasureField'
import { useErrorMessage } from '@/composables/useErrorMessage'

const props = defineProps<{
  open: boolean
  vin: string
  item: MaintenanceItem | null
}>()

const emit = defineEmits<{ (e: 'close'): void; (e: 'edit', item: MaintenanceItem): void }>()

const { t } = useI18n()
const store = useMaintenanceStore()
const vehicleStore = useVehicleStore()
const units = useUnits()
const errorMessage = useErrorMessage()

const performedAt = ref('')
const odometer = useMeasureField(units, 'distance')
const logNotes = ref('')
const logSaving = ref(false)
const logError = ref<string | null>(null)
const historyError = ref<string | null>(null)
const pendingDelete = ref(false)
const deleting = ref(false)
const deleteError = ref<string | null>(null)

const currentItem = computed(() => store.items.find((i) => i.id === props.item?.id) ?? props.item)
const logEntries = computed(() => (props.item ? (store.logEntries[props.item.id] ?? []) : []))

watch(
  () => props.open,
  (isOpen) => {
    if (!isOpen || !props.item) return
    pendingDelete.value = false
    deleteError.value = null
    logError.value = null
    historyError.value = null
    performedAt.value = localDateKey(new Date())
    odometer.load(vehicleStore.currentStatus?.odometerKm ?? null)
    logNotes.value = ''
    store.fetchLog(props.vin, props.item.id)
  },
)

async function submitLog() {
  if (!props.item) return
  logError.value = null
  logSaving.value = true
  try {
    await store.logService(props.vin, props.item.id, {
      performedAt: performedAt.value,
      odometerKm: odometer.metric(),
      notes: logNotes.value.trim() || null,
    })
    if (store.actionError) {
      logError.value = errorMessage(store.actionError)
      return
    }
    logNotes.value = ''
  } finally {
    logSaving.value = false
  }
}

async function removeLogEntry(logId: number) {
  if (!props.item) return
  historyError.value = null
  await store.deleteLogEntry(props.vin, props.item.id, logId)
  if (store.actionError) historyError.value = errorMessage(store.actionError)
}

function requestDelete() {
  deleteError.value = null
  pendingDelete.value = true
}

function cancelDelete() {
  pendingDelete.value = false
}

// The modal only closes once the item is really gone; a refused delete stays on screen.
async function confirmDelete() {
  if (!props.item) return
  deleting.value = true
  try {
    await store.deleteItem(props.vin, props.item.id)
  } finally {
    deleting.value = false
  }
  pendingDelete.value = false
  if (store.actionError) {
    deleteError.value = errorMessage(store.actionError)
    return
  }
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
    <div v-if="currentItem">
      <section class="detail-modal__section maintenance-detail">
        <div class="maintenance-detail__summary">
          <span
            class="badge"
            :class="`badge-${currentItem.dueStatus === 'dueSoon' ? 'warning' : currentItem.dueStatus === 'overdue' ? 'danger' : currentItem.dueStatus === 'ok' ? 'success' : 'secondary'}`"
          >
            {{ t(`maintenance.status.${currentItem.dueStatus}`) }}
          </span>
          <span class="text-muted text-sm">{{ formatIntervalSummary(currentItem, t, units) }}</span>
        </div>

        <p v-if="currentItem.nextDueOdometerKm != null" class="text-sm">
          {{ t('maintenance.nextDueOdometer') }}:
          {{ units.format('distance', currentItem.nextDueOdometerKm, ODOMETER_FORMAT) }}
        </p>
        <p v-if="currentItem.nextDueDate" class="text-sm">
          {{ t('maintenance.nextDueDate') }}: {{ formatDate(currentItem.nextDueDate) }}
        </p>
        <p v-if="currentItem.notes" class="text-muted text-sm">{{ currentItem.notes }}</p>
      </section>

      <section class="detail-modal__section">
        <h4 class="detail-modal__section-title">{{ t('maintenance.logService') }}</h4>
        <form class="form-stack maintenance-log-form" @submit.prevent="submitLog">
          <div class="form-row">
            <div class="form-group">
              <label for="log-performed-at">{{ t('maintenance.logForm.performedAt') }}</label>
              <input id="log-performed-at" v-model="performedAt" type="date" class="form-control" />
            </div>
            <div class="form-group">
              <label for="log-odometer">{{
                t('maintenance.logForm.odometer', { unit: units.symbol('distance') })
              }}</label>
              <input
                id="log-odometer"
                v-model.number="odometer.shown"
                type="number"
                min="0"
                class="form-control"
              />
            </div>
          </div>
          <div class="form-group">
            <label for="log-notes">{{ t('maintenance.logForm.notes') }}</label>
            <input
              id="log-notes"
              v-model="logNotes"
              type="text"
              class="form-control"
              maxlength="1000"
            />
          </div>
          <p v-if="logError" class="text-danger text-sm">{{ logError }}</p>
          <button type="submit" class="btn btn-primary btn-sm" :disabled="logSaving">
            {{ t('maintenance.logForm.submit') }}
          </button>
        </form>
      </section>

      <section class="detail-modal__section">
        <h4 class="detail-modal__section-title">{{ t('maintenance.history') }}</h4>
        <p v-if="historyError" class="text-danger text-sm">{{ historyError }}</p>
        <p v-if="logEntries.length === 0" class="text-muted text-sm">
          {{ t('maintenance.noHistory') }}
        </p>
        <ul v-else class="maintenance-history">
          <li v-for="entry in logEntries" :key="entry.id" class="maintenance-history__row">
            <span>{{ formatDate(entry.performedAt) }}</span>
            <span v-if="entry.odometerKm != null" class="text-muted">
              {{ units.format('distance', entry.odometerKm, ODOMETER_FORMAT) }}
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
      </section>
    </div>

    <template #footer>
      <template v-if="pendingDelete">
        <span class="text-danger text-sm">{{ t('maintenance.deleteConfirm') }}</span>
        <button class="btn btn-outline-secondary" @click="cancelDelete">
          {{ t('common.cancel') }}
        </button>
        <button class="btn btn-danger" :disabled="deleting" @click="confirmDelete">
          {{ t('common.confirm') }}
        </button>
      </template>
      <template v-else>
        <span v-if="deleteError" class="text-danger text-sm">{{ deleteError }}</span>
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
