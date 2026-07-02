<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useVehicleStore } from '@/stores/vehicle'
import { useUiSettingsStore } from '@/stores/settingsUi'
import type { CardId } from '@/stores/settingsShared'
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

const props = defineProps<{ cardId: CardId }>()

const { t } = useI18n()
const router = useRouter()
const store = useVehicleStore()
const settings = useUiSettingsStore()

interface SimpleCardConfig {
  id: CardId
  match: boolean
  icon: string
  label: string
  value: string | number | null
  unit?: string
  variant?: 'success' | 'warning' | 'danger' | 'info'
}

const vin = computed(() => store.vehicles[0]?.vin ?? null)
const status = computed(() => store.currentStatus)

const vehicleType = computed((): VehicleType | 'unknown' => {
  const override = settings.vehicleTypeOverride
  if (override !== 'auto') return override as VehicleType
  return store.detectedVehicleType
})

const isHev = computed(() => vehicleType.value === 'hev')
const latestTrip = computed(() => store.trips[store.trips.length - 1] ?? null)
const topSpeedKmh = computed(() => {
  if (!latestTrip.value) return null
  const speeds = latestTrip.value.points.map((p) => p.speed).filter((s): s is number => s !== null)
  return speeds.length ? Math.round(Math.max(...speeds)) : null
})
const supportsExternalCharge = computed(
  () => vehicleType.value === 'phev' || vehicleType.value === 'bev',
)

// Config table for the branches that render nothing but a plain <StatusCard>. Branches that
// dispatch to a dedicated sub-component (doors, climate, hvBattery, etc.) stay in the
// v-else-if chain below since they aren't simple StatusCard-only cases.
const simpleCards = computed((): SimpleCardConfig[] => {
  const s = status.value
  if (!s) return []
  return [
    {
      id: 'fuelLevel',
      match: s.fuelLevelPercent !== null,
      icon: 'gas-pump',
      label: t('vehicle.fuel'),
      value: s.fuelLevelPercent !== null ? Math.round(s.fuelLevelPercent) : null,
      unit: '%',
      variant:
        s.fuelLevelPercent !== null
          ? s.fuelLevelPercent < 15
            ? 'danger'
            : s.fuelLevelPercent < 30
              ? 'warning'
              : 'success'
          : undefined,
    },
    {
      id: 'fuelRange',
      match: s.fuelRangeKm !== null,
      icon: 'road',
      label: t('vehicle.range'),
      value: s.fuelRangeKm !== null ? Math.round(s.fuelRangeKm) : null,
      unit: t('common.km'),
    },
    {
      id: 'evBattery',
      match: s.evSocPercent !== null,
      icon: 'bolt',
      label: t('vehicle.evSoc'),
      value: s.evSocPercent !== null ? Math.round(s.evSocPercent) : null,
      unit: '%',
      variant:
        s.evSocPercent !== null
          ? s.evSocPercent < 20
            ? 'danger'
            : s.evSocPercent < 50
              ? 'warning'
              : 'success'
          : undefined,
    },
    {
      id: 'charging',
      match: s.isCharging !== null,
      icon: 'plug',
      label: t('vehicle.charging'),
      value: s.isCharging ? t('vehicle.chargingYes') : t('vehicle.chargingNo'),
      variant: s.isCharging ? 'info' : undefined,
    },
    {
      id: 'odometer',
      match: true,
      icon: 'gauge',
      label: t('vehicle.odometer'),
      value: s.odometerKm !== null ? Math.round(s.odometerKm).toLocaleString() : null,
      unit: t('common.km'),
    },
    {
      id: 'battery12v',
      match: true,
      icon: 'battery-three-quarters',
      label: t('vehicle.battery'),
      value: s.batteryVoltage !== null ? s.batteryVoltage.toFixed(1) : null,
      unit: 'V',
      variant: s.batteryVoltage !== null && s.batteryVoltage < 12 ? 'danger' : 'success',
    },
    {
      id: 'sunRoof',
      match: s.sunRoofOpen !== null,
      icon: 'sun',
      label: t('settings.cards.sunRoof'),
      value: s.sunRoofOpen ? t('common.open') : t('common.closed'),
      variant: s.sunRoofOpen ? 'warning' : 'success',
    },
    {
      id: 'efficiencyDistance',
      match: s.mileageOfTheDay !== null,
      icon: 'route',
      label: t('vehicle.efficiency.todayDistance'),
      value: s.mileageOfTheDay !== null ? s.mileageOfTheDay.toFixed(1) : null,
      unit: t('common.km'),
    },
    {
      id: 'efficiencyEnergy',
      match: s.powerUsageOfDay !== null,
      icon: 'plug-circle-bolt',
      label: t('vehicle.efficiency.todayEnergy'),
      value: s.powerUsageOfDay !== null ? s.powerUsageOfDay.toFixed(0) : null,
      unit: t('common.wh'),
    },
    {
      id: 'efficiencyCharge',
      match: s.mileageSinceLastCharge !== null && !isHev.value,
      icon: 'battery-full',
      label: t('vehicle.efficiency.sinceCharge'),
      value: s.mileageSinceLastCharge !== null ? s.mileageSinceLastCharge.toFixed(1) : null,
      unit: t('common.km'),
    },
    // efficiencyRatio - Wh/km when driving data is available
    {
      id: 'efficiencyRatio',
      match: s.powerUsageOfDay !== null && s.mileageOfTheDay !== null && s.mileageOfTheDay > 0,
      icon: 'leaf',
      label: t('vehicle.efficiency.efficiency'),
      value:
        s.powerUsageOfDay !== null && s.mileageOfTheDay !== null
          ? (s.powerUsageOfDay / s.mileageOfTheDay).toFixed(0)
          : null,
      unit: `${t('common.wh')}/${t('common.km')}`,
    },
    // efficiencyRatio - fuel economy estimate for HEV/PHEV from range computer
    {
      id: 'efficiencyRatio',
      match:
        (isHev.value || vehicleType.value === 'phev') &&
        s.fuelRangeKm !== null &&
        s.fuelLevelPercent !== null &&
        s.fuelLevelPercent > 0,
      icon: 'gas-pump',
      label: t('vehicle.efficiency.fuelEconomy'),
      value:
        s.fuelRangeKm !== null && s.fuelLevelPercent !== null
          ? (s.fuelRangeKm / (s.fuelLevelPercent / 100) / 100).toFixed(1)
          : null,
      unit: 'km/%',
    },
    {
      id: 'speed',
      match: s.speed !== null,
      icon: 'gauge-high',
      label: t('vehicle.speed'),
      value: s.speed !== null ? Math.round(s.speed) : null,
      unit: 'km/h',
    },
    {
      id: 'remainingCharge',
      match: s.remainingChargingTime !== null && supportsExternalCharge.value,
      icon: 'clock',
      label: t('vehicle.remainingCharge'),
      value: s.remainingChargingTime,
      unit: t('common.min'),
      variant: 'info',
    },
    {
      id: 'topSpeed',
      match: topSpeedKmh.value !== null,
      icon: 'gauge-high',
      label: t('vehicle.topSpeed'),
      value: topSpeedKmh.value,
      unit: 'km/h',
    },
  ]
})

// At most one entry can match a given cardId (efficiencyRatio has two candidate entries,
// so this preserves the original v-else-if "first match wins" semantics).
const activeSimpleCard = computed(
  () => simpleCards.value.find((c) => c.id === props.cardId && c.match) ?? null,
)
</script>

<template>
  <template v-if="status">
    <!-- simple StatusCard-only cards, driven by the simpleCards config table -->
    <StatusCard
      v-if="activeSimpleCard"
      :icon="activeSimpleCard.icon"
      :label="activeSimpleCard.label"
      :value="activeSimpleCard.value"
      :unit="activeSimpleCard.unit"
      :variant="activeSimpleCard.variant"
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
          : latestTrip
            ? latestTrip.distanceKm.toFixed(1)
            : t('vehicle.noTrips')
      "
      :unit="
        (status.currentJourneyDistance !== null && status.currentJourneyDistance > 0) ||
        latestTrip !== null
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
        latestTrip !== null
      "
      @click="router.push({ name: 'map', query: { selectLatest: '1' } })"
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
