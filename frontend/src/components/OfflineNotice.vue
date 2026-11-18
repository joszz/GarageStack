<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useVehicleStore } from '@/stores/vehicle'
import { useOnline } from '@/composables/useOnline'
import { formatDateTime } from '@/utils/format'

// Says so when the car on the page is the copy this browser saved, because the server could not be
// reached: without it, an hour-old position would pass for the live one. `reconnect` fires when
// the browser has a connection again, for the page to ask for what it missed.
const emit = defineEmits<{ reconnect: [] }>()

const { t } = useI18n()
const store = useVehicleStore()
const online = useOnline(() => emit('reconnect'))

const shown = computed(
  () =>
    store.showingSavedState &&
    store.currentStatus !== null &&
    (!online.value || store.statusError !== null),
)

// The car's own time, not when this browser saved it: that is how old the position really is.
const seenAt = computed(() => {
  const status = store.currentStatus
  const at = status?.lastVehicleStateAt ?? status?.recordedAt
  return at ? formatDateTime(at) : null
})
</script>

<template>
  <div v-if="shown" class="offline-notice" role="status">
    <font-awesome-icon icon="wifi" aria-hidden="true" />
    <span>
      {{ seenAt ? t('offline.savedState', { time: seenAt }) : t('offline.savedStateUnknown') }}
    </span>
  </div>
</template>
