<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import ExpandableStatusCard from './ExpandableStatusCard.vue'
import DetailListItem from './DetailListItem.vue'
import DetailListSwitch from './DetailListSwitch.vue'
import RangeControl from './RangeControl.vue'
import CommandFailure from './CommandFailure.vue'
import { useVehicleCommand } from '@/composables/useVehicleCommand'
import type { TelemetrySnapshot } from '@/services/vehicleApi'
import { useUnits } from '@/composables/useUnits'

const { t } = useI18n()
const units = useUnits()

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

// The car takes whole degrees Celsius, so the slider steps through those whatever the display
// unit; in Fahrenheit each step is labelled with its rounded equivalent.
const TEMP_MIN = 16
const TEMP_MAX = 28

function wholeDegrees(celsius: number): string {
  return units.value.measure('temperature', celsius, { decimals: 0 })!.value
}

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
  const interior = units.value.format('temperature', props.interiorTemperature)
  if (interior !== null) parts.push(interior)
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

const temperatureText = computed(
  () => `${wholeDegrees(sliderTemp.value)} ${units.value.symbol('temperature')}`,
)
const temperatureEdges = computed((): [string, string] => [
  `${wholeDegrees(TEMP_MIN)}°`,
  `${wholeDegrees(TEMP_MAX)}°`,
])
const controlsDisabled = computed(() => isApplying.value || !props.vin)

function seatText(level: number): string {
  return seatLabels.value[level] ?? ''
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
      <RangeControl
        v-if="climateOn !== null || remoteTemperature !== null"
        v-model="sliderTemp"
        icon="temperature-half"
        :label="t('control.temperature')"
        :value-text="temperatureText"
        :min="TEMP_MIN"
        :max="TEMP_MAX"
        :edge-labels="temperatureEdges"
        :disabled="controlsDisabled"
      />

      <DetailListSwitch
        v-if="climateOn !== null"
        v-model="localClimateOn"
        icon="wind"
        :label="t('control.climate')"
        :disabled="controlsDisabled"
      />

      <DetailListSwitch
        v-if="rearWindowDefroster !== null"
        v-model="localRearDefroster"
        icon="car-rear"
        :label="t('control.rearDefroster')"
        :disabled="controlsDisabled"
      />

      <!-- Interior temperature (read-only) -->
      <DetailListItem
        v-if="interiorTemperature !== null"
        icon="thermometer-half"
        :value="units.format('temperature', interiorTemperature)"
        :label="t('vehicle.temperature.interior')"
      />

      <!-- Exterior temperature (read-only) -->
      <DetailListItem
        v-if="exteriorTemperature !== null"
        icon="temperature-low"
        :value="units.format('temperature', exteriorTemperature)"
        :label="t('vehicle.temperature.exterior')"
      />

      <RangeControl
        v-if="heatedSeatFrontLeft !== null"
        v-model="seatLeftLocal"
        icon="couch"
        :label="t('vehicle.climateDetail.seatLeft')"
        :value-text="seatText(seatLeftLocal)"
        :min="0"
        :max="3"
        :tick-labels="seatLabels"
        :disabled="controlsDisabled"
      />

      <RangeControl
        v-if="heatedSeatFrontRight !== null"
        v-model="seatRightLocal"
        icon="couch"
        :label="t('vehicle.climateDetail.seatRight')"
        :value-text="seatText(seatRightLocal)"
        :min="0"
        :max="3"
        :tick-labels="seatLabels"
        :disabled="controlsDisabled"
      />
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
