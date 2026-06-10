import { ref, onMounted, onUnmounted } from 'vue'

const STORAGE_KEY = 'garagestack-pwa-install-dismissed'

interface BeforeInstallPromptEvent extends Event {
  prompt(): Promise<void>
  readonly userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>
}

const promptEvent = ref<BeforeInstallPromptEvent | null>(null)
const modalVisible = ref(false)

function isInstalled(): boolean {
  return (
    window.matchMedia('(display-mode: standalone)').matches ||
    ('standalone' in window.navigator &&
      (window.navigator as Navigator & { standalone: boolean }).standalone === true)
  )
}

function isDismissedForever(): boolean {
  try {
    return localStorage.getItem(STORAGE_KEY) === 'never'
  } catch {
    return false
  }
}

function onBeforeInstallPrompt(e: Event) {
  e.preventDefault()
  if (isInstalled() || isDismissedForever()) return
  promptEvent.value = e as BeforeInstallPromptEvent
  modalVisible.value = true
}

export function usePwaInstall() {
  onMounted(() => {
    window.addEventListener('beforeinstallprompt', onBeforeInstallPrompt)
  })

  onUnmounted(() => {
    window.removeEventListener('beforeinstallprompt', onBeforeInstallPrompt)
  })

  async function install() {
    if (!promptEvent.value) return
    await promptEvent.value.prompt()
    const { outcome } = await promptEvent.value.userChoice
    promptEvent.value = null
    modalVisible.value = false
    if (outcome === 'accepted') {
      try {
        localStorage.setItem(STORAGE_KEY, 'never')
      } catch {
        // ignore
      }
    }
  }

  function dismiss(neverShow: boolean) {
    modalVisible.value = false
    promptEvent.value = null
    if (neverShow) {
      try {
        localStorage.setItem(STORAGE_KEY, 'never')
      } catch {
        // ignore
      }
    }
  }

  return {
    modalVisible,
    canInstall: promptEvent,
    install,
    dismiss,
  }
}
