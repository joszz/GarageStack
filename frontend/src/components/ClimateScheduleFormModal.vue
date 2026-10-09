<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import DetailModal from './DetailModal.vue'
import SettingsSection from './settings/SettingsSection.vue'
import SettingsToggle from './SettingsToggle.vue'
import ChipGroup from './ChipGroup.vue'
import RangeControl from './RangeControl.vue'
import DetailListSwitch from './DetailListSwitch.vue'
import WeekdayPicker from './WeekdayPicker.vue'
import { useClimateSchedulesStore } from '@/stores/climateSchedules'
import { useVehicleStore } from '@/stores/vehicle'
import type {
  ClimateSchedule,
  ClimateScheduleRequest,
  IsoWeekday,
} from '@/services/climateScheduleApi'
import { useUnits } from '@/composables/useUnits'
import { useErrorMessage } from '@/composables/useErrorMessage'
import { useMeasureField } from '@/composables/useMeasureField'
import { useClimateLabels } from '@/composables/useClimateLabels'
import {
  CLIMATE_TEMPERATURE_MAX_C,
  CLIMATE_TEMPERATURE_MIN_C,
  SEAT_LEVEL_MAX,
  type ClimateOnMode,
} from '@/utils/climate'
import {
  WORKWEEK,
  browserTimeZone,
  commandLabel,
  daysSummary,
  outcomeVariant,
  runLabel,
} from '@/utils/climateSchedule'

const props = defineProps<{
  open: boolean
  vin: string
  schedule: ClimateSchedule | null
}>()

const emit = defineEmits<{ (e: 'close'): void }>()

const { t } = useI18n()
const store = useClimateSchedulesStore()
const vehicleStore = useVehicleStore()
const units = useUnits()
const errorMessage = useErrorMessage()
const { temperatureText, temperatureEdges, seatLabels, seatText, modeOptions } = useClimateLabels()

// A new schedule warms to 21 °C unless the car is already set to something else.
const DEFAULT_TEMPERATURE_C = 21
// What "only when cold" starts at when switched on: a frosty morning.
const DEFAULT_COLDER_THAN_C = 5

const name = ref('')
const startTime = ref('07:30')
const days = ref<IsoWeekday[]>([...WORKWEEK])
const enabled = ref(true)
const mode = ref<ClimateOnMode>('on')
const temperatureC = ref(DEFAULT_TEMPERATURE_C)
const rearDefroster = ref<boolean | null>(false)
const seatLeft = ref(0)
const seatRight = ref(0)
const conditionOn = ref(false)
const colderThan = useMeasureField(units, 'temperature')
const warmerThan = useMeasureField(units, 'temperature')
const validationError = ref<string | null>(null)
const saving = ref(false)
const pendingDelete = ref(false)

const isEdit = computed(() => props.schedule !== null)
const title = computed(() =>
  isEdit.value ? t('climateSchedules.editSchedule') : t('climateSchedules.addSchedule'),
)
const temperatureUnit = computed(() => ({ unit: units.value.symbol('temperature') }))

// The extras show as the climate popup shows them: only for a car that reports them. Before the
// car has reported anything, all are offered.
const status = computed(() => vehicleStore.currentStatus)
const offersRearDefroster = computed(
  () =>
    status.value === null ||
    status.value.rearWindowDefroster !== null ||
    rearDefroster.value === true,
)
const offersSeatHeating = computed(
  () =>
    status.value === null ||
    status.value.heatedSeatFrontLeft !== null ||
    seatLeft.value > 0 ||
    seatRight.value > 0,
)

function startingTemperature(): number {
  const current = status.value?.remoteTemperature
  return current != null &&
    current >= CLIMATE_TEMPERATURE_MIN_C &&
    current <= CLIMATE_TEMPERATURE_MAX_C
    ? Math.round(current)
    : DEFAULT_TEMPERATURE_C
}

watch(
  () => props.open,
  (isOpen) => {
    if (!isOpen) return
    validationError.value = null
    pendingDelete.value = false
    const s = props.schedule
    name.value = s?.name ?? ''
    startTime.value = s?.startTime ?? '07:30'
    days.value = s ? [...s.days] : [...WORKWEEK]
    enabled.value = s?.enabled ?? true
    mode.value = s?.mode ?? 'on'
    temperatureC.value = s?.temperatureC ?? startingTemperature()
    rearDefroster.value = s?.rearDefroster ?? false
    seatLeft.value = s?.seatLeftLevel ?? 0
    seatRight.value = s?.seatRightLevel ?? 0
    conditionOn.value = s !== null && (s.onlyBelowC !== null || s.onlyAboveC !== null)
    colderThan.load(s?.onlyBelowC ?? null)
    warmerThan.load(s?.onlyAboveC ?? null)
  },
)

// Switching the condition on starts from a frosty morning rather than two empty fields.
watch(conditionOn, (on) => {
  if (on && colderThan.metric() === null && warmerThan.metric() === null)
    colderThan.load(DEFAULT_COLDER_THAN_C)
})

const repeatHint = computed(() =>
  days.value.length === 0
    ? t('climateSchedules.form.onceHint')
    : t('climateSchedules.form.repeatsOn', { days: daysSummary(days.value, t) }),
)

const nextRunText = computed(() => {
  const next = props.schedule?.nextRunUtc
  return next
    ? t('climateSchedules.nextRun', { when: runLabel(next, new Date(), t) })
    : t('climateSchedules.notPlanned')
})

const lastRun = computed(() => {
  const s = props.schedule
  if (!s?.lastRunOutcome) return null
  const failed = s.lastRunFailedCommand
  // A run that started climate but not an extra names the extra; any other failure shows the
  // car's own reason.
  const detail =
    s.lastRunOutcome === 'started' && failed
      ? t('climateSchedules.lastRun.extraFailed', { what: commandLabel(failed, t) })
      : s.lastRunDetail
  return {
    label: t(`climateSchedules.outcome.${s.lastRunOutcome}`),
    variant: outcomeVariant(s.lastRunOutcome),
    when: s.lastRunAt ? runLabel(s.lastRunAt, new Date(), t) : null,
    detail,
  }
})

// A schedule keeps the time zone it was made in, so travelling does not move it.
const zoneHint = computed(() => {
  const zone = props.schedule?.timeZoneId
  return zone && zone !== browserTimeZone() ? t('climateSchedules.form.zoneHint', { zone }) : null
})

function close() {
  if (saving.value) return
  emit('close')
}

function validate(): string | null {
  if (!startTime.value) return t('climateSchedules.form.validationTime')
  if (!conditionOn.value) return null
  const below = colderThan.metric()
  const above = warmerThan.metric()
  if (below === null && above === null) return t('climateSchedules.form.validationCondition')
  if (below !== null && above !== null && below >= above)
    return t('errors.codes.climateSchedule.conditionAlwaysTrue')
  return null
}

function buildRequest(): ClimateScheduleRequest {
  return {
    name: name.value.trim() || null,
    enabled: enabled.value,
    startTime: startTime.value,
    days: [...days.value].sort((a, b) => a - b),
    timeZoneId: props.schedule?.timeZoneId ?? browserTimeZone(),
    mode: mode.value,
    temperatureC: temperatureC.value,
    rearDefroster: offersRearDefroster.value && rearDefroster.value === true,
    seatLeftLevel: offersSeatHeating.value ? seatLeft.value : 0,
    seatRightLevel: offersSeatHeating.value ? seatRight.value : 0,
    onlyBelowC: conditionOn.value ? colderThan.metric() : null,
    onlyAboveC: conditionOn.value ? warmerThan.metric() : null,
  }
}

async function submit() {
  validationError.value = validate()
  if (validationError.value) return

  saving.value = true
  try {
    const request = buildRequest()
    if (props.schedule) await store.updateSchedule(props.vin, props.schedule.id, request)
    else await store.createSchedule(props.vin, request)
    if (store.actionError) {
      validationError.value = errorMessage(store.actionError)
      return
    }
    emit('close')
  } finally {
    saving.value = false
  }
}

async function confirmDelete() {
  if (!props.schedule) return
  saving.value = true
  try {
    await store.deleteSchedule(props.vin, props.schedule.id)
    if (store.actionError) {
      validationError.value = errorMessage(store.actionError)
      pendingDelete.value = false
      return
    }
    emit('close')
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <DetailModal :open="open" :title="title" wide @close="close">
    <form @submit.prevent="submit">
      <SettingsSection v-if="isEdit" :title="t('climateSchedules.form.status')">
        <div class="settings-toggles">
          <SettingsToggle
            v-model="enabled"
            input-id="climate-schedule-enabled"
            icon="power-off"
            :label="t('climateSchedules.form.enabled')"
            :desc="nextRunText"
          />
        </div>
        <p v-if="lastRun" class="climate-schedule-last-run text-sm">
          <span class="badge" :class="`badge-${lastRun.variant}`">{{ lastRun.label }}</span>
          <span v-if="lastRun.when" class="text-muted">{{ lastRun.when }}</span>
          <span v-if="lastRun.detail" class="climate-schedule-last-run__detail text-muted">
            {{ lastRun.detail }}
          </span>
        </p>
      </SettingsSection>

      <SettingsSection :title="t('climateSchedules.form.when')">
        <div class="form-stack">
          <div class="form-row">
            <div class="form-group">
              <label for="climate-schedule-time">{{ t('climateSchedules.form.startTime') }}</label>
              <input
                id="climate-schedule-time"
                v-model="startTime"
                type="time"
                class="form-control"
                required
              />
            </div>
            <div class="form-group">
              <label for="climate-schedule-name">{{ t('climateSchedules.form.name') }}</label>
              <input
                id="climate-schedule-name"
                v-model="name"
                type="text"
                class="form-control"
                maxlength="50"
                :placeholder="t('climateSchedules.form.namePlaceholder')"
              />
            </div>
          </div>
          <div class="form-group">
            <span class="form-group__label">{{ t('climateSchedules.form.repeat') }}</span>
            <WeekdayPicker v-model="days" :group-label="t('climateSchedules.form.repeat')" />
            <p class="text-muted text-xs">{{ repeatHint }}</p>
          </div>
        </div>
      </SettingsSection>

      <SettingsSection :title="t('climateSchedules.form.climate')">
        <div class="detail-list">
          <div class="detail-list__item">
            <font-awesome-icon icon="sliders" class="detail-list__item-icon" />
            <ChipGroup
              v-model="mode"
              :options="modeOptions"
              :group-label="t('control.mode.label')"
            />
          </div>
          <RangeControl
            v-if="mode === 'on'"
            v-model="temperatureC"
            icon="temperature-half"
            :label="t('control.temperature')"
            :value-text="temperatureText(temperatureC)"
            :min="CLIMATE_TEMPERATURE_MIN_C"
            :max="CLIMATE_TEMPERATURE_MAX_C"
            :edge-labels="temperatureEdges"
          />
          <DetailListSwitch
            v-if="offersRearDefroster"
            v-model="rearDefroster"
            icon="car-rear"
            :label="t('control.rearDefroster')"
          />
          <template v-if="offersSeatHeating">
            <RangeControl
              v-model="seatLeft"
              icon="couch"
              :label="t('vehicle.climateDetail.seatLeft')"
              :value-text="seatText(seatLeft)"
              :min="0"
              :max="SEAT_LEVEL_MAX"
              :tick-labels="seatLabels"
            />
            <RangeControl
              v-model="seatRight"
              icon="couch"
              :label="t('vehicle.climateDetail.seatRight')"
              :value-text="seatText(seatRight)"
              :min="0"
              :max="SEAT_LEVEL_MAX"
              :tick-labels="seatLabels"
            />
          </template>
        </div>
      </SettingsSection>

      <SettingsSection :title="t('climateSchedules.form.condition')">
        <div class="settings-toggles">
          <SettingsToggle
            v-model="conditionOn"
            input-id="climate-schedule-condition"
            icon="temperature-low"
            :label="t('climateSchedules.form.onlyColdOrHot')"
            :desc="t('climateSchedules.form.onlyColdOrHotDesc')"
          />
        </div>
        <div v-if="conditionOn" class="form-row mt-2">
          <div class="form-group">
            <label for="climate-schedule-below">{{
              t('climateSchedules.form.colderThan', temperatureUnit)
            }}</label>
            <input
              id="climate-schedule-below"
              v-model.number="colderThan.shown"
              type="number"
              step="1"
              class="form-control"
            />
          </div>
          <div class="form-group">
            <label for="climate-schedule-above">{{
              t('climateSchedules.form.warmerThan', temperatureUnit)
            }}</label>
            <input
              id="climate-schedule-above"
              v-model.number="warmerThan.shown"
              type="number"
              step="1"
              class="form-control"
            />
          </div>
        </div>
      </SettingsSection>

      <p class="text-muted text-xs mt-3">{{ t('climateSchedules.form.timingHint') }}</p>
      <p v-if="zoneHint" class="text-muted text-xs">{{ zoneHint }}</p>
      <p v-if="validationError" class="text-danger text-sm" role="alert">{{ validationError }}</p>
    </form>

    <template #footer>
      <template v-if="pendingDelete">
        <span class="text-danger text-sm me-auto">{{ t('climateSchedules.deleteConfirm') }}</span>
        <button class="btn btn-outline-secondary" :disabled="saving" @click="pendingDelete = false">
          {{ t('common.cancel') }}
        </button>
        <button class="btn btn-danger" :disabled="saving" @click="confirmDelete">
          {{ t('common.confirm') }}
        </button>
      </template>
      <template v-else>
        <button
          v-if="isEdit"
          class="btn btn-outline-secondary me-auto"
          :disabled="saving"
          @click="pendingDelete = true"
        >
          <font-awesome-icon icon="trash" />{{ t('climateSchedules.delete') }}
        </button>
        <button class="btn btn-outline-secondary" :disabled="saving" @click="close">
          {{ t('common.cancel') }}
        </button>
        <button class="btn btn-primary" :disabled="saving" @click="submit">
          {{ t('climateSchedules.form.save') }}
        </button>
      </template>
    </template>
  </DetailModal>
</template>
