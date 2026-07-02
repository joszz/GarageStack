<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import ExpandableStatusCard from './ExpandableStatusCard.vue'
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
      <button
        v-if="!active"
        class="btn btn-warning"
        :class="isPending('find-my-car') ? 'btn--pending' : ''"
        :disabled="sending === 'find-my-car' || isPending('find-my-car') || !vin"
        @click="activate"
      >
        <font-awesome-icon v-if="sending === 'find-my-car'" icon="spinner" spin />
        <font-awesome-icon v-else-if="isPending('find-my-car')" icon="clock" />
        <font-awesome-icon v-else icon="bullhorn" />
        {{ isPending('find-my-car') ? t('control.pending') : t('control.findMyCarActivate') }}
      </button>
      <button
        v-else
        class="btn btn-danger"
        :class="isPending('find-my-car') ? 'btn--pending' : ''"
        :disabled="sending === 'find-my-car' || isPending('find-my-car')"
        @click="stop"
      >
        <font-awesome-icon v-if="sending === 'find-my-car'" icon="spinner" spin />
        <font-awesome-icon v-else-if="isPending('find-my-car')" icon="clock" />
        <font-awesome-icon v-else icon="xmark" />
        {{ isPending('find-my-car') ? t('control.pending') : t('control.findMyCarStop') }}
      </button>
    </template>
  </ExpandableStatusCard>
</template>
