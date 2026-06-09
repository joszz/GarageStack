<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import StatusCard from './StatusCard.vue'
import DetailModal from './DetailModal.vue'
import { useVehicleCommand } from '@/composables/useVehicleCommand'
import { useModal } from '@/composables/useModal'

const { t } = useI18n()

const props = defineProps<{
  vin: string | null
}>()

const { sending, isPending, send } = useVehicleCommand()
const active = ref(false)
const { isOpen: modalOpen, open: openModal, close: closeModal } = useModal()

async function activate() {
  if (await send(props.vin, 'find-my-car', 'activate')) active.value = true
}

async function stop() {
  if (await send(props.vin, 'find-my-car', 'stop')) active.value = false
}
</script>

<template>
  <StatusCard
    icon="car-burst"
    :label="t('control.findMyCar')"
    :value="active ? t('control.findMyCarActive') : '-'"
    :variant="active ? 'warning' : undefined"
    clickable
    @click="openModal"
  />

  <DetailModal :open="modalOpen" :title="t('control.findMyCar')" @close="closeModal">
    <p class="card-info-desc">{{ t('control.findMyCarConfirm') }}</p>
    <template #footer>
      <button class="btn btn-outline-secondary" @click="closeModal">
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
  </DetailModal>
</template>
