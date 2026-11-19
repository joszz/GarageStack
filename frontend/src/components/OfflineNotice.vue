<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useVehicleStore } from '@/stores/vehicle'
import { useConnection } from '@/composables/useConnection'
import { formatDateTime } from '@/utils/format'

// Says so whenever the server cannot be reached: what the page shows is then what this browser
// saved, and without the notice an hour-old position or last month's statistics would pass for
// the current ones. Every page header shows it (see ViewHeader).

const { t } = useI18n()
const store = useVehicleStore()
const connection = useConnection()

// The car's own time, not when this browser saved it: that is how old the position really is.
const carSeenAt = computed(() => {
  const status = store.currentStatus
  const at = status?.lastVehicleStateAt ?? status?.recordedAt
  return at ? formatDateTime(at) : null
})

const message = computed(() => {
  // The oldest answer the service worker stood in with on this page.
  if (connection.savedAt !== null) {
    return t('offline.savedPage', { time: formatDateTime(connection.savedAt) })
  }
  // Only the car this browser kept in its own storage (from before the worker saved answers).
  if (store.showingSavedState && carSeenAt.value !== null) {
    return t('offline.savedState', { time: carSeenAt.value })
  }
  return t('offline.savedPageUnknown')
})
</script>

<template>
  <div v-if="!connection.reachable" class="offline-notice" role="status">
    <font-awesome-icon icon="wifi" aria-hidden="true" />
    <span>{{ message }}</span>
  </div>
</template>
