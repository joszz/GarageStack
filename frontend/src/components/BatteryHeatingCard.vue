<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import ExpandableStatusCard from './ExpandableStatusCard.vue'
import DetailListItem from './DetailListItem.vue'
import CommandButton from './CommandButton.vue'
import CommandFeedback from './CommandFeedback.vue'
import { useVehicleCommand } from '@/composables/useVehicleCommand'
import { formatTimeOfDay } from '@/utils/format'

const { t } = useI18n()

const props = defineProps<{
  vin: string | null
  batteryHeating: boolean | null
  scheduleMode: string | null
  scheduleStartTime: string | null
}>()

const summaryValue = computed((): string | null => {
  if (props.batteryHeating === null) return null
  const state = props.batteryHeating ? t('common.on') : t('common.off')
  if (props.scheduleMode && props.scheduleMode !== 'off' && props.scheduleStartTime)
    return `${state} · ${formatTimeOfDay(props.scheduleStartTime)}`
  return state
})

const { sending, lastResult, isPending, send } = useVehicleCommand()

function setHeating(on: boolean) {
  send(props.vin, 'battery-heating', String(on), { isConfirmed: (s) => s.batteryHeating === on })
}

// The gateway sends the schedule as "on" or "off".
const scheduleModeLabel = computed(() => {
  if (props.scheduleMode === 'on') return t('common.on')
  if (props.scheduleMode === 'off') return t('common.off')
  return props.scheduleMode
})
</script>

<template>
  <ExpandableStatusCard
    v-if="batteryHeating !== null"
    icon="temperature-arrow-up"
    :title="t('vehicle.batteryHeating.title')"
    :value="summaryValue"
    :variant="batteryHeating ? 'info' : undefined"
  >
    <div class="detail-list">
      <div class="detail-list__item detail-list__item--control">
        <font-awesome-icon icon="temperature-arrow-up" class="detail-list__item-icon" />
        <span class="detail-list__item-label">
          {{ t('vehicle.batteryHeating.title') }}
          <span class="badge" :class="batteryHeating ? 'badge-info' : 'badge-secondary'">
            {{ batteryHeating ? t('common.on') : t('common.off') }}
          </span>
        </span>
        <CommandButton
          class="btn-sm"
          :class="batteryHeating ? 'btn-outline-secondary' : 'btn-primary'"
          :pending="isPending('battery-heating')"
          :sending="sending === 'battery-heating'"
          :disabled="!vin"
          :icon="batteryHeating ? 'power-off' : 'temperature-arrow-up'"
          :label="batteryHeating ? t('control.stopHeating') : t('control.startHeating')"
          @click="setHeating(!batteryHeating)"
        />
      </div>
      <CommandFeedback
        command="battery-heating"
        :pending="isPending('battery-heating')"
        :last-result="lastResult"
      />
      <DetailListItem
        v-if="scheduleMode !== null"
        icon="calendar-check"
        :label="t('vehicle.batteryHeating.schedule')"
      >
        <template #value>
          <span class="badge" :class="scheduleMode !== 'off' ? 'badge-info' : 'badge-secondary'">
            {{ scheduleModeLabel }}
          </span>
        </template>
      </DetailListItem>
      <DetailListItem
        v-if="scheduleStartTime !== null"
        icon="clock"
        :value="formatTimeOfDay(scheduleStartTime)"
        :label="t('vehicle.batteryHeating.startTime')"
      />
    </div>
  </ExpandableStatusCard>
</template>
