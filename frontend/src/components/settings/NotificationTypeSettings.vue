<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { useVehicleStore } from '@/stores/vehicle'
import { useNotificationPushSync } from '@/composables/useNotificationPushSync'
import {
  notificationCategoryIdsFor,
  type NotificationCategoryId,
} from '@/utils/notificationCategories'

// The notification types to show and to be pushed. This also drives the push subscription:
// deselecting every type unsubscribes, selecting at least one (re)subscribes.
const { t } = useI18n()
const settings = useUiSettingsStore()
const vehicleStore = useVehicleStore()
const { showPermissionDeniedNotice, showSubscribeFailedNotice } = useNotificationPushSync()

const NOTIFICATION_CATEGORY_LABEL_KEYS: Record<NotificationCategoryId, string> = {
  'low-tyre': 'lowTyre',
  'high-tyre': 'highTyre',
  'low-ev': 'lowEv',
  'charging-complete': 'chargingComplete',
  'engine-start': 'engineStart',
  'unlocked-parked': 'unlockedParked',
  'doors-open-parked': 'doorsOpenParked',
  'windows-open-parked': 'windowsOpenParked',
  'vehicle-message': 'vehicleMessage',
  'climate-schedule': 'climateSchedule',
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

    <div v-if="showPermissionDeniedNotice" class="notif-type-filter__denied text-danger text-sm">
      {{ t('push.permissionDenied') }}
    </div>

    <div
      v-else-if="showSubscribeFailedNotice"
      class="notif-type-filter__denied text-danger text-sm"
    >
      {{ t('push.subscribeFailed') }}
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
