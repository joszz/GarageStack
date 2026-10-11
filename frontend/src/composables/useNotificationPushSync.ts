import { computed, watch } from 'vue'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { usePush } from '@/composables/usePush'

// Keeps this device's push subscription in step with its settings. The push switch subscribes and
// unsubscribes; the notification-type checklist only tells the server which types to leave out,
// so ticking a box never brings up a permission prompt or turns push back on.
export function useNotificationPushSync() {
  const settings = useUiSettingsStore()
  const { pushSupported, pushState, pushError, togglePush, syncExclusions } = usePush({
    excludedCategories: () => settings.notificationTypeExclusions,
  })

  const showPermissionDeniedNotice = computed(() => pushSupported && pushState.value === 'denied')
  const showSubscribeFailedNotice = computed(() => pushSupported && pushError.value)

  // Asks for permission the first time it is switched on.
  const pushEnabled = computed({
    get: () => pushState.value === 'subscribed',
    set: (enabled: boolean) => {
      if (enabled !== (pushState.value === 'subscribed')) void togglePush()
    },
  })

  // Nothing to switch while the browser is still being asked what it holds, or once it refused.
  const pushSwitchDisabled = computed(
    () => pushState.value === 'unknown' || pushState.value === 'denied',
  )

  if (pushSupported) {
    // As a sorted string, so only a change in which types are off reaches the server.
    watch(
      () => [...settings.notificationTypeExclusions].sort().join(),
      () => void syncExclusions(),
    )
  }

  return {
    pushSupported,
    pushEnabled,
    pushSwitchDisabled,
    showPermissionDeniedNotice,
    showSubscribeFailedNotice,
  }
}
