<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import ExpandableStatusCard from './ExpandableStatusCard.vue'
import DetailListItem from './DetailListItem.vue'
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
const applyInProgress = ref(false)
const { sending, lastResult, isPending, send, waitUntilSettled } = useVehicleCommand()

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
const sliderTemp = ref<number>(props.remoteTemperature ?? 22)
const seatLeftLocal = ref<number>(props.heatedSeatFrontLeft ?? 0)
const seatRightLocal = ref<number>(props.heatedSeatFrontRight ?? 0)

watch(modalOpen, (open) => {
  if (open) {
    localClimateOn.value = props.climateOn
    localRearDefroster.value = props.rearWindowDefroster
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
    props.rearWindowDefroster !== null,
)

const commandKeys = [
  'climate',
  'rear-defroster',
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

// The real vehicle API only processes one command at a time (each can take up to ~30s
// to reach the car), so a batch of changes must be sent one at a time, waiting for each
// to settle before sending the next - firing them all at once queues later commands
// behind earlier ones and makes them miss their own confirmation window.
async function applyAll() {
  applyInProgress.value = true
  try {
    if (
      (props.climateOn !== null || props.remoteTemperature !== null) &&
      sliderTemp.value !== (props.remoteTemperature ?? 22)
    ) {
      const target = sliderTemp.value
      await send(
        props.vin,
        'climate-temperature',
        String(target),
        (s) => s.remoteTemperature === target,
      )
      await waitUntilSettled('climate-temperature')
    }
    if (props.climateOn !== null && localClimateOn.value !== props.climateOn) {
      const target = localClimateOn.value
      await send(props.vin, 'climate', target ? 'on' : 'off', (s) => s.climateOn === target)
      await waitUntilSettled('climate')
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
      await waitUntilSettled('rear-defroster')
    }
    if (props.heatedSeatFrontLeft !== null && seatLeftLocal.value !== props.heatedSeatFrontLeft) {
      const target = seatLeftLocal.value
      await send(props.vin, 'seat-left', String(target), (s) => s.heatedSeatFrontLeft === target)
      await waitUntilSettled('seat-left')
    }
    if (
      props.heatedSeatFrontRight !== null &&
      seatRightLocal.value !== props.heatedSeatFrontRight
    ) {
      const target = seatRightLocal.value
      await send(props.vin, 'seat-right', String(target), (s) => s.heatedSeatFrontRight === target)
      await waitUntilSettled('seat-right')
    }
  } finally {
    applyInProgress.value = false
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
  <ExpandableStatusCard
    v-if="hasAnyData"
    icon="wind"
    :title="t('control.climate')"
    :value="summaryValue"
    :variant="climateOn ? 'info' : undefined"
    v-model:open="modalOpen"
  >
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

      <!-- Interior temperature (read-only) -->
      <DetailListItem
        v-if="interiorTemperature !== null"
        icon="thermometer-half"
        :value="`${interiorTemperature.toFixed(1)} °C`"
        :label="t('vehicle.temperature.interior')"
      />

      <!-- Exterior temperature (read-only) -->
      <DetailListItem
        v-if="exteriorTemperature !== null"
        icon="temperature-low"
        :value="`${exteriorTemperature.toFixed(1)} °C`"
        :label="t('vehicle.temperature.exterior')"
      />

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

    <template #footer="{ close }">
      <span v-if="lastResult && !lastResult.ok && !anyPending" class="text-danger me-auto">
        {{ t('control.error') }}
      </span>
      <button class="btn btn-outline-secondary" @click="close">
        {{ t('common.cancel') }}
      </button>
      <button
        class="btn btn-primary"
        :class="anyPending ? 'btn--pending' : ''"
        :disabled="applyInProgress || !vin || !hasPendingChanges"
        @click="applyAll"
      />
    </template>
  </ExpandableStatusCard>
</template>

<style scoped>
.range-control--grow {
  flex: 1;
}
</style>
