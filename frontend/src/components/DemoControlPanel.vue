<script setup lang="ts">
import { ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { demoApi } from '@/services/api'
import { useVehicleStore } from '@/stores/vehicle'

const props = defineProps<{ open: boolean }>()

const { t } = useI18n()
const vehicleStore = useVehicleStore()

const vin = computed(() => vehicleStore.vehicles[0]?.vin ?? null)
const status = computed(() => vehicleStore.currentStatus)

const socValue = ref<number>(78)

async function sendOverride(override: Record<string, boolean | number | null>) {
  if (!vin.value) return
  await demoApi.setStatus(vin.value, override)
  await vehicleStore.fetchStatus(vin.value)
}

async function toggle(field: string, current: boolean | null | undefined) {
  await sendOverride({ [field]: !current })
}

async function applySoc() {
  await sendOverride({ evSocPercent: socValue.value })
}

async function applyTemperature(field: string, value: string) {
  const num = parseFloat(value)
  if (!isNaN(num)) await sendOverride({ [field]: num })
}

function bool(val: boolean | null | undefined): boolean {
  return val === true
}

const lightMode = computed(() => {
  if (bool(status.value?.lightsMainBeam)) return 'main'
  if (bool(status.value?.lightsDippedBeam)) return 'dipped'
  if (bool(status.value?.lightsSide)) return 'side'
  return 'off'
})

async function setLights(mode: 'off' | 'side' | 'dipped' | 'main') {
  await sendOverride({
    lightsMainBeam: mode === 'main',
    lightsDippedBeam: mode === 'dipped',
    lightsSide: mode === 'side' || mode === 'dipped' || mode === 'main',
  })
}
</script>

<template>
  <Transition name="demo-panel">
    <div v-if="props.open" class="demo-panel" role="region" :aria-label="t('demo.controlPanel')">
      <div class="demo-panel-title">
        <font-awesome-icon :icon="['fas', 'flask']" />
        {{ t('demo.controlPanel') }}
      </div>

      <div class="demo-panel-section">
        <div class="demo-panel-section-label">{{ t('demo.doors') }}</div>
        <div class="demo-panel-toggles">
          <button
            class="demo-toggle-btn"
            :class="{ 'is-active': bool(status?.driverDoorOpen) }"
            @click="toggle('driverDoorOpen', status?.driverDoorOpen)"
          >
            {{ t('demo.driver') }}
          </button>
          <button
            class="demo-toggle-btn"
            :class="{ 'is-active': bool(status?.passengerDoorOpen) }"
            @click="toggle('passengerDoorOpen', status?.passengerDoorOpen)"
          >
            {{ t('demo.passenger') }}
          </button>
          <button
            class="demo-toggle-btn"
            :class="{ 'is-active': bool(status?.rearLeftDoorOpen) }"
            @click="toggle('rearLeftDoorOpen', status?.rearLeftDoorOpen)"
          >
            {{ t('demo.rearLeft') }}
          </button>
          <button
            class="demo-toggle-btn"
            :class="{ 'is-active': bool(status?.rearRightDoorOpen) }"
            @click="toggle('rearRightDoorOpen', status?.rearRightDoorOpen)"
          >
            {{ t('demo.rearRight') }}
          </button>
          <button
            class="demo-toggle-btn"
            :class="{ 'is-active': bool(status?.trunkOpen) }"
            @click="toggle('trunkOpen', status?.trunkOpen)"
          >
            {{ t('demo.trunk') }}
          </button>
          <button
            class="demo-toggle-btn"
            :class="{ 'is-active': bool(status?.bonnetOpen) }"
            @click="toggle('bonnetOpen', status?.bonnetOpen)"
          >
            {{ t('demo.bonnet') }}
          </button>
        </div>
      </div>

      <div class="demo-panel-section">
        <div class="demo-panel-section-label">{{ t('demo.windows') }}</div>
        <div class="demo-panel-toggles">
          <button
            class="demo-toggle-btn"
            :class="{ 'is-active': bool(status?.driverWindowOpen) }"
            @click="toggle('driverWindowOpen', status?.driverWindowOpen)"
          >
            {{ t('demo.driver') }}
          </button>
          <button
            class="demo-toggle-btn"
            :class="{ 'is-active': bool(status?.passengerWindowOpen) }"
            @click="toggle('passengerWindowOpen', status?.passengerWindowOpen)"
          >
            {{ t('demo.passenger') }}
          </button>
          <button
            class="demo-toggle-btn"
            :class="{ 'is-active': bool(status?.rearLeftWindowOpen) }"
            @click="toggle('rearLeftWindowOpen', status?.rearLeftWindowOpen)"
          >
            {{ t('demo.rearLeft') }}
          </button>
          <button
            class="demo-toggle-btn"
            :class="{ 'is-active': bool(status?.rearRightWindowOpen) }"
            @click="toggle('rearRightWindowOpen', status?.rearRightWindowOpen)"
          >
            {{ t('demo.rearRight') }}
          </button>
        </div>
      </div>

      <div class="demo-panel-section">
        <div class="demo-panel-section-label">{{ t('demo.lights') }}</div>
        <div class="demo-panel-toggles">
          <button
            class="demo-toggle-btn"
            :class="{ 'is-active': lightMode === 'off' }"
            @click="setLights('off')"
          >
            {{ t('demo.lightsOff') }}
          </button>
          <button
            class="demo-toggle-btn"
            :class="{ 'is-active': lightMode === 'side' }"
            @click="setLights('side')"
          >
            {{ t('demo.sidelights') }}
          </button>
          <button
            class="demo-toggle-btn"
            :class="{ 'is-active': lightMode === 'dipped' }"
            @click="setLights('dipped')"
          >
            {{ t('demo.dippedBeam') }}
          </button>
          <button
            class="demo-toggle-btn"
            :class="{ 'is-active': lightMode === 'main' }"
            @click="setLights('main')"
          >
            {{ t('demo.mainBeam') }}
          </button>
        </div>
      </div>

      <div class="demo-panel-section">
        <div class="demo-panel-section-label">{{ t('demo.state') }}</div>
        <div class="demo-panel-toggles">
          <button
            class="demo-toggle-btn"
            :class="{ 'is-active': bool(status?.isLocked) }"
            @click="toggle('isLocked', status?.isLocked)"
          >
            {{ t('demo.locked') }}
          </button>
          <button
            class="demo-toggle-btn"
            :class="{ 'is-active': bool(status?.engineRunning) }"
            @click="toggle('engineRunning', status?.engineRunning)"
          >
            {{ t('demo.engine') }}
          </button>
          <button
            class="demo-toggle-btn"
            :class="{ 'is-active': bool(status?.climateOn) }"
            @click="toggle('climateOn', status?.climateOn)"
          >
            {{ t('demo.climate') }}
          </button>
        </div>
      </div>

      <div class="demo-panel-section">
        <div class="demo-panel-section-label">{{ t('demo.charging') }}</div>
        <div class="demo-panel-toggles">
          <button
            class="demo-toggle-btn"
            :class="{ 'is-active': bool(status?.chargerConnected) }"
            @click="toggle('chargerConnected', status?.chargerConnected)"
          >
            {{ t('demo.chargerConnected') }}
          </button>
          <button
            class="demo-toggle-btn"
            :class="{ 'is-active': bool(status?.isCharging) }"
            @click="toggle('isCharging', status?.isCharging)"
          >
            {{ t('demo.isCharging') }}
          </button>
        </div>
      </div>

      <div class="demo-panel-section">
        <div class="demo-panel-section-label">{{ t('demo.soc') }}</div>
        <div class="demo-panel-range">
          <input v-model.number="socValue" type="range" min="10" max="100" step="1" />
          <span>{{ socValue }}%</span>
          <button class="demo-toggle-btn" @click="applySoc">OK</button>
        </div>
      </div>

      <div class="demo-panel-section">
        <div class="demo-panel-section-label">{{ t('demo.temperature') }}</div>
        <div class="demo-panel-inputs">
          <div class="demo-panel-input-group">
            <label>{{ t('demo.interior') }} (°C)</label>
            <input
              type="number"
              :value="status?.interiorTemperature ?? 21"
              step="0.5"
              @change="
                applyTemperature('interiorTemperature', ($event.target as HTMLInputElement).value)
              "
            />
          </div>
          <div class="demo-panel-input-group">
            <label>{{ t('demo.exterior') }} (°C)</label>
            <input
              type="number"
              :value="status?.exteriorTemperature ?? 14"
              step="0.5"
              @change="
                applyTemperature('exteriorTemperature', ($event.target as HTMLInputElement).value)
              "
            />
          </div>
        </div>
      </div>
    </div>
  </Transition>
</template>
