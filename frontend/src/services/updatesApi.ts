import { request } from '@/services/apiCore'

/** Whether a newer GarageStack release is out, as the API's daily check last found. */
export interface UpdateInfo {
  updateAvailable: boolean
  latestVersion: string | null
  releaseUrl: string | null
}

export const updatesApi = {
  get: () => request<UpdateInfo>('/api/updates'),
}
