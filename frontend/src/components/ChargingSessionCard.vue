<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import StatusCard from './StatusCard.vue'
import DetailModal from './DetailModal.vue'
import DetailListItem from './DetailListItem.vue'
import { useModal } from '@/composables/useModal'

const { t } = useI18n()

const props = defineProps<{
  chargingType: string | null
  chargingCableLock: boolean | null
  obcPowerSinglePhase: number | null
  obcPowerThreePhase: number | null
  remainingChargingTime: number | null
  bmsChargeStatus: string | null
  lastChargeEndingPower: number | null
  chargingLastEndAt: string | null
  chargingScheduleMode: string | null
  chargingScheduleStartTime: string | null
  chargingScheduleEndTime: string | null
}>()

const { isOpen: modalOpen, open: openModal, close: closeModal } = useModal()

const obcPower = computed(() => props.obcPowerThreePhase ?? props.obcPowerSinglePhase)

const summaryValue = computed((): string | null => {
  const parts: string[] = []
  if (obcPower.value !== null) parts.push(`${obcPower.value.toFixed(1)} kW`)
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

const lastEndFormatted = computed((): string | null => {
  if (!props.chargingLastEndAt) return null
  try {
    return new Date(props.chargingLastEndAt).toLocaleString()
  } catch {
    return null
  }
})
</script>

<template>
  <StatusCard
    v-if="hasAnyData"
    icon="plug-circle-bolt"
    :label="t('vehicle.chargingSession.title')"
    :value="summaryValue"
    variant="info"
    clickable
    @click="openModal"
  />

  <DetailModal :open="modalOpen" :title="t('vehicle.chargingSession.title')" @close="closeModal">
    <div class="detail-list">
      <DetailListItem
        v-if="obcPower !== null"
        icon="bolt-lightning"
        :value="`${obcPower.toFixed(1)} kW`"
        :label="t('vehicle.chargingSession.power')"
      />
      <DetailListItem
        v-if="obcPowerThreePhase !== null"
        icon="plug"
        :value="`${obcPowerThreePhase.toFixed(1)} kW`"
        :label="t('vehicle.chargingSession.power3phase')"
      />
      <DetailListItem
        v-if="obcPowerSinglePhase !== null"
        icon="plug"
        :value="`${obcPowerSinglePhase.toFixed(1)} kW`"
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
          <span class="badge badge-secondary">{{ bmsChargeStatus }}</span>
        </template>
      </DetailListItem>
      <DetailListItem
        v-if="lastChargeEndingPower !== null"
        icon="percent"
        :value="`${lastChargeEndingPower.toFixed(1)}%`"
        :label="t('vehicle.chargingSession.lastEndSoc')"
      />
      <DetailListItem
        v-if="lastEndFormatted"
        icon="calendar-check"
        :value="lastEndFormatted"
        :label="t('vehicle.chargingSession.lastEnd')"
      />
      <DetailListItem
        v-if="chargingScheduleMode && chargingScheduleMode !== 'DISABLED'"
        icon="clock"
        :label="t('vehicle.chargingSession.schedule')"
      >
        <template #value>
          <span class="badge badge-info">{{ chargingScheduleMode }}</span>
          <template v-if="chargingScheduleStartTime">
            <span class="detail-list__item-value">{{ chargingScheduleStartTime }}</span>
            <template v-if="chargingScheduleEndTime">
              <span class="detail-list__item-sep">-</span>
              <span class="detail-list__item-value">{{ chargingScheduleEndTime }}</span>
            </template>
          </template>
        </template>
      </DetailListItem>
    </div>
  </DetailModal>
</template>
