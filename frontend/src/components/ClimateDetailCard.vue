<script setup lang="ts">
import { ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import StatusCard from './StatusCard.vue'
import DetailModal from './DetailModal.vue'
import { useVehicleCommand } from '@/composables/useVehicleCommand'

const { t } = useI18n()

const props = defineProps<{
  vin: string | null
  climateOn: boolean | null
  remoteTemperature: number | null
  interiorTemperature: number | null
  exteriorTemperature: number | null
  heatedSeatFrontLeft: number | null
  heatedSeatFrontRight: number | null
  rearWindowDefroster: boolean | null
}>()

const modalOpen = ref(false)
const { sending, lastResult, send } = useVehicleCommand()

const TEMP_MIN = 16
const TEMP_MAX = 28

const sliderTemp = ref<number>(props.remoteTemperature ?? 22)

const seatLabels = computed(() => [
  t('control.seat.off'),
  t('control.seat.low'),
  t('control.seat.medium'),
  t('control.seat.high'),
])

const seatLeftLocal  = ref<number>(props.heatedSeatFrontLeft  ?? 0)
const seatRightLocal = ref<number>(props.heatedSeatFrontRight ?? 0)

function onSeatChange(side: 'seat-left' | 'seat-right', e: Event) {
  const val = Number((e.target as HTMLInputElement).value)
  if (side === 'seat-left') seatLeftLocal.value = val
  else seatRightLocal.value = val
  send(props.vin, side, String(val))
}

const summaryValue = computed((): string | null => {
  const parts: string[] = []
  if (props.climateOn !== null)
    parts.push(props.climateOn ? t('vehicle.climateOn') : t('vehicle.climateOff'))
  if (props.interiorTemperature !== null)
    parts.push(`${props.interiorTemperature.toFixed(1)}°C`)
  return parts.length ? parts.join(' · ') : null
})

const hasAnyData = computed(() =>
  props.climateOn !== null ||
  props.remoteTemperature !== null ||
  props.interiorTemperature !== null ||
  props.exteriorTemperature !== null ||
  props.heatedSeatFrontLeft !== null ||
  props.heatedSeatFrontRight !== null ||
  props.rearWindowDefroster !== null,
)

function handleClimateToggle() {
  send(props.vin, 'climate', props.climateOn ? 'off' : 'on')
}

function handleDefrostToggle() {
  send(props.vin, 'rear-defroster', props.rearWindowDefroster ? 'off' : 'on')
}

function applyTemperature() {
  send(props.vin, 'climate-temperature', String(sliderTemp.value))
}
</script>

<template>
  <StatusCard
    v-if="hasAnyData"
    icon="wind"
    :label="t('control.climate')"
    :value="summaryValue"
    :variant="climateOn ? 'info' : undefined"
    clickable
    @click="modalOpen = true"
  />

  <DetailModal
    :open="modalOpen"
    :title="t('control.climate')"
    @close="modalOpen = false"
  >
    <div class="detail-list">
      <!-- AC temperature slider -->
      <div v-if="climateOn !== null || remoteTemperature !== null" class="detail-list__item detail-list__item--range">
        <font-awesome-icon icon="temperature-half" class="detail-list__item-icon" />
        <div class="range-control" style="flex:1;">
          <div class="range-control__header">
            <span class="range-control__label">{{ t('control.temperature') }}</span>
            <span class="range-control__value">{{ sliderTemp }}°C</span>
          </div>
          <div class="range-control__row">
            <span>{{ TEMP_MIN }}°</span>
            <input
              v-model.number="sliderTemp"
              type="range"
              :min="TEMP_MIN"
              :max="TEMP_MAX"
              step="1"
              :disabled="sending === 'climate-temperature' || !vin"
            />
            <span>{{ TEMP_MAX }}°</span>
            <button
              class="btn btn-primary btn-sm"
              :disabled="sending === 'climate-temperature' || !vin"
              @click="applyTemperature"
            >
              <font-awesome-icon v-if="sending === 'climate-temperature'" icon="spinner" spin />
              <font-awesome-icon v-else icon="check" />
            </button>
          </div>
        </div>
      </div>

      <!-- Climate on/off toggle -->
      <div v-if="climateOn !== null" class="detail-list__item detail-list__item--control">
        <font-awesome-icon icon="wind" class="detail-list__item-icon" />
        <span class="detail-list__item-label">{{ t('control.climate') }}</span>
        <div class="form-check form-switch">
          <input
            class="form-check-input"
            type="checkbox"
            role="switch"
            :checked="climateOn"
            :disabled="sending === 'climate' || !vin"
            @change="handleClimateToggle"
          />
        </div>
      </div>

      <!-- Rear defroster toggle -->
      <div v-if="rearWindowDefroster !== null" class="detail-list__item detail-list__item--control">
        <font-awesome-icon icon="car-rear" class="detail-list__item-icon" />
        <span class="detail-list__item-label">{{ t('control.rearDefroster') }}</span>
        <div class="form-check form-switch">
          <input
            class="form-check-input"
            type="checkbox"
            role="switch"
            :checked="rearWindowDefroster"
            :disabled="sending === 'rear-defroster' || !vin"
            @change="handleDefrostToggle"
          />
        </div>
      </div>

      <!-- Interior temperature (read-only) -->
      <div v-if="interiorTemperature !== null" class="detail-list__item">
        <font-awesome-icon icon="thermometer-half" class="detail-list__item-icon" />
        <span class="detail-list__item-value">{{ interiorTemperature.toFixed(1) }} °C</span>
        <span class="detail-list__item-sep">-</span>
        <span class="detail-list__item-label">{{ t('vehicle.temperature.interior') }}</span>
      </div>

      <!-- Exterior temperature (read-only) -->
      <div v-if="exteriorTemperature !== null" class="detail-list__item">
        <font-awesome-icon icon="temperature-low" class="detail-list__item-icon" />
        <span class="detail-list__item-value">{{ exteriorTemperature.toFixed(1) }} °C</span>
        <span class="detail-list__item-sep">-</span>
        <span class="detail-list__item-label">{{ t('vehicle.temperature.exterior') }}</span>
      </div>


      <!-- Driver seat slider -->
      <div v-if="heatedSeatFrontLeft !== null" class="detail-list__item detail-list__item--range">
        <font-awesome-icon icon="couch" class="detail-list__item-icon" />
        <div class="range-control" style="flex:1;">
          <div class="range-control__header">
            <span class="range-control__label">{{ t('vehicle.climateDetail.seatLeft') }}</span>
            <span class="range-control__value">{{ seatLabels[seatLeftLocal] }}</span>
          </div>
          <div class="range-control__row">
            <input
              type="range"
              min="0" max="3" step="1"
              :value="seatLeftLocal"
              :disabled="sending === 'seat-left' || !vin"
              @change="(e) => onSeatChange('seat-left', e)"
            />
          </div>
          <div class="range-control__labels">
            <span v-for="label in seatLabels" :key="label">{{ label }}</span>
          </div>
        </div>
      </div>

      <!-- Passenger seat slider -->
      <div v-if="heatedSeatFrontRight !== null" class="detail-list__item detail-list__item--range">
        <font-awesome-icon icon="couch" class="detail-list__item-icon" />
        <div class="range-control" style="flex:1;">
          <div class="range-control__header">
            <span class="range-control__label">{{ t('vehicle.climateDetail.seatRight') }}</span>
            <span class="range-control__value">{{ seatLabels[seatRightLocal] }}</span>
          </div>
          <div class="range-control__row">
            <input
              type="range"
              min="0" max="3" step="1"
              :value="seatRightLocal"
              :disabled="sending === 'seat-right' || !vin"
              @change="(e) => onSeatChange('seat-right', e)"
            />
          </div>
          <div class="range-control__labels">
            <span v-for="label in seatLabels" :key="label">{{ label }}</span>
          </div>
        </div>
      </div>
    </div>

    <div v-if="lastResult" class="detail-list__feedback" :class="lastResult.ok ? 'text-success' : 'text-danger'">
      {{ lastResult.ok ? t('control.sent') : t('control.error') }}
    </div>
  </DetailModal>
</template>
