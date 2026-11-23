<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { useVehicleStore } from '@/stores/vehicle'
import { useRdwStore } from '@/stores/rdw'
import { useNotificationPushSync } from '@/composables/useNotificationPushSync'
import SettingsToggle from '../SettingsToggle.vue'
import {
  isCategoryShown,
  isOptIn,
  notificationCategoryIdsFor,
  type NotificationCategoryId,
} from '@/utils/notificationCategories'

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

// Only the types this drivetrain can actually produce: a plain hybrid never charges, so offering
// it a "charging complete" switch is a control that does nothing. The RDW's only where the
// deployment looks plates up.
const offeredNotificationTypes = computed(() =>
  notificationCategoryIdsFor(vehicleStore.effectiveVehicleType).filter(
    (id) => id !== 'rdw' || rdw.available === true,
  ),
)

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

function selectAllNotificationTypes() {
  const offered = offeredAlertTypes.value as readonly string[]
  settings.notificationTypeExclusions = settings.notificationTypeExclusions.filter(
    (excludedId) => !offered.includes(excludedId),
  )
}

function deselectAllNotificationTypes() {
  settings.notificationTypeExclusions = [
    ...new Set([...settings.notificationTypeExclusions, ...offeredAlertTypes.value]),
  ]
}

function without(list: readonly string[], id: string): string[] {
  return list.filter((other) => other !== id)
}

// An opt-in type is switched on by being in the inclusions, any other off by being in the exclusions.
function toggleNotificationType(id: NotificationCategoryId, checked: boolean) {
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

const isShown = (id: NotificationCategoryId) =>
  isCategoryShown(id, settings.notificationTypeExclusions, settings.notificationTypeInclusions)
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
          >
            <input
              class="form-check-input"
              type="checkbox"
              role="switch"
              :checked="isShown(opt.value)"
              @change="
                toggleNotificationType(opt.value, ($event.target as HTMLInputElement).checked)
              "
            />
            <span class="notif-type-checklist__label">{{ opt.label }}</span>
          </label>
        </div>
      </template>
    </SettingsToggle>
  </div>
</template>
