<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import StatusCard from './StatusCard.vue'
import DetailModal from './DetailModal.vue'
import { useModal } from '@/composables/useModal'

const { t } = useI18n()

const props = defineProps<{
  driverWindowOpen: boolean | null
  passengerWindowOpen: boolean | null
  rearLeftWindowOpen: boolean | null
  rearRightWindowOpen: boolean | null
}>()

const { isOpen: modalOpen, open: openModal, close: closeModal } = useModal()

type WinItem = { key: string; label: string; open: boolean }

const windowList = computed((): WinItem[] =>
  ([
    { key: 'driver',    label: t('vehicle.doors_detail.driver'),    open: props.driverWindowOpen },
    { key: 'passenger', label: t('vehicle.doors_detail.passenger'), open: props.passengerWindowOpen },
    { key: 'rearLeft',  label: t('vehicle.doors_detail.rearLeft'),  open: props.rearLeftWindowOpen },
    { key: 'rearRight', label: t('vehicle.doors_detail.rearRight'), open: props.rearRightWindowOpen },
  ] as { key: string; label: string; open: boolean | null }[])
    .filter((w): w is WinItem => w.open !== null),
)

const openWindows = computed(() => windowList.value.filter(w => w.open))

const summary = computed((): string | null => {
  if (windowList.value.length === 0) return null
  return openWindows.value.length > 0
    ? `${openWindows.value.length} open`
    : t('common.closed')
})

const variant = computed(() => {
  if (windowList.value.length === 0) return undefined
  return openWindows.value.length > 0 ? 'warning' as const : 'success' as const
})

</script>

<template>
  <StatusCard
    v-if="summary !== null"
    icon="car-side"
    :label="t('vehicle.windows')"
    :value="summary"
    :variant="variant"
    clickable
    @click="openModal"
  />

  <DetailModal
    :open="modalOpen"
    :title="t('vehicle.windows')"
    @close="closeModal"
  >
    <div class="detail-list">
      <div
        v-for="win in windowList"
        :key="win.key"
        class="detail-list__item"
        :class="win.open ? 'detail-list__item--alert' : ''"
      >
        <font-awesome-icon icon="car-side" class="detail-list__item-icon" />
        <span class="detail-list__item-value">{{ win.open ? t('common.open') : t('common.closed') }}</span>
        <span class="detail-list__item-sep">-</span>
        <span class="detail-list__item-label">{{ win.label }}</span>
      </div>
    </div>
  </DetailModal>
</template>
