<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import DetailModal from './DetailModal.vue'
import { useVehicleCommand } from '@/composables/useVehicleCommand'

const { t } = useI18n()

const props = defineProps<{
  vin: string | null
}>()

const { sending, isPending, send } = useVehicleCommand()
const active = ref(false)
const confirmOpen = ref(false)

function confirm() {
  confirmOpen.value = false
  active.value = true
  send(props.vin, 'find-my-car', 'activate')
}

function stop() {
  active.value = false
  send(props.vin, 'find-my-car', 'stop')
}
</script>

<template>
  <div class="status-card" :class="active ? 'status-card--warning' : ''">
    <div class="status-card__icon">
      <font-awesome-icon icon="car-burst" />
    </div>
    <div class="status-card__body">
      <span class="status-card__label">{{ t('control.findMyCar') }}</span>
      <button
        v-if="!active"
        class="btn btn-warning btn-sm find-my-car__btn"
        :class="isPending('find-my-car') ? 'btn--pending' : ''"
        :disabled="sending === 'find-my-car' || isPending('find-my-car') || !vin"
        @click="confirmOpen = true"
      >
        <font-awesome-icon v-if="sending === 'find-my-car'" icon="spinner" spin />
        <font-awesome-icon v-else-if="isPending('find-my-car')" icon="clock" />
        <font-awesome-icon v-else icon="bullhorn" />
        {{ isPending('find-my-car') ? t('control.pending') : t('control.findMyCarActivate') }}
      </button>
      <button
        v-else
        class="btn btn-danger btn-sm find-my-car__btn"
        :class="isPending('find-my-car') ? 'btn--pending' : ''"
        :disabled="sending === 'find-my-car' || isPending('find-my-car')"
        @click="stop"
      >
        <font-awesome-icon v-if="sending === 'find-my-car'" icon="spinner" spin />
        <font-awesome-icon v-else-if="isPending('find-my-car')" icon="clock" />
        <font-awesome-icon v-else icon="xmark" />
        {{ isPending('find-my-car') ? t('control.pending') : t('control.findMyCarStop') }}
      </button>
    </div>
  </div>

  <DetailModal
    :open="confirmOpen"
    :title="t('control.findMyCar')"
    @close="confirmOpen = false"
  >
    <p>{{ t('control.findMyCarConfirm') }}</p>
    <template #footer>
      <button class="btn btn-outline-secondary" @click="confirmOpen = false">
        {{ t('common.cancel') }}
      </button>
      <button class="btn btn-warning" @click="confirm">
        <font-awesome-icon icon="bullhorn" />
        {{ t('control.findMyCarActivate') }}
      </button>
    </template>
  </DetailModal>
</template>
