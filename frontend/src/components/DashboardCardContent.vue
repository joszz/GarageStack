<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useVehicleStore } from '@/stores/vehicle'
import type { CardId } from '@/cards/registry'
import {
  cardHasData,
  cardIcon,
  hasEnergyEfficiency,
  hasFuelConsumption,
  hasFuelEconomy,
} from '@/cards/registry'
import { useCardData } from '@/cards/useCardData'
import StatusCard from './StatusCard.vue'
import DoorsCard from './DoorsCard.vue'
import WindowsCard from './WindowsCard.vue'
import ClimateDetailCard from './ClimateDetailCard.vue'
import HvBatteryCard from './HvBatteryCard.vue'
import FindMyCarCard from './FindMyCarCard.vue'
import LightsCard from './LightsCard.vue'
import ChargingSessionCard from './ChargingSessionCard.vue'
import BatteryHeatingCard from './BatteryHeatingCard.vue'
import MaintenanceSummaryCard from './MaintenanceSummaryCard.vue'
import { formatNumber } from '@/utils/format'
import { energyUnit, hvBatteryReading, litres, litresPer100Km, whPerKm } from '@/utils/energy'

const props = defineProps<{ cardId: CardId }>()

const { t } = useI18n()
const router = useRouter()
const store = useVehicleStore()

interface SimpleCardConfig {
  id: CardId
  label: string
  value: string | number | null
  /** Only for cards with more than one presentation; otherwise the registry decides. */
  match?: boolean
  /** Only when this presentation needs a different icon than the card's registry icon. */
  icon?: string
  unit?: string
  variant?: 'success' | 'warning' | 'danger' | 'info'
}

const vin = computed(() => store.activeVin)
const status = computed(() => store.currentStatus)
const cardData = useCardData()

const vehicleType = computed(() => store.effectiveVehicleType)
const latestTrip = computed(() => store.trips[store.trips.length - 1] ?? null)
const topSpeedKmh = computed(() => {
  if (!latestTrip.value) return null
  const speeds = latestTrip.value.points.map((p) => p.speed).filter((s): s is number => s !== null)
  return speeds.length ? Math.round(Math.max(...speeds)) : null
})
const supportsExternalCharge = computed(
  () => vehicleType.value === 'phev' || vehicleType.value === 'bev',
)

// A hybrid's energy counters hold fuel, not kWh, so the same telemetry field is labelled and
// scaled differently per drivetrain. See utils/energy.
const reportsFuelCounter = computed(() => energyUnit(vehicleType.value) === 'litres')

const hvBattery = computed(() => {
  const s = status.value
  if (!s) return null
  return hvBatteryReading(s, vehicleType.value, store.hvBatteryCapacityKwh)
})

// Config table for the branches that render nothing but a plain <StatusCard>. Branches that
// dispatch to a dedicated sub-component (doors, climate, hvBattery, etc.) stay in the
// v-else-if chain below since they aren't simple StatusCard-only cases. Whether a card has
// anything to show, and which icon it carries, come from the card registry; an entry only
// spells those out where one card has two presentations to choose between.
const simpleCards = computed((): SimpleCardConfig[] => {
  const s = status.value
  const ctx = cardData.value
  if (!s || !ctx) return []
  const efficiencyWhPerKm = whPerKm(s.powerUsageOfDay, s.mileageOfTheDay)
  const fuelUsedLitres = litres(s.powerUsageOfDay)
  const consumptionL100Km = litresPer100Km(s.powerUsageOfDay, s.mileageOfTheDay)
  return [
    {
      id: 'fuelLevel',
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
      label: t('vehicle.range'),
      value: s.fuelRangeKm !== null ? Math.round(s.fuelRangeKm) : null,
      unit: t('common.km'),
    },
    {
      id: 'evBattery',
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
      label: t('vehicle.charging'),
      value: s.isCharging ? t('vehicle.chargingYes') : t('vehicle.chargingNo'),
      variant: s.isCharging ? 'info' : undefined,
    },
    {
      id: 'odometer',
      label: t('vehicle.odometer'),
      value: s.odometerKm !== null ? Math.round(s.odometerKm).toLocaleString() : null,
      unit: t('common.km'),
    },
    {
      id: 'battery12v',
      label: t('vehicle.battery'),
      value: s.batteryVoltage !== null ? formatNumber(s.batteryVoltage) : null,
      unit: 'V',
      variant: s.batteryVoltage !== null && s.batteryVoltage < 12 ? 'danger' : 'success',
    },
    {
      id: 'sunRoof',
      label: t('settings.cards.sunRoof'),
      value: s.sunRoofOpen ? t('common.open') : t('common.closed'),
      variant: s.sunRoofOpen ? 'warning' : 'success',
    },
    {
      id: 'efficiencyDistance',
      label: t('vehicle.efficiency.todayDistance'),
      value: s.mileageOfTheDay !== null ? formatNumber(s.mileageOfTheDay) : null,
      unit: t('common.km'),
    },
    // efficiencyEnergy - kWh through the traction battery on a plug-in car
    {
      id: 'efficiencyEnergy',
      match: !reportsFuelCounter.value && s.powerUsageOfDay !== null,
      label: t('vehicle.efficiency.todayEnergy'),
      value: s.powerUsageOfDay !== null ? formatNumber(s.powerUsageOfDay) : null,
      unit: t('common.kwh'),
    },
    // efficiencyEnergy - litres burned on a hybrid, whose counter reports fuel
    {
      id: 'efficiencyEnergy',
      match: reportsFuelCounter.value && fuelUsedLitres !== null,
      icon: 'gas-pump',
      label: t('vehicle.efficiency.todayFuel'),
      value: fuelUsedLitres !== null ? formatNumber(fuelUsedLitres) : null,
      unit: t('common.litre'),
    },
    {
      id: 'efficiencyCharge',
      label: t('vehicle.efficiency.sinceCharge'),
      value: s.mileageSinceLastCharge !== null ? formatNumber(s.mileageSinceLastCharge) : null,
      unit: t('common.km'),
    },
    // efficiencyRatio - measured L/100 km on a hybrid, which beats the estimate below
    {
      id: 'efficiencyRatio',
      match: hasFuelConsumption(ctx),
      label: t('vehicle.efficiency.consumption'),
      value: consumptionL100Km !== null ? formatNumber(consumptionL100Km) : null,
      unit: `${t('common.litre')}/100${t('common.km')}`,
    },
    // efficiencyRatio - Wh/km when driving data is available
    {
      id: 'efficiencyRatio',
      match: hasEnergyEfficiency(ctx),
      label: t('vehicle.efficiency.efficiency'),
      value: efficiencyWhPerKm !== null ? formatNumber(efficiencyWhPerKm, 0) : null,
      unit: `${t('common.wh')}/${t('common.km')}`,
    },
    // efficiencyRatio - fuel economy estimate for HEV/PHEV from range computer
    {
      id: 'efficiencyRatio',
      match: hasFuelEconomy(ctx),
      icon: 'gas-pump',
      label: t('vehicle.efficiency.fuelEconomy'),
      value:
        s.fuelRangeKm !== null && s.fuelLevelPercent !== null
          ? formatNumber(s.fuelRangeKm / (s.fuelLevelPercent / 100) / 100)
          : null,
      unit: 'km/%',
    },
    {
      id: 'speed',
      label: t('vehicle.speed'),
      value: s.speed !== null ? Math.round(s.speed) : null,
      unit: 'km/h',
    },
    {
      id: 'remainingCharge',
      label: t('vehicle.remainingCharge'),
      value: s.remainingChargingTime,
      unit: t('common.min'),
      variant: 'info',
    },
    {
      id: 'topSpeed',
      label: t('vehicle.topSpeed'),
      value: topSpeedKmh.value,
      unit: 'km/h',
    },
  ]
})

// At most one entry renders for a given cardId (efficiencyRatio has two candidate entries, so
// the first match wins, as the original v-else-if chain did). An entry without its own match
// falls back to the registry: if the card is on screen at all, its data is there.
const activeSimpleCard = computed(() => {
  const ctx = cardData.value
  if (!ctx) return null
  const entry = simpleCards.value.find(
    (c) => c.id === props.cardId && (c.match ?? cardHasData(c.id, ctx)),
  )
  return entry ? { ...entry, icon: entry.icon ?? cardIcon(entry.id) } : null
})
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
    />

    <!-- hvBattery -->
    <HvBatteryCard
      v-else-if="cardId === 'hvBattery' && hvBattery"
      :vin="vin"
      :soc-percent="hvBattery.socPercent"
      :stored-kwh="hvBattery.storedKwh"
      :capacity-kwh="hvBattery.capacityKwh"
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
          ? formatNumber(status.currentJourneyDistance)
          : latestTrip
            ? formatNumber(latestTrip.distanceKm)
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

    <!-- maintenance -->
    <MaintenanceSummaryCard v-else-if="cardId === 'maintenance'" />
  </template>
</template>
