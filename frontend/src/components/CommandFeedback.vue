<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import type { CommandOutcome } from '@/composables/useVehicleCommand'
import CommandFailure from './CommandFailure.vue'

// The line under a command's controls: waiting for the car while it is pending, otherwise why the
// last try of this command failed, if it did.
defineProps<{
  command: string
  pending: boolean
  lastResult: CommandOutcome | null
}>()

const { t } = useI18n()
</script>

<template>
  <div v-if="pending" class="detail-list__feedback text-info">
    <font-awesome-icon icon="clock" />
    {{ t('control.pending') }}
  </div>
  <CommandFailure
    v-else-if="lastResult?.key === command && !lastResult.ok"
    class="detail-list__feedback"
    :detail="lastResult.detail"
  />
</template>
