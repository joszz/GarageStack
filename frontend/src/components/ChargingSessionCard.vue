<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import ExpandableStatusCard from './ExpandableStatusCard.vue'
import DetailListItem from './DetailListItem.vue'
import { formatDateTime, formatNumber, formatTimeOfDay } from '@/utils/format'
import { gatewayLabel } from '@/utils/gatewayLabel'

const i18n = useI18n()
const { t } = i18n

const props = defineProps<{
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
}>()

const obcPower = computed(() => props.obcPowerThreePhase ?? props.obcPowerSinglePhase)

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
        v-if="chargingCableLock !== null"
        :icon="chargingCableLock ? 'lock' : 'lock-open'"
        :label="t('vehicle.chargingSession.cableLock')"
      >
        <template #value>
          <span class="badge" :class="chargingCableLock ? 'badge-success' : 'badge-warning'">
            {{
              chargingCableLock
                ? t('vehicle.chargingSession.locked')
                : t('vehicle.chargingSession.unlocked')
            }}
          </span>
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
  </ExpandableStatusCard>
</template>
