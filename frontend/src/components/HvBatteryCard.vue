<script setup lang="ts">
import { ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import StatusCard from './StatusCard.vue'
import DetailModal from './DetailModal.vue'
import { useVehicleCommand } from '@/composables/useVehicleCommand'

const { t } = useI18n()

const props = defineProps<{
  vin: string | null
  hvSocKwh: number | null
  hvTotalCapacityKwh: number | null
  hvVoltage: number | null
  hvCurrent: number | null
  hvPower: number | null
  hvBatteryActive: boolean | null
  chargerConnected: boolean | null
  powerUsageSinceLastCharge: number | null
  canSetChargeLimit: boolean
}>()

const modalOpen = ref(false)
const { sending, lastResult, isPending, send } = useVehicleCommand()

const socPercent = computed(() => {
  if (props.hvSocKwh === null || props.hvTotalCapacityKwh === null || props.hvTotalCapacityKwh === 0)
    return null
  return Math.round((props.hvSocKwh / props.hvTotalCapacityKwh) * 100)
})

const summaryValue = computed((): string | null => {
  const parts: string[] = []
  if (socPercent.value !== null) parts.push(`${socPercent.value}%`)
  else if (props.hvSocKwh !== null) parts.push(`${props.hvSocKwh.toFixed(1)} kWh`)
  if (props.hvBatteryActive !== null)
    parts.push(props.hvBatteryActive ? t('vehicle.hvBattery.active') : t('vehicle.hvBattery.idle'))
  return parts.length ? parts.join(' · ') : null
})

const summaryVariant = computed(() => {
  if (socPercent.value === null) return undefined
  if (socPercent.value < 20) return 'danger' as const
  if (socPercent.value < 50) return 'warning' as const
  return 'success' as const
})

const hasAnyData = computed(() =>
  props.hvSocKwh !== null || props.hvVoltage !== null || props.hvPower !== null,
)

function setChargeLimit(value: string) {
  send(props.vin, 'charge-limit', value)
}
</script>

<template>
  <StatusCard
    v-if="hasAnyData"
    icon="bolt"
    :label="t('vehicle.hvBattery.title')"
    :value="summaryValue"
    :variant="summaryVariant"
    clickable
    @click="modalOpen = true"
  />

  <DetailModal
    :open="modalOpen"
    :title="t('vehicle.hvBattery.title')"
    @close="modalOpen = false"
  >
    <div class="detail-list">
      <div v-if="hvSocKwh !== null" class="detail-list__item">
        <font-awesome-icon icon="bolt" class="detail-list__item-icon" />
        <span class="detail-list__item-value">{{ hvSocKwh.toFixed(1) }} kWh</span>
        <span class="detail-list__item-sep">-</span>
        <span class="detail-list__item-label">{{ t('vehicle.hvBattery.socKwh') }}</span>
      </div>
      <div v-if="hvTotalCapacityKwh !== null" class="detail-list__item">
        <font-awesome-icon icon="database" class="detail-list__item-icon" />
        <span class="detail-list__item-value">{{ hvTotalCapacityKwh.toFixed(1) }} kWh</span>
        <span class="detail-list__item-sep">-</span>
        <span class="detail-list__item-label">{{ t('vehicle.hvBattery.capacity') }}</span>
      </div>
      <div v-if="socPercent !== null" class="detail-list__item">
        <font-awesome-icon icon="percent" class="detail-list__item-icon" />
        <span class="detail-list__item-value">{{ socPercent }}%</span>
        <span class="detail-list__item-sep">-</span>
        <span class="detail-list__item-label">{{ t('vehicle.hvBattery.soc') }}</span>
      </div>
      <div v-if="hvVoltage !== null" class="detail-list__item">
        <font-awesome-icon icon="plug" class="detail-list__item-icon" />
        <span class="detail-list__item-value">{{ hvVoltage.toFixed(0) }} V</span>
        <span class="detail-list__item-sep">-</span>
        <span class="detail-list__item-label">{{ t('vehicle.hvBattery.voltage') }}</span>
      </div>
      <div v-if="hvCurrent !== null" class="detail-list__item">
        <font-awesome-icon icon="wave-square" class="detail-list__item-icon" />
        <span class="detail-list__item-value">{{ hvCurrent.toFixed(1) }} A</span>
        <span class="detail-list__item-sep">-</span>
        <span class="detail-list__item-label">{{ t('vehicle.hvBattery.current') }}</span>
      </div>
      <div v-if="hvPower !== null" class="detail-list__item">
        <font-awesome-icon icon="bolt-lightning" class="detail-list__item-icon" />
        <span class="detail-list__item-value">{{ hvPower.toFixed(1) }} kW</span>
        <span class="detail-list__item-sep">-</span>
        <span class="detail-list__item-label">{{ t('vehicle.hvBattery.power') }}</span>
      </div>
      <div v-if="powerUsageSinceLastCharge !== null" class="detail-list__item">
        <font-awesome-icon icon="chart-line" class="detail-list__item-icon" />
        <span class="detail-list__item-value">{{ powerUsageSinceLastCharge.toFixed(1) }} kWh</span>
        <span class="detail-list__item-sep">-</span>
        <span class="detail-list__item-label">{{ t('vehicle.hvBattery.usedSinceCharge') }}</span>
      </div>
      <div v-if="chargerConnected !== null" class="detail-list__item">
        <font-awesome-icon icon="plug-circle-check" class="detail-list__item-icon" />
        <span class="badge" :class="chargerConnected ? 'badge-info' : 'badge-secondary'">
          {{ chargerConnected ? t('vehicle.hvBattery.pluggedIn') : t('vehicle.hvBattery.unplugged') }}
        </span>
        <span class="detail-list__item-sep">-</span>
        <span class="detail-list__item-label">{{ t('vehicle.hvBattery.charger') }}</span>
      </div>
    </div>

    <!-- Charge current limit — PHEV/BEV only -->
    <div v-if="canSetChargeLimit" class="detail-modal__section">
      <div class="detail-modal__section-title">{{ t('control.chargeLimit') }}</div>
      <div class="modal-btn-group">
        <button class="btn btn-outline-secondary" :class="isPending('charge-limit') ? 'btn--pending' : ''" :disabled="sending === 'charge-limit' || isPending('charge-limit')" @click="setChargeLimit('6A')">6 A</button>
        <button class="btn btn-outline-secondary" :class="isPending('charge-limit') ? 'btn--pending' : ''" :disabled="sending === 'charge-limit' || isPending('charge-limit')" @click="setChargeLimit('8A')">8 A</button>
        <button class="btn btn-outline-secondary" :class="isPending('charge-limit') ? 'btn--pending' : ''" :disabled="sending === 'charge-limit' || isPending('charge-limit')" @click="setChargeLimit('16A')">16 A</button>
        <button class="btn btn-success" :class="isPending('charge-limit') ? 'btn--pending' : ''" :disabled="sending === 'charge-limit' || isPending('charge-limit')" @click="setChargeLimit('Max')">Max</button>
      </div>
      <div v-if="isPending('charge-limit')" class="detail-list__feedback text-info">
        <font-awesome-icon icon="clock" />
        {{ t('control.pending') }}
      </div>
      <div v-else-if="lastResult?.key === 'charge-limit' && !lastResult.ok" class="detail-list__feedback text-danger">
        {{ t('control.error') }}
      </div>
    </div>
  </DetailModal>
</template>
