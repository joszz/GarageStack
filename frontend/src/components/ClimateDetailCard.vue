<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import ExpandableStatusCard from './ExpandableStatusCard.vue'
import DetailListItem from './DetailListItem.vue'
import CommandFailure from './CommandFailure.vue'
import { useVehicleCommand } from '@/composables/useVehicleCommand'
import type { TelemetrySnapshot } from '@/services/vehicleApi'
import { formatNumber } from '@/utils/format'

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
  if (props.interiorTemperature !== null) parts.push(`${formatNumber(props.interiorTemperature)}°C`)
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

interface ClimateChange {
  command: (typeof commandKeys)[number]
  value: string
  isConfirmed: (s: TelemetrySnapshot) => boolean
}

// What Apply sends, in the order it sends it. The button's enabled state reads the same list, so
// the two cannot disagree about what counts as a change.
const pendingChanges = computed((): ClimateChange[] => {
  const changes: ClimateChange[] = []
  const temperature = sliderTemp.value
  if (
    (props.climateOn !== null || props.remoteTemperature !== null) &&
    temperature !== (props.remoteTemperature ?? 22)
  )
    changes.push({
      command: 'climate-temperature',
      value: String(temperature),
      isConfirmed: (s) => s.remoteTemperature === temperature,
    })
  const climateOn = localClimateOn.value
  if (props.climateOn !== null && climateOn !== props.climateOn)
    changes.push({
      command: 'climate',
      value: climateOn ? 'on' : 'off',
      isConfirmed: (s) => s.climateOn === climateOn,
    })
  const defroster = localRearDefroster.value
  if (props.rearWindowDefroster !== null && defroster !== props.rearWindowDefroster)
    changes.push({
      command: 'rear-defroster',
      value: defroster ? 'on' : 'off',
      isConfirmed: (s) => s.rearWindowDefroster === defroster,
    })
  const seatLeft = seatLeftLocal.value
  if (props.heatedSeatFrontLeft !== null && seatLeft !== props.heatedSeatFrontLeft)
    changes.push({
      command: 'seat-left',
      value: String(seatLeft),
      isConfirmed: (s) => s.heatedSeatFrontLeft === seatLeft,
    })
  const seatRight = seatRightLocal.value
  if (props.heatedSeatFrontRight !== null && seatRight !== props.heatedSeatFrontRight)
    changes.push({
      command: 'seat-right',
      value: String(seatRight),
      isConfirmed: (s) => s.heatedSeatFrontRight === seatRight,
    })
  return changes
})

const hasPendingChanges = computed(() => pendingChanges.value.length > 0)

// The real vehicle API only processes one command at a time (each can take up to ~30s
// to reach the car), so a batch of changes must be sent one at a time, waiting for each
// to settle before sending the next - firing them all at once queues later commands
// behind earlier ones and makes them miss their own confirmation window. The first command
// that fails ends the batch: a car that refused one (asleep, out of reach) refuses the rest
// too, and the next send would clear the error before anyone read it.
async function applyAll() {
  applyInProgress.value = true
  try {
    for (const change of pendingChanges.value) {
      const sent = await send(props.vin, change.command, change.value, {
        isConfirmed: change.isConfirmed,
      })
      if (!sent) break
      await waitUntilSettled(change.command)
      if (lastResult.value?.ok === false) break
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
        :value="`${formatNumber(interiorTemperature)} °C`"
        :label="t('vehicle.temperature.interior')"
      />

      <!-- Exterior temperature (read-only) -->
      <DetailListItem
        v-if="exteriorTemperature !== null"
        icon="temperature-low"
        :value="`${formatNumber(exteriorTemperature)} °C`"
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
      <CommandFailure
        v-if="lastResult && !lastResult.ok && !anyPending"
        class="me-auto"
        :detail="lastResult.detail"
      />
      <button class="btn btn-outline-secondary" @click="close">
        {{ t('common.cancel') }}
      </button>
      <button
        class="btn btn-primary"
        :class="anyPending ? 'btn--pending' : ''"
        :disabled="applyInProgress || !vin || !hasPendingChanges"
        @click="applyAll"
      >
        <font-awesome-icon v-if="isApplying" icon="spinner" spin />
        {{ anyPending ? t('control.pending') : t('common.apply') }}
      </button>
    </template>
  </ExpandableStatusCard>
</template>

<style scoped>
.range-control--grow {
  flex: 1;
}
</style>
