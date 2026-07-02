<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import ExpandableStatusCard from './ExpandableStatusCard.vue'
import DetailListItem from './DetailListItem.vue'

const { t } = useI18n()

const props = defineProps<{
  driverWindowOpen: boolean | null
  passengerWindowOpen: boolean | null
  rearLeftWindowOpen: boolean | null
  rearRightWindowOpen: boolean | null
}>()

type WinItem = { key: string; label: string; open: boolean }

const windowList = computed((): WinItem[] =>
  (
    [
      { key: 'driver', label: t('vehicle.doors_detail.driver'), open: props.driverWindowOpen },
      {
        key: 'passenger',
        label: t('vehicle.doors_detail.passenger'),
        open: props.passengerWindowOpen,
      },
      {
        key: 'rearLeft',
        label: t('vehicle.doors_detail.rearLeft'),
        open: props.rearLeftWindowOpen,
      },
      {
        key: 'rearRight',
        label: t('vehicle.doors_detail.rearRight'),
        open: props.rearRightWindowOpen,
      },
    ] as { key: string; label: string; open: boolean | null }[]
  ).filter((w): w is WinItem => w.open !== null),
)

const openWindows = computed(() => windowList.value.filter((w) => w.open))

const summary = computed((): string | null => {
  if (windowList.value.length === 0) return null
  return openWindows.value.length > 0 ? `${openWindows.value.length} open` : t('common.closed')
})

const variant = computed(() => {
  if (windowList.value.length === 0) return undefined
  return openWindows.value.length > 0 ? ('warning' as const) : ('success' as const)
})
</script>

<template>
  <ExpandableStatusCard
    v-if="summary !== null"
    icon="car-side"
    :title="t('vehicle.windows')"
    :value="summary"
    :variant="variant"
  >
    <div class="detail-list">
      <DetailListItem
        v-for="win in windowList"
        :key="win.key"
        icon="car-side"
        :value="win.open ? t('common.open') : t('common.closed')"
        :label="win.label"
        :alert="win.open"
      />
    </div>
  </ExpandableStatusCard>
</template>
