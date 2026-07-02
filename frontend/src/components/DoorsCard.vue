<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import ExpandableStatusCard from './ExpandableStatusCard.vue'
import DetailListItem from './DetailListItem.vue'
import { useVehicleCommand } from '@/composables/useVehicleCommand'

const { t } = useI18n()

const props = defineProps<{
  vin: string | null
  isLocked: boolean | null
  driverDoorOpen: boolean | null
  passengerDoorOpen: boolean | null
  rearLeftDoorOpen: boolean | null
  rearRightDoorOpen: boolean | null
  bonnetOpen: boolean | null
  trunkOpen: boolean | null
}>()

const { sending, lastResult, isPending, send } = useVehicleCommand()

const localLocked = ref<boolean | null>(null)
const effectiveLocked = computed(() => localLocked.value ?? props.isLocked)

watch(
  () => props.isLocked,
  () => {
    localLocked.value = null
  },
)

type DoorItem = { key: string; label: string; icon: string; open: boolean }

const doorList = computed((): DoorItem[] =>
  (
    [
      {
        key: 'driver',
        label: t('vehicle.doors_detail.driver'),
        icon: 'door-open',
        open: props.driverDoorOpen,
      },
      {
        key: 'passenger',
        label: t('vehicle.doors_detail.passenger'),
        icon: 'door-open',
        open: props.passengerDoorOpen,
      },
      {
        key: 'rearLeft',
        label: t('vehicle.doors_detail.rearLeft'),
        icon: 'door-open',
        open: props.rearLeftDoorOpen,
      },
      {
        key: 'rearRight',
        label: t('vehicle.doors_detail.rearRight'),
        icon: 'door-open',
        open: props.rearRightDoorOpen,
      },
      {
        key: 'bonnet',
        label: t('vehicle.doors_detail.bonnet'),
        icon: 'car-burst',
        open: props.bonnetOpen,
      },
      {
        key: 'boot',
        label: t('vehicle.doors_detail.boot'),
        icon: 'car-rear',
        open: props.trunkOpen,
      },
    ] as { key: string; label: string; icon: string; open: boolean | null }[]
  ).filter((d): d is DoorItem => d.open !== null),
)

const openDoors = computed(() => doorList.value.filter((d) => d.open))

const summary = computed((): string | null => {
  if (effectiveLocked.value === null && doorList.value.length === 0) return null
  if (openDoors.value.length > 0) return t('common.open')
  if (effectiveLocked.value === true) return t('vehicle.locked')
  if (doorList.value.length > 0) return t('common.closed')
  return effectiveLocked.value !== null
    ? effectiveLocked.value
      ? t('vehicle.locked')
      : t('vehicle.unlocked')
    : null
})

const variant = computed(() => {
  if (openDoors.value.length > 0) return 'danger' as const
  if (effectiveLocked.value === true) return 'success' as const
  if (doorList.value.length > 0) return 'warning' as const
  return undefined
})

async function handleLockToggle() {
  if (isPending('lock')) return
  const newLocked = !effectiveLocked.value
  await send(props.vin, 'lock', newLocked ? 'True' : 'False', (s) => s.isLocked === newLocked)
  if (lastResult.value?.ok) localLocked.value = newLocked
}
</script>

<template>
  <ExpandableStatusCard
    v-if="summary !== null"
    :icon="effectiveLocked === false ? 'lock-open' : 'lock'"
    :title="t('vehicle.doors')"
    :value="summary"
    :variant="variant"
  >
    <div v-if="isLocked !== null" class="detail-list">
      <div class="detail-list__item detail-list__item--control">
        <font-awesome-icon
          :icon="effectiveLocked ? 'lock' : 'lock-open'"
          class="detail-list__item-icon"
        />
        <span class="detail-list__item-label">{{ t('control.lockDoors') }}</span>
        <button
          class="btn btn-sm"
          :class="
            isPending('lock')
              ? 'btn--pending btn-outline-secondary'
              : effectiveLocked
                ? 'btn-outline-warning'
                : 'btn-success'
          "
          :disabled="sending === 'lock' || isPending('lock') || !vin"
          @click="handleLockToggle"
        >
          <font-awesome-icon v-if="sending === 'lock'" icon="spinner" spin />
          <font-awesome-icon v-else-if="isPending('lock')" icon="clock" />
          <font-awesome-icon v-else :icon="effectiveLocked ? 'lock-open' : 'lock'" />
          {{
            isPending('lock')
              ? t('control.pending')
              : effectiveLocked
                ? t('control.unlock')
                : t('control.lock')
          }}
        </button>
      </div>
      <div
        v-if="lastResult?.key === 'lock' && !lastResult.ok && !isPending('lock')"
        class="detail-list__feedback text-danger"
      >
        {{ t('control.error') }}
      </div>
    </div>

    <div v-if="doorList.length > 0" class="detail-list" :class="isLocked !== null ? 'mt-3' : ''">
      <DetailListItem
        v-for="door in doorList"
        :key="door.key"
        :icon="door.icon"
        :value="door.open ? t('common.open') : t('common.closed')"
        :label="door.label"
        :alert="door.open"
      />
    </div>
  </ExpandableStatusCard>
</template>
