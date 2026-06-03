<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import StatusCard from './StatusCard.vue'
import DetailModal from './DetailModal.vue'
import { useModal } from '@/composables/useModal'

const { t } = useI18n()

const props = defineProps<{
  chargingType: string | null
  chargingCableLock: boolean | null
  obcPowerSinglePhase: number | null
  obcPowerThreePhase: number | null
  remainingChargingTime: number | null
}>()

const { isOpen: modalOpen, open: openModal, close: closeModal } = useModal()

const obcPower = computed(() => props.obcPowerThreePhase ?? props.obcPowerSinglePhase)

const summaryValue = computed((): string | null => {
  const parts: string[] = []
  if (obcPower.value !== null) parts.push(`${obcPower.value.toFixed(1)} kW`)
  if (props.remainingChargingTime !== null) parts.push(`${props.remainingChargingTime} ${t('common.min')}`)
  if (parts.length) return parts.join(' · ')
  if (props.chargingType) return props.chargingType
  if (props.chargingCableLock !== null)
    return props.chargingCableLock ? t('vehicle.chargingSession.locked') : t('vehicle.chargingSession.unlocked')
  return null
})

const hasAnyData = computed(
  () =>
    obcPower.value !== null ||
    props.chargingType !== null ||
    props.chargingCableLock !== null ||
    props.remainingChargingTime !== null,
)
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
      <div v-if="obcPower !== null" class="detail-list__item">
        <font-awesome-icon icon="bolt-lightning" class="detail-list__item-icon" />
        <span class="detail-list__item-value">{{ obcPower.toFixed(1) }} kW</span>
        <span class="detail-list__item-sep">-</span>
        <span class="detail-list__item-label">{{ t('vehicle.chargingSession.power') }}</span>
      </div>
      <div v-if="obcPowerThreePhase !== null" class="detail-list__item">
        <font-awesome-icon icon="plug" class="detail-list__item-icon" />
        <span class="detail-list__item-value">{{ obcPowerThreePhase.toFixed(1) }} kW</span>
        <span class="detail-list__item-sep">-</span>
        <span class="detail-list__item-label">{{ t('vehicle.chargingSession.power3phase') }}</span>
      </div>
      <div v-if="obcPowerSinglePhase !== null" class="detail-list__item">
        <font-awesome-icon icon="plug" class="detail-list__item-icon" />
        <span class="detail-list__item-value">{{ obcPowerSinglePhase.toFixed(1) }} kW</span>
        <span class="detail-list__item-sep">-</span>
        <span class="detail-list__item-label">{{ t('vehicle.chargingSession.power1phase') }}</span>
      </div>
      <div v-if="remainingChargingTime !== null" class="detail-list__item">
        <font-awesome-icon icon="clock" class="detail-list__item-icon" />
        <span class="detail-list__item-value">{{ remainingChargingTime }} {{ t('common.min') }}</span>
        <span class="detail-list__item-sep">-</span>
        <span class="detail-list__item-label">{{ t('vehicle.remainingCharge') }}</span>
      </div>
      <div v-if="chargingType" class="detail-list__item">
        <font-awesome-icon icon="charging-station" class="detail-list__item-icon" />
        <span class="badge badge-info">{{ chargingType }}</span>
        <span class="detail-list__item-sep">-</span>
        <span class="detail-list__item-label">{{ t('vehicle.chargingSession.type') }}</span>
      </div>
      <div v-if="chargingCableLock !== null" class="detail-list__item">
        <font-awesome-icon :icon="chargingCableLock ? 'lock' : 'lock-open'" class="detail-list__item-icon" />
        <span class="badge" :class="chargingCableLock ? 'badge-success' : 'badge-warning'">
          {{ chargingCableLock ? t('vehicle.chargingSession.locked') : t('vehicle.chargingSession.unlocked') }}
        </span>
        <span class="detail-list__item-sep">-</span>
        <span class="detail-list__item-label">{{ t('vehicle.chargingSession.cableLock') }}</span>
      </div>
    </div>
  </DetailModal>
</template>
