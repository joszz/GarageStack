<script setup lang="ts">
import { ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { demoApi } from '@/services/demoApi'
import { useVehicleStore } from '@/stores/vehicle'
import type { TelemetrySnapshot } from '@/services/vehicleApi'

const props = defineProps<{ open: boolean }>()

const { t } = useI18n()
const vehicleStore = useVehicleStore()

const vin = computed(() => vehicleStore.vehicles[0]?.vin ?? null)
const status = computed(() => vehicleStore.currentStatus)

interface ToggleButtonConfig {
  field: keyof TelemetrySnapshot
  label: string
}

const doorToggles: ToggleButtonConfig[] = [
  { field: 'driverDoorOpen', label: 'demo.driver' },
  { field: 'passengerDoorOpen', label: 'demo.passenger' },
  { field: 'rearLeftDoorOpen', label: 'demo.rearLeft' },
  { field: 'rearRightDoorOpen', label: 'demo.rearRight' },
  { field: 'trunkOpen', label: 'demo.trunk' },
  { field: 'bonnetOpen', label: 'demo.bonnet' },
]

const windowToggles: ToggleButtonConfig[] = [
  { field: 'driverWindowOpen', label: 'demo.driver' },
  { field: 'passengerWindowOpen', label: 'demo.passenger' },
  { field: 'rearLeftWindowOpen', label: 'demo.rearLeft' },
  { field: 'rearRightWindowOpen', label: 'demo.rearRight' },
]

const stateToggles: ToggleButtonConfig[] = [
  { field: 'isLocked', label: 'demo.locked' },
  { field: 'engineRunning', label: 'demo.engine' },
  { field: 'climateOn', label: 'demo.climate' },
]

const chargingToggles: ToggleButtonConfig[] = [
  { field: 'chargerConnected', label: 'demo.chargerConnected' },
  { field: 'isCharging', label: 'demo.isCharging' },
]

function statusBool(field: keyof TelemetrySnapshot): boolean {
  return bool(status.value?.[field] as boolean | null | undefined)
}

const socValue = ref<number>(78)
const speedValue = ref<number>(0)

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

async function applySpeed() {
  await sendOverride({ speed: speedValue.value })
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
            v-for="btn in doorToggles"
            :key="btn.field"
            class="demo-toggle-btn"
            :class="{ 'is-active': statusBool(btn.field) }"
            @click="toggle(btn.field, status?.[btn.field] as boolean | null | undefined)"
          >
            {{ t(btn.label) }}
          </button>
        </div>
      </div>

      <div class="demo-panel-section">
        <div class="demo-panel-section-label">{{ t('demo.windows') }}</div>
        <div class="demo-panel-toggles">
          <button
            v-for="btn in windowToggles"
            :key="btn.field"
            class="demo-toggle-btn"
            :class="{ 'is-active': statusBool(btn.field) }"
            @click="toggle(btn.field, status?.[btn.field] as boolean | null | undefined)"
          >
            {{ t(btn.label) }}
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
            v-for="btn in stateToggles"
            :key="btn.field"
            class="demo-toggle-btn"
            :class="{ 'is-active': statusBool(btn.field) }"
            @click="toggle(btn.field, status?.[btn.field] as boolean | null | undefined)"
          >
            {{ t(btn.label) }}
          </button>
        </div>
      </div>

      <div class="demo-panel-section">
        <div class="demo-panel-section-label">{{ t('demo.charging') }}</div>
        <div class="demo-panel-toggles">
          <button
            v-for="btn in chargingToggles"
            :key="btn.field"
            class="demo-toggle-btn"
            :class="{ 'is-active': statusBool(btn.field) }"
            @click="toggle(btn.field, status?.[btn.field] as boolean | null | undefined)"
          >
            {{ t(btn.label) }}
          </button>
        </div>
      </div>

      <div class="demo-panel-section">
        <div class="demo-panel-section-label">{{ t('demo.soc') }}</div>
        <div class="demo-panel-range">
          <input
            v-model.number="socValue"
            type="range"
            min="10"
            max="100"
            step="1"
            :aria-label="t('demo.soc')"
          />
          <span>{{ socValue }}%</span>
          <button class="demo-toggle-btn" @click="applySoc">OK</button>
        </div>
      </div>

      <div class="demo-panel-section">
        <div class="demo-panel-section-label">{{ t('demo.speed') }}</div>
        <div class="demo-panel-range">
          <input
            v-model.number="speedValue"
            type="range"
            min="0"
            max="200"
            step="1"
            :aria-label="t('demo.speed')"
          />
          <span>{{ speedValue }} km/h</span>
          <button class="demo-toggle-btn" @click="applySpeed">OK</button>
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
