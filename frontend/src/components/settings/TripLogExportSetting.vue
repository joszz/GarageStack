<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useVehicleStore } from '@/stores/vehicle'
import { useErrorMessage } from '@/composables/useErrorMessage'
import { useTripLogExport } from '@/composables/useTripLogExport'
import { currentMonthPeriod, type TripLogPeriod } from '@/utils/tripLog'
import SettingsToggle from '../SettingsToggle.vue'
import TripLogPeriodFields from '../TripLogPeriodFields.vue'

const { t } = useI18n()
const errorMessage = useErrorMessage()
const vehicleStore = useVehicleStore()
const { busy, remaining, empty, error, download } = useTripLogExport()

const period = ref<TripLogPeriod>(currentMonthPeriod())

function save() {
  const vin = vehicleStore.activeVin
  if (vin) void download(vin, period.value)
}
</script>

<template>
  <SettingsToggle
    class="settings-toggle--select settings-toggle--action"
    :label="t('settings.tripLogExport.title')"
    :desc="t('settings.tripLogExport.desc')"
  >
    <template #note>
      <p v-if="remaining !== null" class="text-sm text-muted" role="status">
        {{ t('settings.tripLogExport.lookingUp', { n: remaining }, remaining) }}
      </p>
      <p v-else-if="empty" class="text-sm text-muted" role="status">
        {{ t('settings.tripLogExport.empty') }}
      </p>
      <p v-if="error" class="text-danger text-sm" role="alert">{{ errorMessage(error) }}</p>
    </template>
    <template #control>
      <div class="trip-log-export">
        <TripLogPeriodFields v-model="period" />
        <button
          type="button"
          class="btn btn-outline-secondary"
          :disabled="busy || !vehicleStore.activeVin"
          @click="save"
        >
          <font-awesome-icon :icon="busy ? 'spinner' : 'file-csv'" :spin="busy" />
          {{ t('settings.tripLogExport.download') }}
        </button>
      </div>
    </template>
  </SettingsToggle>
</template>
