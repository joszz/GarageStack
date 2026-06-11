<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import StatusCard from './StatusCard.vue'
import DetailModal from './DetailModal.vue'
import { useModal } from '@/composables/useModal'
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

const { isOpen: modalOpen, open: openModal, close: closeModal } = useModal()
const { sending, lastResult, isPending, send } = useVehicleCommand()

const TEMP_MIN = 16
const TEMP_MAX = 28

const sliderTemp = ref<number>(props.remoteTemperature ?? 22)

const seatLabels = computed(() => [
  t('control.seat.off'),
  t('control.seat.low'),
  t('control.seat.medium'),
  t('control.seat.high'),
])

const seatLeftLocal = ref<number>(props.heatedSeatFrontLeft ?? 0)
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
  if (props.interiorTemperature !== null) parts.push(`${props.interiorTemperature.toFixed(1)}°C`)
  return parts.length ? parts.join(' · ') : null
})

const hasAnyData = computed(
  () =>
    props.climateOn !== null ||
    props.remoteTemperature !== null ||
    props.interiorTemperature !== null ||
    props.exteriorTemperature !== null ||
    props.heatedSeatFrontLeft !== null ||
    props.heatedSeatFrontRight !== null ||
    props.rearWindowDefroster !== null,
)

function handleClimateToggle() {
  if (isPending('climate')) return
  const newValue = !props.climateOn
  send(props.vin, 'climate', newValue ? 'on' : 'off', (s) => s.climateOn === newValue)
}

function handleDefrostToggle() {
  if (isPending('rear-defroster')) return
  const newValue = !props.rearWindowDefroster
  send(
    props.vin,
    'rear-defroster',
    newValue ? 'on' : 'off',
    (s) => s.rearWindowDefroster === newValue,
  )
}

function applyTemperature() {
  send(props.vin, 'climate-temperature', String(sliderTemp.value))
}

function onSeatLeftChange(e: Event) {
  onSeatChange('seat-left', e)
}

function onSeatRightChange(e: Event) {
  onSeatChange('seat-right', e)
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
    @click="openModal"
  />

  <DetailModal :open="modalOpen" :title="t('control.climate')" @close="closeModal">
    <div class="detail-list">
      <!-- AC temperature slider -->
      <div
        v-if="climateOn !== null || remoteTemperature !== null"
        class="detail-list__item detail-list__item--range"
      >
        <font-awesome-icon icon="temperature-half" class="detail-list__item-icon" />
        <div class="range-control range-control--grow">
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
              :disabled="
                sending === 'climate-temperature' || isPending('climate-temperature') || !vin
              "
            />
            <span>{{ TEMP_MAX }}°</span>
            <button
              class="btn btn-primary btn-sm"
              :class="isPending('climate-temperature') ? 'btn--pending' : ''"
              :disabled="
                sending === 'climate-temperature' || isPending('climate-temperature') || !vin
              "
              @click="applyTemperature"
            >
              <font-awesome-icon v-if="sending === 'climate-temperature'" icon="spinner" spin />
              <font-awesome-icon v-else-if="isPending('climate-temperature')" icon="clock" />
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
            :disabled="sending === 'climate' || isPending('climate') || !vin"
            @change="handleClimateToggle"
          />
        </div>
      </div>
      <div v-if="isPending('climate')" class="detail-list__feedback text-info">
        <font-awesome-icon icon="clock" />
        {{ t('control.pending') }}
      </div>
      <div
        v-else-if="lastResult?.key === 'climate' && !lastResult.ok"
        class="detail-list__feedback text-danger"
      >
        {{ t('control.error') }}
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
            :disabled="sending === 'rear-defroster' || isPending('rear-defroster') || !vin"
            @change="handleDefrostToggle"
          />
        </div>
      </div>
      <div v-if="isPending('rear-defroster')" class="detail-list__feedback text-info">
        <font-awesome-icon icon="clock" />
        {{ t('control.pending') }}
      </div>
      <div
        v-else-if="lastResult?.key === 'rear-defroster' && !lastResult.ok"
        class="detail-list__feedback text-danger"
      >
        {{ t('control.error') }}
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
        <div class="range-control range-control--grow">
          <div class="range-control__header">
            <span class="range-control__label">{{ t('vehicle.climateDetail.seatLeft') }}</span>
            <span class="range-control__value">{{ seatLabels[seatLeftLocal] }}</span>
          </div>
          <div class="range-control__row">
            <input
              type="range"
              min="0"
              max="3"
              step="1"
              :value="seatLeftLocal"
              :disabled="sending === 'seat-left' || isPending('seat-left') || !vin"
              @change="onSeatLeftChange"
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
        <div class="range-control range-control--grow">
          <div class="range-control__header">
            <span class="range-control__label">{{ t('vehicle.climateDetail.seatRight') }}</span>
            <span class="range-control__value">{{ seatLabels[seatRightLocal] }}</span>
          </div>
          <div class="range-control__row">
            <input
              type="range"
              min="0"
              max="3"
              step="1"
              :value="seatRightLocal"
              :disabled="sending === 'seat-right' || isPending('seat-right') || !vin"
              @change="onSeatRightChange"
            />
          </div>
          <div class="range-control__labels">
            <span v-for="label in seatLabels" :key="label">{{ label }}</span>
          </div>
        </div>
      </div>

      <!-- General error fallback -->
      <div
        v-if="
          lastResult && !lastResult.ok && !['climate', 'rear-defroster'].includes(lastResult.key)
        "
        class="detail-list__feedback text-danger"
      >
        {{ t('control.error') }}
      </div>
    </div>
  </DetailModal>
</template>

<style scoped>
.range-control--grow {
  flex: 1;
}
</style>
