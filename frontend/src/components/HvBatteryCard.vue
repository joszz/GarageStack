<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import ExpandableStatusCard from './ExpandableStatusCard.vue'
import DetailListItem from './DetailListItem.vue'
import CommandButton from './CommandButton.vue'
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

const { sending, lastResult, isPending, send } = useVehicleCommand()

const socPercent = computed(() => {
  if (
    props.hvSocKwh === null ||
    props.hvTotalCapacityKwh === null ||
    props.hvTotalCapacityKwh === 0
  )
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

const hasAnyData = computed(
  () => props.hvSocKwh !== null || props.hvVoltage !== null || props.hvPower !== null,
)

function setChargeLimit(value: string) {
  send(props.vin, 'charge-limit', value)
}
</script>

<template>
  <ExpandableStatusCard
    v-if="hasAnyData"
    icon="bolt"
    :title="t('vehicle.hvBattery.title')"
    :value="summaryValue"
    :variant="summaryVariant"
  >
    <div class="detail-list">
      <DetailListItem
        v-if="hvSocKwh !== null"
        icon="bolt"
        :value="`${hvSocKwh.toFixed(1)} kWh`"
        :label="t('vehicle.hvBattery.socKwh')"
      />
      <DetailListItem
        v-if="hvTotalCapacityKwh !== null"
        icon="database"
        :value="`${hvTotalCapacityKwh.toFixed(1)} kWh`"
        :label="t('vehicle.hvBattery.capacity')"
      />
      <DetailListItem
        v-if="socPercent !== null"
        icon="percent"
        :value="`${socPercent}%`"
        :label="t('vehicle.hvBattery.soc')"
      />
      <DetailListItem
        v-if="hvVoltage !== null"
        icon="plug"
        :value="`${hvVoltage.toFixed(0)} V`"
        :label="t('vehicle.hvBattery.voltage')"
      />
      <DetailListItem
        v-if="hvCurrent !== null"
        icon="wave-square"
        :value="`${hvCurrent.toFixed(1)} A`"
        :label="t('vehicle.hvBattery.current')"
      />
      <DetailListItem
        v-if="hvPower !== null"
        icon="bolt-lightning"
        :value="`${hvPower.toFixed(1)} kW`"
        :label="t('vehicle.hvBattery.power')"
      />
      <DetailListItem
        v-if="powerUsageSinceLastCharge !== null"
        icon="chart-line"
        :value="`${powerUsageSinceLastCharge.toFixed(1)} kWh`"
        :label="t('vehicle.hvBattery.usedSinceCharge')"
      />
      <DetailListItem
        v-if="chargerConnected !== null"
        icon="plug-circle-check"
        :label="t('vehicle.hvBattery.charger')"
      >
        <template #value>
          <span class="badge" :class="chargerConnected ? 'badge-info' : 'badge-secondary'">
            {{
              chargerConnected ? t('vehicle.hvBattery.pluggedIn') : t('vehicle.hvBattery.unplugged')
            }}
          </span>
        </template>
      </DetailListItem>
    </div>

    <!-- Charge current limit - PHEV/BEV only -->
    <div v-if="canSetChargeLimit" class="detail-modal__section">
      <div class="detail-modal__section-title">{{ t('control.chargeLimit') }}</div>
      <div class="modal-btn-group">
        <CommandButton
          class="btn-outline-secondary"
          :pending="isPending('charge-limit')"
          :sending="sending === 'charge-limit'"
          :show-pending-label="false"
          label="6 A"
          @click="setChargeLimit('6A')"
        />
        <CommandButton
          class="btn-outline-secondary"
          :pending="isPending('charge-limit')"
          :sending="sending === 'charge-limit'"
          :show-pending-label="false"
          label="8 A"
          @click="setChargeLimit('8A')"
        />
        <CommandButton
          class="btn-outline-secondary"
          :pending="isPending('charge-limit')"
          :sending="sending === 'charge-limit'"
          :show-pending-label="false"
          label="16 A"
          @click="setChargeLimit('16A')"
        />
        <CommandButton
          class="btn-success"
          :pending="isPending('charge-limit')"
          :sending="sending === 'charge-limit'"
          :show-pending-label="false"
          label="Max"
          @click="setChargeLimit('Max')"
        />
      </div>
      <div v-if="isPending('charge-limit')" class="detail-list__feedback text-info">
        <font-awesome-icon icon="clock" />
        {{ t('control.pending') }}
      </div>
      <div
        v-else-if="lastResult?.key === 'charge-limit' && !lastResult.ok"
        class="detail-list__feedback text-danger"
      >
        {{ t('control.error') }}
      </div>
    </div>
  </ExpandableStatusCard>
</template>
