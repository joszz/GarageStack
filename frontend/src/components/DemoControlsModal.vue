<script setup lang="ts">
import { computed, ref, useId } from 'vue'
import { useI18n } from 'vue-i18n'
import DetailModal from '@/components/DetailModal.vue'
import SettingsTabs from '@/components/SettingsTabs.vue'
import SettingsSection from '@/components/settings/SettingsSection.vue'
import DetailListSwitch from '@/components/DetailListSwitch.vue'
import RangeControl from '@/components/RangeControl.vue'
import ChipGroup from '@/components/ChipGroup.vue'
import {
  useDemoControls,
  type DemoChargingType,
  type DemoLights,
  type DemoMotion,
  type DemoSliderField,
  type DemoSwitchField,
} from '@/composables/useDemoControls'
import { useUnits } from '@/composables/useUnits'
import { formatNumber } from '@/utils/format'
import { titleWithCar } from '@/utils/vehicleLabels'
import type { ChipOption } from '@/utils/chips'

// The Demo Controls: the shown car's state, changed live, in the same dialog as the rest of the
// app. Every change goes to the car shown and to it alone.
defineProps<{ open: boolean }>()
const emit = defineEmits<{ (e: 'close'): void }>()

const { t } = useI18n()
const units = useUnits()
const id = useId()
const demo = useDemoControls()

const title = computed(() => titleWithCar(t('demo.controlPanel'), demo.carLabel.value))

type DemoTab = 'car' | 'driving' | 'energy' | 'climate' | 'tyres'
const activeTab = ref<DemoTab>('car')
const tabs = computed(() => [
  { id: 'car' as const, icon: 'car-side', label: t('demo.tabs.car') },
  { id: 'driving' as const, icon: 'gauge-high', label: t('demo.tabs.driving') },
  { id: 'energy' as const, icon: 'bolt', label: t('demo.tabs.energy') },
  { id: 'climate' as const, icon: 'temperature-half', label: t('demo.tabs.climate') },
  { id: 'tyres' as const, icon: 'circle', label: t('demo.tabs.tyres') },
])

// A switch per field, with its icon and label key.
function switchRows(rows: [DemoSwitchField, string, string][]) {
  return rows.map(([field, icon, label]) => ({ field, icon, label, model: demo.switchFor(field) }))
}

const doorRows = switchRows([
  ['driverDoorOpen', 'door-open', 'demo.driver'],
  ['passengerDoorOpen', 'door-open', 'demo.passenger'],
  ['rearLeftDoorOpen', 'door-open', 'demo.rearLeft'],
  ['rearRightDoorOpen', 'door-open', 'demo.rearRight'],
  ['trunkOpen', 'car-rear', 'demo.trunk'],
  ['bonnetOpen', 'car-side', 'demo.bonnet'],
])
const windowRows = switchRows([
  ['driverWindowOpen', 'car-side', 'demo.driver'],
  ['passengerWindowOpen', 'car-side', 'demo.passenger'],
  ['rearLeftWindowOpen', 'car-side', 'demo.rearLeft'],
  ['rearRightWindowOpen', 'car-side', 'demo.rearRight'],
])
const locked = demo.switchFor('isLocked')
const engine = demo.switchFor('engineRunning')
const online = demo.switchFor('isAvailable')
const climate = demo.switchFor('climateOn')
const connected = demo.switchFor('chargerConnected')
const charging = demo.switchFor('isCharging')

const lightOptions = computed((): ChipOption<DemoLights>[] => [
  { value: 'off', label: t('demo.lightsOff') },
  { value: 'side', label: t('demo.sidelights') },
  { value: 'dipped', label: t('demo.dippedBeam') },
  { value: 'main', label: t('demo.mainBeam') },
])
const motionOptions = computed((): ChipOption<DemoMotion>[] => [
  { value: 'parked', label: t('demo.parked'), icon: 'square-parking' },
  { value: 'driving', label: t('demo.driving'), icon: 'road' },
])
const chargingTypeOptions: ChipOption<DemoChargingType>[] = [
  { value: 'AC', label: 'AC', icon: 'plug' },
  { value: 'DC', label: 'DC', icon: 'charging-station' },
]

// Every slider moves in the metric unit the car reports and reads out in the user's units.
const soc = demo.sliderFor('evSocPercent', 78)
const fuel = demo.sliderFor('fuelLevelPercent', 60)
const speed = demo.sliderFor('speed', 0)
const interior = demo.sliderFor('interiorTemperature', 21)
const exterior = demo.sliderFor('exteriorTemperature', 14)
const auxBattery = demo.sliderFor('batteryVoltage', 12.6)
const tyres: [DemoSliderField, string][] = [
  ['tyrePressureFrontLeft', 'demo.frontLeft'],
  ['tyrePressureFrontRight', 'demo.frontRight'],
  ['tyrePressureRearLeft', 'demo.rearLeft'],
  ['tyrePressureRearRight', 'demo.rearRight'],
]
const tyreRows = tyres.map(([field, label]) => ({
  field,
  label,
  slider: demo.sliderFor(field, 2.4),
}))

const percent = (value: number) => `${formatNumber(value, 0)}%`
const temperature = (value: number) => units.value.format('temperature', value) ?? ''
const pressure = (value: number) => units.value.format('pressure', value) ?? ''
const volts = (value: number) => `${formatNumber(value, 1)} V`
const kilowatts = (value: number) => `${formatNumber(value, 1)} kW`
</script>

<template>
  <DetailModal :open="open" :title="title" wide @close="emit('close')">
    <template #navigation>
      <SettingsTabs
        v-model="activeTab"
        :tabs="tabs"
        :label="t('demo.tabs.label')"
        :id-prefix="id"
      />
    </template>

    <p v-if="demo.failed.value" class="text-danger text-sm" role="alert">
      <font-awesome-icon icon="triangle-exclamation" aria-hidden="true" />
      {{ t('demo.failed') }}
    </p>

    <!-- Kept mounted between tabs, like the settings dialog's panels, so a slider keeps its
         place and a switch its focus. -->
    <div
      :id="`${id}-panel-car`"
      role="tabpanel"
      :aria-labelledby="`${id}-tab-car`"
      :hidden="activeTab !== 'car'"
      tabindex="0"
    >
      <SettingsSection :title="t('demo.doors')">
        <div class="detail-list">
          <DetailListSwitch
            v-for="row in doorRows"
            :key="row.field"
            v-model="row.model.value"
            :icon="row.icon"
            :label="t(row.label)"
          />
        </div>
      </SettingsSection>
      <SettingsSection :title="t('demo.windows')">
        <div class="detail-list">
          <DetailListSwitch
            v-for="row in windowRows"
            :key="row.field"
            v-model="row.model.value"
            :icon="row.icon"
            :label="t(row.label)"
          />
        </div>
      </SettingsSection>
      <SettingsSection :title="t('demo.state')">
        <div class="detail-list">
          <DetailListSwitch v-model="locked" icon="lock" :label="t('demo.locked')" />
          <div class="detail-list__item">
            <font-awesome-icon icon="lightbulb" class="detail-list__item-icon" />
            <ChipGroup
              v-model="demo.lights.value"
              :options="lightOptions"
              :group-label="t('demo.lights')"
            />
          </div>
        </div>
      </SettingsSection>
    </div>

    <div
      :id="`${id}-panel-driving`"
      role="tabpanel"
      :aria-labelledby="`${id}-tab-driving`"
      :hidden="activeTab !== 'driving'"
      tabindex="0"
    >
      <div class="detail-list">
        <div class="detail-list__item">
          <font-awesome-icon icon="car-side" class="detail-list__item-icon" />
          <ChipGroup
            v-model="demo.motion.value"
            :options="motionOptions"
            :group-label="t('demo.motion')"
          />
        </div>
        <RangeControl
          v-model="speed.value.value"
          icon="gauge-high"
          :label="t('demo.speed')"
          :value-text="units.format('speed', speed.value.value) ?? ''"
          :min="0"
          :max="200"
          @change="speed.commit"
        />
        <DetailListSwitch v-model="engine" icon="power-off" :label="t('demo.engine')" />
        <DetailListSwitch v-model="online" icon="wifi" :label="t('demo.online')" />
        <DetailListSwitch
          v-model="demo.gatewaySignedIn.value"
          icon="key"
          :label="t('demo.gatewaySignedIn')"
        />
      </div>
    </div>

    <div
      :id="`${id}-panel-energy`"
      role="tabpanel"
      :aria-labelledby="`${id}-tab-energy`"
      :hidden="activeTab !== 'energy'"
      tabindex="0"
    >
      <SettingsSection :title="t('demo.charging')">
        <div class="detail-list">
          <DetailListSwitch v-model="connected" icon="plug" :label="t('demo.chargerConnected')" />
          <DetailListSwitch
            v-model="charging"
            icon="plug-circle-bolt"
            :label="t('demo.isCharging')"
          />
          <div class="detail-list__item">
            <font-awesome-icon icon="charging-station" class="detail-list__item-icon" />
            <ChipGroup
              v-model="demo.chargingType.value"
              :options="chargingTypeOptions"
              :group-label="t('demo.chargingType')"
            />
          </div>
          <RangeControl
            v-model="demo.chargingPower.value.value"
            icon="bolt"
            :label="t('demo.chargingPower')"
            :value-text="kilowatts(demo.chargingPower.value.value)"
            :min="1"
            :max="demo.chargingType.value === 'DC' ? 150 : 22"
            :step="0.5"
            :disabled="!demo.status.value?.isCharging"
            @change="demo.chargingPower.commit"
          />
        </div>
      </SettingsSection>
      <SettingsSection :title="t('demo.levels')">
        <div class="detail-list">
          <RangeControl
            v-model="soc.value.value"
            icon="battery-half"
            :label="t('demo.soc')"
            :value-text="percent(soc.value.value)"
            :min="5"
            :max="100"
            @change="soc.commit"
          />
          <RangeControl
            v-if="demo.hasFuel.value"
            v-model="fuel.value.value"
            icon="gas-pump"
            :label="t('demo.fuel')"
            :value-text="percent(fuel.value.value)"
            :min="0"
            :max="100"
            @change="fuel.commit"
          />
          <RangeControl
            v-model="auxBattery.value.value"
            icon="car-battery"
            :label="t('demo.auxBattery')"
            :value-text="volts(auxBattery.value.value)"
            :min="11"
            :max="14.8"
            :step="0.1"
            @change="auxBattery.commit"
          />
        </div>
      </SettingsSection>
    </div>

    <div
      :id="`${id}-panel-climate`"
      role="tabpanel"
      :aria-labelledby="`${id}-tab-climate`"
      :hidden="activeTab !== 'climate'"
      tabindex="0"
    >
      <div class="detail-list">
        <DetailListSwitch v-model="climate" icon="wind" :label="t('demo.climate')" />
        <RangeControl
          v-model="interior.value.value"
          icon="temperature-half"
          :label="t('demo.interior')"
          :value-text="temperature(interior.value.value)"
          :min="-10"
          :max="45"
          :step="0.5"
          @change="interior.commit"
        />
        <RangeControl
          v-model="exterior.value.value"
          icon="temperature-low"
          :label="t('demo.exterior')"
          :value-text="temperature(exterior.value.value)"
          :min="-20"
          :max="40"
          :step="0.5"
          @change="exterior.commit"
        />
      </div>
    </div>

    <div
      :id="`${id}-panel-tyres`"
      role="tabpanel"
      :aria-labelledby="`${id}-tab-tyres`"
      :hidden="activeTab !== 'tyres'"
      tabindex="0"
    >
      <div class="detail-list">
        <RangeControl
          v-for="tyre in tyreRows"
          :key="tyre.field"
          v-model="tyre.slider.value.value"
          icon="circle"
          :label="t(tyre.label)"
          :value-text="pressure(tyre.slider.value.value)"
          :min="1.2"
          :max="3.6"
          :step="0.05"
          @change="tyre.slider.commit"
        />
      </div>
    </div>
  </DetailModal>
</template>
