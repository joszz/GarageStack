<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { useVehicleStore } from '@/stores/vehicle'
import { useNotificationPushSync } from '@/composables/useNotificationPushSync'
import SettingsToggle from '../SettingsToggle.vue'
import {
  notificationCategoryIdsFor,
  type NotificationCategoryId,
} from '@/utils/notificationCategories'

// Push for this device, and the notification types it shows and gets pushed.
const { t } = useI18n()
const settings = useUiSettingsStore()
const vehicleStore = useVehicleStore()
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
  'low-ev': 'lowEv',
  'charging-complete': 'chargingComplete',
  'charging-interrupted': 'chargingInterrupted',
  'engine-start': 'engineStart',
  'unlocked-parked': 'unlockedParked',
  'doors-open-parked': 'doorsOpenParked',
  'windows-open-parked': 'windowsOpenParked',
  'vehicle-message': 'vehicleMessage',
  'climate-schedule': 'climateSchedule',
  'low-12v': 'low12v',
  'low-fuel': 'lowFuel',
  'gateway-login': 'gatewayLogin',
  'data-stale': 'dataStale',
  maintenance: 'maintenance',
}

// Only the types this drivetrain can actually produce: a plain hybrid never charges, so offering
// it a "charging complete" switch is a control that does nothing.
const offeredNotificationTypes = computed(() =>
  notificationCategoryIdsFor(vehicleStore.effectiveVehicleType),
)

const notificationTypeOptions = computed(() =>
  offeredNotificationTypes.value.map((id) => ({
    value: id,
    label: t(`notifications.categories.${NOTIFICATION_CATEGORY_LABEL_KEYS[id]}`),
  })),
)

// Both bulk actions touch only the offered types, leaving an exclusion the user set for a type
// this drivetrain hides untouched: switching the vehicle type back restores their choice rather
// than silently turning that category on.
function selectAllNotificationTypes() {
  const offered = offeredNotificationTypes.value as readonly string[]
  settings.notificationTypeExclusions = settings.notificationTypeExclusions.filter(
    (excludedId) => !offered.includes(excludedId),
  )
}

function deselectAllNotificationTypes() {
  settings.notificationTypeExclusions = [
    ...new Set([...settings.notificationTypeExclusions, ...offeredNotificationTypes.value]),
  ]
}

function toggleNotificationType(id: NotificationCategoryId, checked: boolean) {
  settings.notificationTypeExclusions = checked
    ? settings.notificationTypeExclusions.filter((excludedId) => excludedId !== id)
    : [...settings.notificationTypeExclusions, id]
}
</script>

<template>
  <div v-if="pushSupported" class="settings-toggles">
    <SettingsToggle
      v-model="pushEnabled"
      :label="t('push.enable')"
      :desc="t('push.enableDesc')"
      :disabled="pushSwitchDisabled"
      input-id="settings-toggle-push"
    />
  </div>

  <p v-else-if="needsHttps" class="notif-type-filter__denied text-muted text-sm">
    {{ t('push.needsHttps') }}
  </p>

  <div v-if="showPermissionDeniedNotice" class="notif-type-filter__denied text-danger text-sm">
    {{ t('push.permissionDenied') }}
  </div>

  <div v-else-if="showSubscribeFailedNotice" class="notif-type-filter__denied text-danger text-sm">
    {{ t('push.subscribeFailed') }}
  </div>

  <div class="notif-type-filter">
    <div class="notif-type-filter__header">
      <div class="settings-toggle__info">
        <span class="settings-toggle__label">{{ t('notifications.typeFilter') }}</span>
        <span class="settings-toggle__desc">{{ t('notifications.typeFilterDesc') }}</span>
      </div>
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
          :checked="!settings.notificationTypeExclusions.includes(opt.value)"
          @change="toggleNotificationType(opt.value, ($event.target as HTMLInputElement).checked)"
        />
        <span class="notif-type-checklist__label">{{ opt.label }}</span>
      </label>
    </div>
  </div>
</template>
