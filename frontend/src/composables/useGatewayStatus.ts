import { readonly, ref } from 'vue'
import { gatewayApi, type GatewayStatus } from '@/services/gatewayApi'

// One answer for the whole app: the sidebar shows it, the demo panel changes it.
const signedIn = ref<boolean | null>(null)

/**
 * Whether the gateway is signed in to the MG account. When it is not, no car sends anything, and
 * the app says so instead of only calling the car offline. Asked when live updates start and after
 * they reconnect; changes in between arrive over SignalR and go in through {@link apply}.
 */
export function useGatewayStatus() {
  async function refresh() {
    try {
      apply(await gatewayApi.get())
    } catch {
      // Offline, or an API older than the endpoint: keep what is known, which says nothing new.
    }
  }

  function apply(status: GatewayStatus) {
    signedIn.value = status.signedIn
  }

  return { signedIn: readonly(signedIn), refresh, apply }
}
