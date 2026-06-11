<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
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
import ChargingSessionCard from './ChargingSessionCard.vue'
import BatteryHeatingCard from './BatteryHeatingCard.vue'

defineProps<{ cardId: CardId }>()

const { t } = useI18n()
const router = useRouter()
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
      :steering-wheel-heating="status.steeringWheelHeating"
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

    <!-- efficiencyRatio - Wh/km when driving data is available -->
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

    <!-- efficiencyRatio - fuel economy estimate for HEV/PHEV from range computer -->
    <StatusCard
      v-else-if="
        cardId === 'efficiencyRatio' &&
        (isHev || vehicleType === 'phev') &&
        status.fuelRangeKm !== null &&
        status.fuelLevelPercent !== null &&
        status.fuelLevelPercent > 0
      "
      icon="gas-pump"
      :label="t('vehicle.efficiency.fuelEconomy')"
      :value="(status.fuelRangeKm / (status.fuelLevelPercent / 100) / 100).toFixed(1)"
      unit="km/%"
    />

    <!-- speed -->
    <StatusCard
      v-else-if="cardId === 'speed' && status.speed !== null"
      icon="gauge-high"
      :label="t('vehicle.speed')"
      :value="Math.round(status.speed)"
      unit="km/h"
    />

    <!-- activeTrip -->
    <StatusCard
      v-else-if="cardId === 'activeTrip'"
      icon="location-arrow"
      :label="
        status.currentJourneyDistance !== null && status.currentJourneyDistance > 0
          ? t('vehicle.activeTrip')
          : t('vehicle.lastTrip')
      "
      :value="
        status.currentJourneyDistance !== null && status.currentJourneyDistance > 0
          ? status.currentJourneyDistance.toFixed(1)
          : store.lastTrip
            ? store.lastTrip.distanceKm.toFixed(1)
            : t('vehicle.noTrips')
      "
      :unit="
        (status.currentJourneyDistance !== null && status.currentJourneyDistance > 0) ||
        store.lastTrip !== null
          ? t('common.km')
          : undefined
      "
      :variant="
        status.currentJourneyDistance !== null && status.currentJourneyDistance > 0
          ? 'info'
          : undefined
      "
      :clickable="
        !(status.currentJourneyDistance !== null && status.currentJourneyDistance > 0) &&
        store.lastTrip !== null
      "
      @click="router.push({ name: 'map', query: { selectLatest: '1' } })"
    />

    <!-- remainingCharge -->
    <StatusCard
      v-else-if="
        cardId === 'remainingCharge' &&
        status.remainingChargingTime !== null &&
        supportsExternalCharge
      "
      icon="clock"
      :label="t('vehicle.remainingCharge')"
      :value="status.remainingChargingTime"
      :unit="t('common.min')"
      variant="info"
    />

    <!-- chargingSession -->
    <ChargingSessionCard
      v-else-if="cardId === 'chargingSession' && supportsExternalCharge"
      :charging-type="status.chargingType"
      :charging-cable-lock="status.chargingCableLock"
      :obc-power-single-phase="status.obcPowerSinglePhase"
      :obc-power-three-phase="status.obcPowerThreePhase"
      :remaining-charging-time="status.remainingChargingTime"
      :bms-charge-status="status.bmsChargeStatus"
      :last-charge-ending-power="status.lastChargeEndingPower"
      :charging-last-end-at="status.chargingLastEndAt"
      :charging-schedule-mode="status.chargingScheduleMode"
      :charging-schedule-start-time="status.chargingScheduleStartTime"
      :charging-schedule-end-time="status.chargingScheduleEndTime"
    />

    <!-- batteryHeating -->
    <BatteryHeatingCard
      v-else-if="cardId === 'batteryHeating' && supportsExternalCharge"
      :battery-heating="status.batteryHeating"
      :schedule-mode="status.batteryHeatingScheduleMode"
      :schedule-start-time="status.batteryHeatingScheduleStartTime"
    />
  </template>
</template>
