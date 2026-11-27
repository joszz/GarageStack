<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { useVehicleStore } from '@/stores/vehicle'
import { useRdwStore } from '@/stores/rdw'
import { useNotificationPushSync } from '@/composables/useNotificationPushSync'
import SettingsToggle from '../SettingsToggle.vue'
import SettingsSelect, { type SettingsSelectOption } from '../SettingsSelect.vue'
import {
  isCategoryShown,
  isOptIn,
  isPerCarCategory,
  NOTIFICATION_CATEGORY_IDS,
  notificationCategoryIdsFor,
  type NotificationCategoryId,
} from '@/utils/notificationCategories'
import { vehicleLabels } from '@/utils/vehicleLabels'
import type { Vehicle } from '@/services/vehicleApi'

// Push for this device, and the notification types it shows and gets pushed.
const { t } = useI18n()
const settings = useUiSettingsStore()
const vehicleStore = useVehicleStore()
const rdw = useRdwStore()
const {
  pushSupported,
  pushEnabled,
  pushSwitchDisabled,
  showPermissionDeniedNotice,
  showSubscribeFailedNotice,
} = useNotificationPushSync()

// Browsers only offer push (and the service worker behind it) to secure pages: https, or
// localhost. On a plain-HTTP LAN address the switch would simply be missing, so say why instead.
const needsHttps = !pushSupported && !window.isSecureContext

const NOTIFICATION_CATEGORY_LABEL_KEYS: Record<NotificationCategoryId, string> = {
  'low-tyre': 'lowTyre',
  'high-tyre': 'highTyre',
  'slow-puncture': 'slowPuncture',
  'low-ev': 'lowEv',
  'charging-complete': 'chargingComplete',
  'charging-interrupted': 'chargingInterrupted',
  'engine-start': 'engineStart',
  'unlocked-parked': 'unlockedParked',
  'doors-open-parked': 'doorsOpenParked',
  'windows-open-parked': 'windowsOpenParked',
  'moved-parked': 'movedParked',
  'vehicle-message': 'vehicleMessage',
  'climate-schedule': 'climateSchedule',
  'low-12v': 'low12v',
  'low-fuel': 'lowFuel',
  'gateway-login': 'gatewayLogin',
  'data-stale': 'dataStale',
  'place-arrival': 'placeArrival',
  'place-departure': 'placeDeparture',
  maintenance: 'maintenance',
  rdw: 'rdw',
  'weekly-summary': 'weeklySummary',
  'monthly-summary': 'monthlySummary',
}

// The list below is for every car, or, with more than one car on the account, for one of them:
// 'all' or a vehicle id as text. A type switched off for all cars stays off for each of them,
// so a car's own list only ever switches more off (NotificationCategories.IsWanted).
const ALL_CARS = 'all'
const scope = ref(ALL_CARS)
const scopeVehicleId = computed(() => (scope.value === ALL_CARS ? null : scope.value))

const scopeOptions = computed((): SettingsSelectOption<string>[] => [
  { value: ALL_CARS, label: t('notifications.scopeAll') },
  ...vehicleLabels(vehicleStore.vehicles).map((car) => ({
    value: String(car.id),
    label: car.label,
  })),
])

// A car that left the account leaves nothing to pick: fall back to every car.
watch(
  () => vehicleStore.vehicles,
  (cars) => {
    if (scopeVehicleId.value !== null && !cars.some((car) => String(car.id) === scope.value))
      scope.value = ALL_CARS
  },
)

function typeOf(car: Vehicle) {
  return car.vehicleTypeOverride ?? car.vehicleType
}

// Only the types this drivetrain can actually produce: a plain hybrid never charges, so offering
// it a "charging complete" switch is a control that does nothing. The RDW's only where the
// deployment looks plates up. For every car at once that is what any of them can produce, and for
// one car, what that one can.
const offeredNotificationTypes = computed(() => {
  const cars = vehicleStore.vehicles
  const shown = scopeVehicleId.value
  const own = cars.find((car) => String(car.id) === shown)
  let offered: readonly NotificationCategoryId[]
  if (own) offered = notificationCategoryIdsFor(typeOf(own)).filter(isPerCarCategory)
  else if (cars.length > 1) {
    const union = new Set(cars.flatMap((car) => notificationCategoryIdsFor(typeOf(car))))
    offered = NOTIFICATION_CATEGORY_IDS.filter((id) => union.has(id))
  } else offered = notificationCategoryIdsFor(vehicleStore.effectiveVehicleType)
  return offered.filter((id) => id !== 'rdw' || rdw.available === true)
})

const notificationTypeOptions = computed(() =>
  offeredNotificationTypes.value.map((id) => ({
    value: id,
    label: t(`notifications.categories.${NOTIFICATION_CATEGORY_LABEL_KEYS[id]}`),
  })),
)

// Both bulk actions touch only the offered alert types, leaving an exclusion the user set for a
// type this drivetrain hides untouched: switching the vehicle type back restores their choice
// rather than silently turning that category on. The summaries are left as they are too: they
// are something to ask for, not an alert, and "all on" would otherwise start a weekly push.
const offeredAlertTypes = computed(() =>
  offeredNotificationTypes.value.filter((id) => !isOptIn(id)),
)

// What the picked car has off on top of the device's own list; nothing while the list is for all cars.
const carExclusions = computed((): readonly string[] =>
  scopeVehicleId.value === null
    ? []
    : (settings.notificationTypeExclusionsByVehicle[scopeVehicleId.value] ?? []),
)

// A car with nothing off has no entry, the same as a car this device never chose for.
function setCarExclusions(vehicleId: string, types: readonly string[]) {
  const others = Object.entries(settings.notificationTypeExclusionsByVehicle).filter(
    ([id]) => id !== vehicleId,
  )
  settings.notificationTypeExclusionsByVehicle = Object.fromEntries(
    types.length > 0 ? [...others, [vehicleId, [...types]]] : others,
  )
}

function selectAllNotificationTypes() {
  const offered = offeredAlertTypes.value as readonly string[]
  if (scopeVehicleId.value !== null) {
    setCarExclusions(
      scopeVehicleId.value,
      carExclusions.value.filter((excludedId) => !offered.includes(excludedId)),
    )
    return
  }
  settings.notificationTypeExclusions = settings.notificationTypeExclusions.filter(
    (excludedId) => !offered.includes(excludedId),
  )
}

function deselectAllNotificationTypes() {
  if (scopeVehicleId.value !== null) {
    setCarExclusions(scopeVehicleId.value, [
      ...new Set([...carExclusions.value, ...offeredAlertTypes.value]),
    ])
    return
  }
  settings.notificationTypeExclusions = [
    ...new Set([...settings.notificationTypeExclusions, ...offeredAlertTypes.value]),
  ]
}

function without(list: readonly string[], id: string): string[] {
  return list.filter((other) => other !== id)
}

// For all cars, an opt-in type is switched on by being in the inclusions, any other off by being
// in the exclusions. For one car every type, an opt-in one too, is switched off by being in that
// car's list: the summary itself is still asked for once, for the device.
function toggleNotificationType(id: NotificationCategoryId, checked: boolean) {
  if (scopeVehicleId.value !== null) {
    setCarExclusions(
      scopeVehicleId.value,
      checked ? without(carExclusions.value, id) : [...without(carExclusions.value, id), id],
    )
    return
  }
  if (isOptIn(id)) {
    settings.notificationTypeInclusions = checked
      ? [...without(settings.notificationTypeInclusions, id), id]
      : without(settings.notificationTypeInclusions, id)
    return
  }
  settings.notificationTypeExclusions = checked
    ? without(settings.notificationTypeExclusions, id)
    : [...settings.notificationTypeExclusions, id]
}

// A type switched off for all cars cannot be switched on for one car, so its row for a car is
// shown off and cannot be changed, and says why.
const isOffForAllCars = (id: NotificationCategoryId) =>
  scopeVehicleId.value !== null &&
  !isCategoryShown(id, settings.notificationTypeExclusions, settings.notificationTypeInclusions)

const isShown = (id: NotificationCategoryId) =>
  isCategoryShown(
    id,
    settings.notificationTypeExclusions,
    settings.notificationTypeInclusions,
    carExclusions.value,
  )
</script>

<template>
  <div class="settings-toggles">
    <SettingsToggle
      v-if="pushSupported"
      v-model="pushEnabled"
      :label="t('push.enable')"
      :desc="t('push.enableDesc')"
      :disabled="pushSwitchDisabled"
      input-id="settings-toggle-push"
    >
      <template #note>
        <p v-if="showPermissionDeniedNotice" class="text-danger text-sm" role="alert">
          {{ t('push.permissionDenied') }}
        </p>
        <p v-else-if="showSubscribeFailedNotice" class="text-danger text-sm" role="alert">
          {{ t('push.subscribeFailed') }}
        </p>
      </template>
    </SettingsToggle>

    <!-- No switch to offer on a plain-HTTP address: the row says why instead. -->
    <SettingsToggle v-else-if="needsHttps" :label="t('push.enable')" :desc="t('push.needsHttps')">
      <template #control><span /></template>
    </SettingsToggle>

    <!-- With more than one car, the list below is for all of them or for one. -->
    <SettingsSelect
      v-if="vehicleStore.hasMultipleVehicles"
      id="settings-notification-scope"
      v-model="scope"
      :label="t('notifications.scope')"
      :desc="t('notifications.scopeDesc')"
      :options="scopeOptions"
    />

    <SettingsToggle
      class="settings-toggle--stacked"
      :label="t('notifications.typeFilter')"
      :desc="t('notifications.typeFilterDesc')"
    >
      <template #control>
        <div class="notif-type-filter__actions">
          <button
            type="button"
            class="btn btn-sm btn-outline-secondary"
            @click="selectAllNotificationTypes"
          >
            {{ t('notifications.selectAll') }}
          </button>
          <button
            type="button"
            class="btn btn-sm btn-outline-secondary"
            @click="deselectAllNotificationTypes"
          >
            {{ t('notifications.deselectAll') }}
          </button>
        </div>

        <div class="notif-type-checklist">
          <label
            v-for="opt in notificationTypeOptions"
            :key="opt.value"
            class="form-check form-switch notif-type-checklist__item"
            :class="{ 'notif-type-checklist__item--locked': isOffForAllCars(opt.value) }"
          >
            <input
              class="form-check-input"
              type="checkbox"
              role="switch"
              :checked="isShown(opt.value)"
              :disabled="isOffForAllCars(opt.value)"
              @change="
                toggleNotificationType(opt.value, ($event.target as HTMLInputElement).checked)
              "
            />
            <span class="notif-type-checklist__label">
              {{ opt.label }}
              <span
                v-if="isOffForAllCars(opt.value)"
                class="notif-type-checklist__note text-muted text-sm"
              >
                {{ t('notifications.offForAllCars') }}
              </span>
            </span>
          </label>
        </div>
      </template>
    </SettingsToggle>
  </div>
</template>
