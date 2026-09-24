import { computed, watch } from 'vue'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { useVehicleStore } from '@/stores/vehicle'
import { usePush } from '@/composables/usePush'
import { notificationCategoryIdsFor } from '@/utils/notificationCategories'

// Keeps the browser push subscription in sync with the notification-type checklist: deselecting
// every type unsubscribes, (re)selecting at least one type resubscribes. Watching the exclusion
// count (rather than the array with `deep: true`) means a plain, non-immediate watch never fires
// on initial mount (no uninvited permission prompt on load), and swapping which single category
// is excluded is a free no-op since the count doesn't change.
export function useNotificationPushSync() {
  const settings = useUiSettingsStore()
  const vehicleStore = useVehicleStore()
  const { pushSupported, pushState, pushError, togglePush } = usePush()

  const showPermissionDeniedNotice = computed(() => pushSupported && pushState.value === 'denied')
  const showSubscribeFailedNotice = computed(() => pushSupported && pushError.value)

  // "Every type deselected" is measured against the types the checklist offers for this
  // drivetrain, not the full category list: a hybrid has no charging switches to turn off, so
  // waiting for all of them would leave it subscribed however much the user deselects.
  function allOfferedTypesExcluded() {
    const excluded = settings.notificationTypeExclusions
    return notificationCategoryIdsFor(vehicleStore.effectiveVehicleType).every((id) =>
      excluded.includes(id),
    )
  }

  if (pushSupported) {
    watch(
      () => settings.notificationTypeExclusions.length,
      () => {
        if (pushState.value === 'denied') return
        const shouldBeSubscribed = !allOfferedTypesExcluded()
        const isSubscribed = pushState.value === 'subscribed'
        if (shouldBeSubscribed !== isSubscribed) togglePush()
      },
    )
  }

  return { showPermissionDeniedNotice, showSubscribeFailedNotice }
}
