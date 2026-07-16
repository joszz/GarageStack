import { computed, watch } from 'vue'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { usePush } from '@/composables/usePush'
import { NOTIFICATION_CATEGORY_IDS } from '@/utils/notificationCategories'

// Keeps the browser push subscription in sync with the notification-type checklist: deselecting
// every type unsubscribes, (re)selecting at least one type resubscribes. Watching the exclusion
// count (rather than the array with `deep: true`) means a plain, non-immediate watch never fires
// on initial mount (no uninvited permission prompt on load), and swapping which single category
// is excluded is a free no-op since the count doesn't change.
export function useNotificationPushSync() {
  const settings = useUiSettingsStore()
  const { pushSupported, pushState, togglePush } = usePush()

  const showPermissionDeniedNotice = computed(() => pushSupported && pushState.value === 'denied')

  if (pushSupported) {
    watch(
      () => settings.notificationTypeExclusions.length,
      (exclusionCount) => {
        if (pushState.value === 'denied') return
        const shouldBeSubscribed = exclusionCount < NOTIFICATION_CATEGORY_IDS.length
        const isSubscribed = pushState.value === 'subscribed'
        if (shouldBeSubscribed !== isSubscribed) togglePush()
      },
    )
  }

  return { showPermissionDeniedNotice }
}
