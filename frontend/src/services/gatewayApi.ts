import { request } from '@/services/apiCore'

/**
 * Whether the SAIC gateway is signed in to the MG account: false after a failed sign-in until the
 * next good one, null while it has not said.
 */
export interface GatewayStatus {
  signedIn: boolean | null
}

export const gatewayApi = {
  get: () => request<GatewayStatus>('/api/gateway'),
}
