<script setup lang="ts">
import { ref, watch, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import DetailModal from './DetailModal.vue'
import { useMaintenanceStore } from '@/stores/maintenance'
import type { MaintenanceItem } from '@/services/maintenanceApi'

const props = defineProps<{
  open: boolean
  vin: string
  item: MaintenanceItem | null
}>()

const emit = defineEmits<{ (e: 'close'): void }>()

const { t } = useI18n()
const store = useMaintenanceStore()

const name = ref('')
const notes = ref('')
const intervalKm = ref<number | null>(null)
const intervalMonths = ref<number | null>(null)
const lastServiceDate = ref('')
const lastServiceOdometerKm = ref<number | null>(null)
const validationError = ref<string | null>(null)
const saving = ref(false)

const isEdit = computed(() => props.item !== null)
const title = computed(() => (isEdit.value ? t('maintenance.editItem') : t('maintenance.addItem')))

watch(
  () => props.open,
  (isOpen) => {
    if (!isOpen) return
    validationError.value = null
    const item = props.item
    name.value = item?.name ?? ''
    notes.value = item?.notes ?? ''
    intervalKm.value = item?.intervalKm ?? null
    intervalMonths.value = item?.intervalMonths ?? null
    lastServiceDate.value = ''
    lastServiceOdometerKm.value = null
  },
)

function close() {
  if (saving.value) return
  emit('close')
}

async function submit() {
  validationError.value = null
  if (!name.value.trim()) {
    validationError.value = t('maintenance.form.validationNameRequired')
    return
  }
  if (intervalKm.value == null && intervalMonths.value == null) {
    validationError.value = t('maintenance.form.validationIntervalRequired')
    return
  }

  saving.value = true
  try {
    if (isEdit.value && props.item) {
      await store.updateItem(props.vin, props.item.id, {
        name: name.value.trim(),
        notes: notes.value.trim() || null,
        intervalKm: intervalKm.value,
        intervalMonths: intervalMonths.value,
      })
    } else {
      await store.createItem(props.vin, {
        name: name.value.trim(),
        notes: notes.value.trim() || null,
        intervalKm: intervalKm.value,
        intervalMonths: intervalMonths.value,
        lastServiceDate: lastServiceDate.value || null,
        lastServiceOdometerKm: lastServiceOdometerKm.value,
      })
    }
    if (store.actionError) {
      validationError.value = store.actionError
      return
    }
    emit('close')
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <DetailModal :open="open" :title="title" @close="close">
    <form class="maintenance-form" @submit.prevent="submit">
      <div class="maintenance-field-group">
        <label for="maintenance-name">{{ t('maintenance.form.name') }}</label>
        <input
          id="maintenance-name"
          v-model="name"
          type="text"
          class="maintenance-field"
          :placeholder="t('maintenance.form.namePlaceholder')"
          maxlength="200"
        />
      </div>

      <div class="maintenance-field-group">
        <label for="maintenance-notes">{{ t('maintenance.form.notes') }}</label>
        <textarea
          id="maintenance-notes"
          v-model="notes"
          class="maintenance-field maintenance-field--textarea"
          rows="2"
          maxlength="1000"
        />
      </div>

      <div class="maintenance-field-row">
        <div class="maintenance-field-group">
          <label for="maintenance-interval-km">{{ t('maintenance.form.intervalKm') }}</label>
          <input
            id="maintenance-interval-km"
            v-model.number="intervalKm"
            type="number"
            min="1"
            max="1000000"
            class="maintenance-field"
          />
        </div>
        <div class="maintenance-field-group">
          <label for="maintenance-interval-months">{{
            t('maintenance.form.intervalMonths')
          }}</label>
          <input
            id="maintenance-interval-months"
            v-model.number="intervalMonths"
            type="number"
            min="1"
            max="120"
            class="maintenance-field"
          />
        </div>
      </div>
      <p class="text-muted text-xs">{{ t('maintenance.form.intervalHint') }}</p>

      <template v-if="!isEdit">
        <div class="maintenance-field-row">
          <div class="maintenance-field-group">
            <label for="maintenance-last-date">{{ t('maintenance.form.lastServiceDate') }}</label>
            <input
              id="maintenance-last-date"
              v-model="lastServiceDate"
              type="date"
              class="maintenance-field"
            />
          </div>
          <div class="maintenance-field-group">
            <label for="maintenance-last-odo">{{
              t('maintenance.form.lastServiceOdometer')
            }}</label>
            <input
              id="maintenance-last-odo"
              v-model.number="lastServiceOdometerKm"
              type="number"
              min="0"
              class="maintenance-field"
            />
          </div>
        </div>
        <p class="text-muted text-xs">{{ t('maintenance.form.lastServiceHint') }}</p>
      </template>

      <p v-if="validationError" class="text-danger text-sm">{{ validationError }}</p>
    </form>

    <template #footer>
      <button class="btn btn-outline-secondary" :disabled="saving" @click="close">
        {{ t('common.cancel') }}
      </button>
      <button class="btn btn-primary" :disabled="saving" @click="submit">
        {{ t('maintenance.form.save') }}
      </button>
    </template>
  </DetailModal>
</template>
