import { requestJson } from './apiCore'

/** One position from the phone, as the API takes it. */
export interface PhonePosition {
  recordedAt: string
  latitude: number
  longitude: number
  accuracyM: number | null
  speedKmh: number | null
}

/** The most positions one request may carry. */
export const MAX_BATCH = 500

export const phonePositionsApi = {
  /** Sends a batch; the answer says how many were new, since a batch sent again stores none. */
  upload: (vin: string, trackerId: string, positions: PhonePosition[]) =>
    requestJson<{ stored: number }>(
      `/api/vehicles/${encodeURIComponent(vin)}/phone-positions`,
      'POST',
      { trackerId, positions },
    ),
}
