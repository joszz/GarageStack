<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useVehicleStore } from '@/stores/vehicle'
import { useSettingsStore } from '@/stores/settings'
import type { CardId } from '@/stores/settings'
import type { VehicleType } from '@/stores/vehicle'
import StatusCard from './StatusCard.vue'
import DoorsCard from './DoorsCard.vue'
import WindowsCard from './WindowsCard.vue'
import ClimateDetailCard from './ClimateDetailCard.vue'
import HvBatteryCard from './HvBatteryCard.vue'
import FindMyCarCard from './FindMyCarCard.vue'
import LightsCard from './LightsCard.vue'

defineProps<{ cardId: CardId }>()

const { t } = useI18n()
const store = useVehicleStore()
const settings = useSettingsStore()

const vin = computed(() => store.vehicles[0]?.vin ?? null)
const status = computed(() => store.currentStatus)

const vehicleType = computed((): VehicleType | 'unknown' => {
  const override = settings.vehicleTypeOverride
  if (override !== 'auto') return override as VehicleType
  return store.detectedVehicleType
})

const isHev = computed(() => vehicleType.value === 'hev')
const supportsExternalCharge = computed(
  () => vehicleType.value === 'phev' || vehicleType.value === 'bev',
)
</script>

<template>
  <template v-if="status">
    <!-- fuelLevel -->
    <StatusCard
      v-if="cardId === 'fuelLevel' && status.fuelLevelPercent !== null"
      icon="gas-pump"
      :label="t('vehicle.fuel')"
      :value="Math.round(status.fuelLevelPercent)"
      unit="%"
      :variant="
        status.fuelLevelPercent < 15
          ? 'danger'
          : status.fuelLevelPercent < 30
            ? 'warning'
            : 'success'
      "
    />

    <!-- fuelRange -->
    <StatusCard
      v-else-if="cardId === 'fuelRange' && status.fuelRangeKm !== null"
      icon="road"
      :label="t('vehicle.range')"
      :value="Math.round(status.fuelRangeKm)"
      :unit="t('common.km')"
    />

    <!-- evBattery -->
    <StatusCard
      v-else-if="cardId === 'evBattery' && status.evSocPercent !== null"
      icon="bolt"
      :label="t('vehicle.evSoc')"
      :value="Math.round(status.evSocPercent)"
      unit="%"
      :variant="
        status.evSocPercent < 20 ? 'danger' : status.evSocPercent < 50 ? 'warning' : 'success'
      "
    />

    <!-- charging -->
    <StatusCard
      v-else-if="cardId === 'charging' && status.isCharging !== null"
      icon="plug"
      :label="t('vehicle.charging')"
      :value="status.isCharging ? t('vehicle.chargingYes') : t('vehicle.chargingNo')"
      :variant="status.isCharging ? 'info' : undefined"
    />

    <!-- odometer -->
    <StatusCard
      v-else-if="cardId === 'odometer'"
      icon="gauge"
      :label="t('vehicle.odometer')"
      :value="status.odometerKm !== null ? Math.round(status.odometerKm).toLocaleString() : null"
      :unit="t('common.km')"
    />

    <!-- battery12v -->
    <StatusCard
      v-else-if="cardId === 'battery12v'"
      icon="battery-three-quarters"
      :label="t('vehicle.battery')"
      :value="status.batteryVoltage !== null ? status.batteryVoltage.toFixed(1) : null"
      unit="V"
      :variant="status.batteryVoltage !== null && status.batteryVoltage < 12 ? 'danger' : 'success'"
    />

    <!-- doors -->
    <DoorsCard
      v-else-if="cardId === 'doors'"
      :vin="vin"
      :is-locked="status.isLocked"
      :driver-door-open="status.driverDoorOpen"
      :passenger-door-open="status.passengerDoorOpen"
      :rear-left-door-open="status.rearLeftDoorOpen"
      :rear-right-door-open="status.rearRightDoorOpen"
      :bonnet-open="status.bonnetOpen"
      :trunk-open="status.trunkOpen"
    />

    <!-- windows -->
    <WindowsCard
      v-else-if="cardId === 'windows'"
      :driver-window-open="status.driverWindowOpen"
      :passenger-window-open="status.passengerWindowOpen"
      :rear-left-window-open="status.rearLeftWindowOpen"
      :rear-right-window-open="status.rearRightWindowOpen"
    />

    <!-- sunRoof -->
    <StatusCard
      v-else-if="cardId === 'sunRoof' && status.sunRoofOpen !== null"
      icon="sun"
      :label="t('settings.cards.sunRoof')"
      :value="status.sunRoofOpen ? t('common.open') : t('common.closed')"
      :variant="status.sunRoofOpen ? 'warning' : 'success'"
    />

    <!-- climate -->
    <ClimateDetailCard
      v-else-if="cardId === 'climate'"
      :vin="vin"
      :climate-on="status.climateOn"
      :remote-temperature="status.remoteTemperature"
      :interior-temperature="status.interiorTemperature"
      :exterior-temperature="status.exteriorTemperature"
      :heated-seat-front-left="status.heatedSeatFrontLeft"
      :heated-seat-front-right="status.heatedSeatFrontRight"
      :rear-window-defroster="status.rearWindowDefroster"
    />

    <!-- hvBattery -->
    <HvBatteryCard
      v-else-if="cardId === 'hvBattery'"
      :vin="vin"
      :hv-soc-kwh="status.hvSocKwh"
      :hv-total-capacity-kwh="status.hvTotalCapacityKwh"
      :hv-voltage="status.hvVoltage"
      :hv-current="status.hvCurrent"
      :hv-power="status.hvPower"
      :hv-battery-active="status.hvBatteryActive"
      :charger-connected="supportsExternalCharge ? status.chargerConnected : null"
      :power-usage-since-last-charge="
        supportsExternalCharge ? status.powerUsageSinceLastCharge : null
      "
      :can-set-charge-limit="supportsExternalCharge"
    />

    <!-- findMyCar -->
    <FindMyCarCard v-else-if="cardId === 'findMyCar'" :vin="vin" />

    <!-- lights -->
    <LightsCard
      v-else-if="cardId === 'lights'"
      :main-beam="status.lightsMainBeam"
      :dipped-beam="status.lightsDippedBeam"
      :side="status.lightsSide"
    />

    <!-- efficiencyDistance -->
    <StatusCard
      v-else-if="cardId === 'efficiencyDistance' && status.mileageOfTheDay !== null"
      icon="route"
      :label="t('vehicle.efficiency.todayDistance')"
      :value="status.mileageOfTheDay.toFixed(1)"
      :unit="t('common.km')"
    />

    <!-- efficiencyEnergy -->
    <StatusCard
      v-else-if="cardId === 'efficiencyEnergy' && status.powerUsageOfDay !== null"
      icon="plug-circle-bolt"
      :label="t('vehicle.efficiency.todayEnergy')"
      :value="status.powerUsageOfDay.toFixed(0)"
      :unit="t('common.wh')"
    />

    <!-- efficiencyCharge -->
    <StatusCard
      v-else-if="cardId === 'efficiencyCharge' && status.mileageSinceLastCharge !== null && !isHev"
      icon="battery-full"
      :label="t('vehicle.efficiency.sinceCharge')"
      :value="status.mileageSinceLastCharge.toFixed(1)"
      :unit="t('common.km')"
    />

    <!-- efficiencyRatio -->
    <StatusCard
      v-else-if="
        cardId === 'efficiencyRatio' &&
        status.powerUsageOfDay !== null &&
        status.mileageOfTheDay !== null &&
        status.mileageOfTheDay > 0
      "
      icon="leaf"
      :label="t('vehicle.efficiency.efficiency')"
      :value="(status.powerUsageOfDay / status.mileageOfTheDay).toFixed(0)"
      :unit="`${t('common.wh')}/${t('common.km')}`"
    />
  </template>
</template>
