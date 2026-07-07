<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import ExpandableStatusCard from './ExpandableStatusCard.vue'
import CommandButton from './CommandButton.vue'
import { useVehicleCommand } from '@/composables/useVehicleCommand'

const { t } = useI18n()

const props = defineProps<{
  vin: string | null
}>()

const { sending, isPending, send } = useVehicleCommand()
const active = ref(false)

async function activate() {
  if (await send(props.vin, 'find-my-car', 'activate')) active.value = true
}

async function stop() {
  if (await send(props.vin, 'find-my-car', 'stop')) active.value = false
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
        @click="activate"
      />
      <CommandButton
        v-else
        class="btn-danger"
        :pending="isPending('find-my-car')"
        :sending="sending === 'find-my-car'"
        icon="xmark"
        :label="t('control.findMyCarStop')"
        @click="stop"
      />
    </template>
  </ExpandableStatusCard>
</template>
