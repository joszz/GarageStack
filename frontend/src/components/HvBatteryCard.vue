<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import ExpandableStatusCard from './ExpandableStatusCard.vue'
import DetailListItem from './DetailListItem.vue'
import CommandButton from './CommandButton.vue'
import CommandFailure from './CommandFailure.vue'
import { useVehicleCommand } from '@/composables/useVehicleCommand'
import { formatNumber } from '@/utils/format'

const { t } = useI18n()

// Charge is taken in percent and kWh rather than derived here: how many kWh a percentage is
// worth depends on the car's real pack size, which the gateway does not report reliably. That
// resolution lives in utils/energy, so this card only presents what it is handed, and is handed
// no kWh at all when the capacity is unknown.
const props = defineProps<{
  vin: string | null
  socPercent: number | null
  storedKwh: number | null
  capacityKwh: number | null
  hvVoltage: number | null
  hvCurrent: number | null
  hvPower: number | null
  hvBatteryActive: boolean | null
  chargerConnected: boolean | null
  powerUsageSinceLastCharge: number | null
  /** The charge level the car stops at; null on a car that does not plug in, or before it reports one. */
  socTargetPercent: number | null
  /** The current the car draws as the gateway spells it (6A, 8A, 16A, Max); null while unknown. */
  chargeCurrentLimit: string | null
  canSetChargeLimit: boolean
}>()

const { sending, lastResult, isPending, send } = useVehicleCommand()

const roundedSocPercent = computed(() =>
  props.socPercent === null ? null : Math.round(props.socPercent),
)

const summaryValue = computed((): string | null => {
  const parts: string[] = []
  if (roundedSocPercent.value !== null) parts.push(`${roundedSocPercent.value}%`)
  else if (props.storedKwh !== null) parts.push(`${formatNumber(props.storedKwh)} kWh`)
  if (props.hvBatteryActive !== null)
    parts.push(props.hvBatteryActive ? t('vehicle.hvBattery.active') : t('vehicle.hvBattery.idle'))
  return parts.length ? parts.join(' · ') : null
})

const summaryVariant = computed(() => {
  if (roundedSocPercent.value === null) return undefined
  if (roundedSocPercent.value < 20) return 'danger' as const
  if (roundedSocPercent.value < 50) return 'warning' as const
  return 'success' as const
})

const hasAnyData = computed(
  () => props.socPercent !== null || props.hvVoltage !== null || props.hvPower !== null,
)

// The values the charge-limit command takes, in the order the buttons show them.
const chargeLimits = computed(() => [
  { value: '6A', label: '6 A' },
  { value: '8A', label: '8 A' },
  { value: '16A', label: '16 A' },
  { value: 'MAX', label: t('control.chargeLimitMax') },
])

// The gateway reports "Max" where the command takes "MAX".
function isActiveLimit(value: string): boolean {
  return props.chargeCurrentLimit?.toUpperCase() === value
}

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
        v-if="storedKwh !== null"
        icon="bolt"
        :value="`${formatNumber(storedKwh)} kWh`"
        :label="t('vehicle.hvBattery.socKwh')"
      />
      <DetailListItem
        v-if="capacityKwh !== null"
        icon="database"
        :value="`${formatNumber(capacityKwh)} kWh`"
        :label="t('vehicle.hvBattery.capacity')"
      />
      <DetailListItem
        v-if="roundedSocPercent !== null"
        icon="percent"
        :value="`${roundedSocPercent}%`"
        :label="t('vehicle.hvBattery.soc')"
      />
      <DetailListItem
        v-if="hvVoltage !== null"
        icon="plug"
        :value="`${formatNumber(hvVoltage, 0)} V`"
        :label="t('vehicle.hvBattery.voltage')"
      />
      <DetailListItem
        v-if="hvCurrent !== null"
        icon="wave-square"
        :value="`${formatNumber(hvCurrent)} A`"
        :label="t('vehicle.hvBattery.current')"
      />
      <DetailListItem
        v-if="hvPower !== null"
        icon="bolt-lightning"
        :value="`${formatNumber(hvPower)} kW`"
        :label="t('vehicle.hvBattery.power')"
      />
      <DetailListItem
        v-if="powerUsageSinceLastCharge !== null"
        icon="chart-line"
        :value="`${formatNumber(powerUsageSinceLastCharge)} kWh`"
        :label="t('vehicle.hvBattery.usedSinceCharge')"
      />
      <DetailListItem
        v-if="socTargetPercent !== null"
        icon="battery-full"
        :value="`${socTargetPercent}%`"
        :label="t('vehicle.hvBattery.chargeTarget')"
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
          v-for="limit in chargeLimits"
          :key="limit.value"
          :class="isActiveLimit(limit.value) ? 'btn-success' : 'btn-outline-secondary'"
          :aria-pressed="isActiveLimit(limit.value)"
          :pending="isPending('charge-limit')"
          :sending="sending === 'charge-limit'"
          :show-pending-label="false"
          :label="limit.label"
          @click="setChargeLimit(limit.value)"
        />
      </div>
      <div v-if="isPending('charge-limit')" class="detail-list__feedback text-info">
        <font-awesome-icon icon="clock" />
        {{ t('control.pending') }}
      </div>
      <CommandFailure
        v-else-if="lastResult?.key === 'charge-limit' && !lastResult.ok"
        class="detail-list__feedback"
        :detail="lastResult.detail"
      />
    </div>
  </ExpandableStatusCard>
</template>
