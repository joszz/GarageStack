<script setup lang="ts">
import { computed, ref, watch } from 'vue'
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
  steeringWheelHeating: boolean | null
}>()

const { isOpen: modalOpen, open: openModal, close: closeModal } = useModal()
const { sending, lastResult, isPending, send } = useVehicleCommand()

const TEMP_MIN = 16
const TEMP_MAX = 28

const seatLabels = computed(() => [
  t('control.seat.off'),
  t('control.seat.low'),
  t('control.seat.medium'),
  t('control.seat.high'),
])

const localClimateOn = ref<boolean | null>(props.climateOn)
const localRearDefroster = ref<boolean | null>(props.rearWindowDefroster)
const localSteeringWheel = ref<boolean | null>(props.steeringWheelHeating)
const sliderTemp = ref<number>(props.remoteTemperature ?? 22)
const seatLeftLocal = ref<number>(props.heatedSeatFrontLeft ?? 0)
const seatRightLocal = ref<number>(props.heatedSeatFrontRight ?? 0)

watch(modalOpen, (open) => {
  if (open) {
    localClimateOn.value = props.climateOn
    localRearDefroster.value = props.rearWindowDefroster
    localSteeringWheel.value = props.steeringWheelHeating
    sliderTemp.value = props.remoteTemperature ?? 22
    seatLeftLocal.value = props.heatedSeatFrontLeft ?? 0
    seatRightLocal.value = props.heatedSeatFrontRight ?? 0
  }
})

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
    props.rearWindowDefroster !== null ||
    props.steeringWheelHeating !== null,
)

const commandKeys = [
  'climate',
  'rear-defroster',
  'steering-wheel',
  'climate-temperature',
  'seat-left',
  'seat-right',
] as const

const anyPending = computed(() => commandKeys.some((k) => isPending(k)))
const isApplying = computed(() => anyPending.value || commandKeys.some((k) => sending.value === k))

const hasPendingChanges = computed(() => {
  if (props.climateOn !== null && localClimateOn.value !== props.climateOn) return true
  if (props.rearWindowDefroster !== null && localRearDefroster.value !== props.rearWindowDefroster)
    return true
  if (
    props.steeringWheelHeating !== null &&
    localSteeringWheel.value !== props.steeringWheelHeating
  )
    return true
  if (
    (props.climateOn !== null || props.remoteTemperature !== null) &&
    sliderTemp.value !== (props.remoteTemperature ?? 22)
  )
    return true
  if (props.heatedSeatFrontLeft !== null && seatLeftLocal.value !== props.heatedSeatFrontLeft)
    return true
  if (props.heatedSeatFrontRight !== null && seatRightLocal.value !== props.heatedSeatFrontRight)
    return true
  return false
})

async function applyAll() {
  if (
    (props.climateOn !== null || props.remoteTemperature !== null) &&
    sliderTemp.value !== (props.remoteTemperature ?? 22)
  ) {
    await send(props.vin, 'climate-temperature', String(sliderTemp.value))
  }
  if (props.climateOn !== null && localClimateOn.value !== props.climateOn) {
    const target = localClimateOn.value
    await send(props.vin, 'climate', target ? 'on' : 'off', (s) => s.climateOn === target)
  }
  if (
    props.rearWindowDefroster !== null &&
    localRearDefroster.value !== props.rearWindowDefroster
  ) {
    const target = localRearDefroster.value
    await send(
      props.vin,
      'rear-defroster',
      target ? 'on' : 'off',
      (s) => s.rearWindowDefroster === target,
    )
  }
  if (
    props.steeringWheelHeating !== null &&
    localSteeringWheel.value !== props.steeringWheelHeating
  ) {
    const target = localSteeringWheel.value
    await send(
      props.vin,
      'steering-wheel',
      target ? 'on' : 'off',
      (s) => s.steeringWheelHeating === target,
    )
  }
  if (props.heatedSeatFrontLeft !== null && seatLeftLocal.value !== props.heatedSeatFrontLeft) {
    await send(props.vin, 'seat-left', String(seatLeftLocal.value))
  }
  if (props.heatedSeatFrontRight !== null && seatRightLocal.value !== props.heatedSeatFrontRight) {
    await send(props.vin, 'seat-right', String(seatRightLocal.value))
  }
}

function onSeatLeftChange(e: Event) {
  seatLeftLocal.value = Number((e.target as HTMLInputElement).value)
}

function onSeatRightChange(e: Event) {
  seatRightLocal.value = Number((e.target as HTMLInputElement).value)
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
              :disabled="isApplying || !vin"
            />
            <span>{{ TEMP_MAX }}°</span>
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
            :checked="localClimateOn ?? false"
            :disabled="isApplying || !vin"
            @change="localClimateOn = !localClimateOn"
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
            :checked="localRearDefroster ?? false"
            :disabled="isApplying || !vin"
            @change="localRearDefroster = !localRearDefroster"
          />
        </div>
      </div>

      <!-- Steering wheel heating toggle -->
      <div
        v-if="steeringWheelHeating !== null"
        class="detail-list__item detail-list__item--control"
      >
        <font-awesome-icon icon="life-ring" class="detail-list__item-icon" />
        <span class="detail-list__item-label">{{ t('control.steeringWheelHeating') }}</span>
        <div class="form-check form-switch">
          <input
            class="form-check-input"
            type="checkbox"
            role="switch"
            :checked="localSteeringWheel ?? false"
            :disabled="isApplying || !vin"
            @change="localSteeringWheel = !localSteeringWheel"
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
              :disabled="isApplying || !vin"
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
              :disabled="isApplying || !vin"
              @change="onSeatRightChange"
            />
          </div>
          <div class="range-control__labels">
            <span v-for="label in seatLabels" :key="label">{{ label }}</span>
          </div>
        </div>
      </div>
    </div>

    <template #footer>
      <span v-if="lastResult && !lastResult.ok && !anyPending" class="text-danger me-auto">
        {{ t('control.error') }}
      </span>
      <button class="btn btn-outline-secondary" @click="closeModal">
        {{ t('common.cancel') }}
      </button>
      <button
        class="btn btn-primary"
        :class="anyPending ? 'btn--pending' : ''"
        :disabled="sending !== null || !vin || !hasPendingChanges"
        @click="applyAll"
      >
        <font-awesome-icon v-if="isApplying" icon="spinner" spin />
        <font-awesome-icon v-else-if="anyPending" icon="clock" />
        <font-awesome-icon v-else icon="check" />
        {{ anyPending ? t('control.pending') : t('common.apply') }}
      </button>
    </template>
  </DetailModal>
</template>

<style scoped>
.range-control--grow {
  flex: 1;
}
</style>
