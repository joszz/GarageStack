<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import ExpandableStatusCard from './ExpandableStatusCard.vue'
import CommandButton from './CommandButton.vue'
import CommandFailure from './CommandFailure.vue'
import { useVehicleCommand } from '@/composables/useVehicleCommand'

const { t } = useI18n()

const props = defineProps<{
  vin: string | null
}>()

const { sending, lastResult, isPending, send } = useVehicleCommand()
const active = ref(false)

// No telemetry reports the horn, so the card shows the request as soon as the API takes it and
// puts it back if the car refuses: the gateway's answer is the only word on whether it sounded.
async function request(target: boolean) {
  const previous = active.value
  const sent = await send(props.vin, 'find-my-car', target ? 'activate' : 'stop', {
    onRejected: () => {
      active.value = previous
    },
  })
  if (sent) active.value = target
}
</script>

<template>
  <ExpandableStatusCard
    icon="car-burst"
    :title="t('control.findMyCar')"
    :value="active ? t('control.findMyCarActive') : '-'"
    :variant="active ? 'warning' : undefined"
  >
    <p class="card-info-desc">{{ t('control.findMyCarConfirm') }}</p>
    <template #footer="{ close }">
      <CommandFailure
        v-if="lastResult && !lastResult.ok && !isPending('find-my-car')"
        class="me-auto"
        :detail="lastResult.detail"
      />
      <button class="btn btn-outline-secondary" @click="close">
        {{ t('common.cancel') }}
      </button>
      <CommandButton
        v-if="!active"
        class="btn-warning"
        :pending="isPending('find-my-car')"
        :sending="sending === 'find-my-car'"
        :disabled="!vin"
        icon="bullhorn"
        :label="t('control.findMyCarActivate')"
        @click="request(true)"
      />
      <CommandButton
        v-else
        class="btn-danger"
        :pending="isPending('find-my-car')"
        :sending="sending === 'find-my-car'"
        icon="xmark"
        :label="t('control.findMyCarStop')"
        @click="request(false)"
      />
    </template>
  </ExpandableStatusCard>
</template>
