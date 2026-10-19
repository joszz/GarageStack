import { request, requestJson } from '@/services/apiCore'

/** The code both RDW endpoints answer with while the deployment has RDW lookups switched off. */
export const RDW_DISABLED_CODE = 'rdw.disabled'

/** Room for a plate as it is written, dashes and spaces included; the API keeps eight characters. */
export const LICENSE_PLATE_INPUT_MAX_LENGTH = 12

/** A recall on the plate. The texts are the RDW's own, in Dutch. */
export interface RdwRecall {
  code: string
  open: boolean
  defect: string | null
  /** What can happen because of the defect. */
  risk: string | null
  repair: string | null
  /** The RDW's rating of the risk: ERN (serious), GEM (moderate) or NNB (not rated yet). */
  severity: string | null
  firstSeenAt: string
}

/** What the RDW last said about the plate. */
export interface RdwInfo {
  /** False when the RDW knows no vehicle by the plate. */
  found: boolean
  make: string | null
  model: string | null
  /** The APK's last valid day, as "YYYY-MM-DD" on the Dutch calendar. */
  apkExpiresOn: string | null
  /** Counted by the API on the Dutch calendar: 0 on the last day, negative once it passed. */
  apkDaysLeft: number | null
  wamInsured: boolean | null
  openRecallIndicator: boolean | null
  recalls: RdwRecall[]
  fetchedAt: string | null
  /** Why the attempt after fetchedAt failed, in English, for the console. */
  lastError: string | null
}

export interface RdwStatus {
  licensePlate: string | null
  info: RdwInfo | null
}

export const rdwApi = {
  get: (vin: string) => request<RdwStatus>(`/api/vehicles/${vin}/rdw`),
  /** Saves the plate (null removes it) and answers with what the RDW said about it right away. */
  setLicensePlate: (vin: string, licensePlate: string | null) =>
    requestJson<RdwStatus>(`/api/vehicles/${vin}/license-plate`, 'PUT', { licensePlate }),
}
