<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import ExpandableStatusCard from './ExpandableStatusCard.vue'
import DetailListItem from './DetailListItem.vue'
import CommandButton from './CommandButton.vue'
import CommandFeedback from './CommandFeedback.vue'
import ChargingScheduleForm from './ChargingScheduleForm.vue'
import { useVehicleCommand } from '@/composables/useVehicleCommand'
import { formatDateTime, formatNumber, formatTimeOfDay } from '@/utils/format'
import { gatewayLabel } from '@/utils/gatewayLabel'

const i18n = useI18n()
const { t } = i18n

const props = defineProps<{
  vin: string | null
  isCharging: boolean | null
  chargerConnected: boolean | null
  chargingType: string | null
  chargingCableLock: boolean | null
  obcPowerSinglePhase: number | null
  obcPowerThreePhase: number | null
  remainingChargingTime: number | null
  bmsChargeStatus: string | null
  lastChargeEndingPower: number | null
  chargingLastStartAt: string | null
  chargingLastEndAt: string | null
  chargingScheduleMode: string | null
  chargingScheduleStartTime: string | null
  chargingScheduleEndTime: string | null
  /** Whether the car takes a charge target, and so can charge until it. */
  canChargeUntilTarget: boolean
}>()

const obcPower = computed(() => props.obcPowerThreePhase ?? props.obcPowerSinglePhase)

const { sending, lastResult, isPending, send } = useVehicleCommand()

// Each is taken as done once the car reports the change, not on the gateway's word alone.
function setCharging(start: boolean) {
  send(props.vin, 'charging', String(start), { isConfirmed: (s) => s.isCharging === start })
}

function setCableLock(lock: boolean) {
  send(props.vin, 'charging-cable-lock', String(lock), {
    isConfirmed: (s) => s.chargingCableLock === lock,
  })
}

const summaryValue = computed((): string | null => {
  const parts: string[] = []
  if (obcPower.value !== null) parts.push(`${formatNumber(obcPower.value)} kW`)
  if (props.remainingChargingTime !== null)
    parts.push(`${props.remainingChargingTime} ${t('common.min')}`)
  if (parts.length) return parts.join(' · ')
  if (props.chargingType) return props.chargingType
  if (props.chargingCableLock !== null)
    return props.chargingCableLock
      ? t('vehicle.chargingSession.locked')
      : t('vehicle.chargingSession.unlocked')
  return null
})

const hasAnyData = computed(
  () =>
    obcPower.value !== null ||
    props.isCharging !== null ||
    props.chargingType !== null ||
    props.chargingCableLock !== null ||
    props.remainingChargingTime !== null ||
    props.bmsChargeStatus !== null,
)

function formatStamp(stamp: string | null): string | null {
  if (!stamp) return null
  try {
    return formatDateTime(stamp)
  } catch {
    return null
  }
}

const lastStartFormatted = computed(() => formatStamp(props.chargingLastStartAt))
const lastEndFormatted = computed(() => formatStamp(props.chargingLastEndAt))

const bmsStatusLabel = computed(() =>
  props.bmsChargeStatus
    ? gatewayLabel(i18n, 'vehicle.chargingSession.bmsStatuses', props.bmsChargeStatus)
    : null,
)
const scheduleActive = computed(
  () => !!props.chargingScheduleMode && props.chargingScheduleMode !== 'DISABLED',
)

// Its own row, as on the battery heating card: next to the mode, the times crowd a phone's row.
const scheduleWindow = computed((): string | null => {
  if (!scheduleActive.value || !props.chargingScheduleStartTime) return null
  const start = formatTimeOfDay(props.chargingScheduleStartTime)
  return props.chargingScheduleEndTime
    ? `${start} - ${formatTimeOfDay(props.chargingScheduleEndTime)}`
    : start
})

const scheduleModeLabel = computed(() =>
  props.chargingScheduleMode
    ? gatewayLabel(i18n, 'vehicle.chargingSession.scheduleModes', props.chargingScheduleMode)
    : null,
)
</script>

<template>
  <ExpandableStatusCard
    v-if="hasAnyData"
    icon="plug-circle-bolt"
    :title="t('vehicle.chargingSession.title')"
    :value="summaryValue"
    variant="info"
  >
    <div class="detail-list">
      <div v-if="isCharging !== null" class="detail-list__item detail-list__item--control">
        <font-awesome-icon icon="bolt" class="detail-list__item-icon" />
        <span class="detail-list__item-label">{{ t('control.charging') }}</span>
        <CommandButton
          class="btn-sm"
          :class="isCharging ? 'btn-outline-secondary' : 'btn-success'"
          :pending="isPending('charging')"
          :sending="sending === 'charging'"
          :disabled="!vin || (!isCharging && chargerConnected === false)"
          :icon="isCharging ? 'power-off' : 'bolt'"
          :label="isCharging ? t('control.stopCharging') : t('control.startCharging')"
          @click="setCharging(!isCharging)"
        />
      </div>
      <div
        v-if="isCharging === false && chargerConnected === false && !isPending('charging')"
        class="detail-list__feedback text-muted"
      >
        {{ t('control.plugInFirst') }}
      </div>
      <CommandFeedback
        command="charging"
        :pending="isPending('charging')"
        :last-result="lastResult"
      />
      <div v-if="chargingCableLock !== null" class="detail-list__item detail-list__item--control">
        <font-awesome-icon
          :icon="chargingCableLock ? 'lock' : 'lock-open'"
          class="detail-list__item-icon"
        />
        <span class="detail-list__item-label">{{ t('vehicle.chargingSession.cableLock') }}</span>
        <CommandButton
          class="btn-sm"
          :class="chargingCableLock ? 'btn-outline-warning' : 'btn-success'"
          :pending="isPending('charging-cable-lock')"
          :sending="sending === 'charging-cable-lock'"
          :disabled="!vin"
          :icon="chargingCableLock ? 'lock-open' : 'lock'"
          :label="chargingCableLock ? t('control.unlock') : t('control.lock')"
          :confirm-label="chargingCableLock ? t('control.confirmUnlockCable') : undefined"
          @click="setCableLock(!chargingCableLock)"
        />
      </div>
      <CommandFeedback
        command="charging-cable-lock"
        :pending="isPending('charging-cable-lock')"
        :last-result="lastResult"
      />
    </div>

    <div class="detail-list mt-3">
      <DetailListItem
        v-if="obcPower !== null"
        icon="bolt-lightning"
        :value="`${formatNumber(obcPower)} kW`"
        :label="t('vehicle.chargingSession.power')"
      />
      <DetailListItem
        v-if="obcPowerThreePhase !== null"
        icon="plug"
        :value="`${formatNumber(obcPowerThreePhase)} kW`"
        :label="t('vehicle.chargingSession.power3phase')"
      />
      <DetailListItem
        v-if="obcPowerSinglePhase !== null"
        icon="plug"
        :value="`${formatNumber(obcPowerSinglePhase)} kW`"
        :label="t('vehicle.chargingSession.power1phase')"
      />
      <DetailListItem
        v-if="remainingChargingTime !== null"
        icon="clock"
        :value="`${remainingChargingTime} ${t('common.min')}`"
        :label="t('vehicle.remainingCharge')"
      />
      <DetailListItem
        v-if="chargingType"
        icon="charging-station"
        :label="t('vehicle.chargingSession.type')"
      >
        <template #value>
          <span class="badge badge-info">{{ chargingType }}</span>
        </template>
      </DetailListItem>
      <DetailListItem
        v-if="bmsChargeStatus"
        icon="battery-half"
        :label="t('vehicle.chargingSession.bmsStatus')"
      >
        <template #value>
          <span class="badge badge-secondary">{{ bmsStatusLabel }}</span>
        </template>
      </DetailListItem>
      <DetailListItem
        v-if="lastChargeEndingPower !== null"
        icon="percent"
        :value="`${formatNumber(lastChargeEndingPower)}%`"
        :label="t('vehicle.chargingSession.lastEndSoc')"
      />
      <DetailListItem
        v-if="lastStartFormatted"
        icon="calendar-days"
        :value="lastStartFormatted"
        :label="t('vehicle.chargingSession.lastStart')"
      />
      <DetailListItem
        v-if="lastEndFormatted"
        icon="calendar-check"
        :value="lastEndFormatted"
        :label="t('vehicle.chargingSession.lastEnd')"
      />
      <DetailListItem
        v-if="scheduleActive"
        icon="sliders"
        :label="t('vehicle.chargingSession.schedule')"
      >
        <template #value>
          <span class="badge badge-info">{{ scheduleModeLabel }}</span>
        </template>
      </DetailListItem>
      <DetailListItem
        v-if="scheduleWindow"
        icon="clock"
        :value="scheduleWindow"
        :label="t('vehicle.chargingSession.scheduleWindow')"
      />
    </div>

    <ChargingScheduleForm
      v-if="vin"
      class="mt-3"
      :vin="vin"
      :mode="chargingScheduleMode"
      :start-time="chargingScheduleStartTime"
      :end-time="chargingScheduleEndTime"
      :can-charge-until-target="canChargeUntilTarget"
    />
  </ExpandableStatusCard>
</template>
