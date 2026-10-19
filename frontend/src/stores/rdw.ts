import { defineStore } from 'pinia'
import { computed, ref, shallowRef } from 'vue'
import { ApiError } from '@/services/apiCore'
import { RDW_DISABLED_CODE, rdwApi, type RdwStatus } from '@/services/rdwApi'

/**
 * The car's Dutch registration and what the RDW says about it. Whether the deployment looks
 * plates up at all is only known from the API's answer: switched off, it says so with
 * rdw.disabled, and then the plate field, the card and the notification type all stay out of
 * sight, the way the map leaves out a speed camera layer the deployment does not serve.
 */
export const useRdwStore = defineStore('rdw', () => {
  /** Null until the API has answered. */
  const available = ref<boolean | null>(null)
  const status = shallowRef<RdwStatus | null>(null)
  const error = ref<Error | null>(null)
  let inFlight: Promise<void> | null = null

  /** Reads the plate and the lookup again. Once the deployment said it is off, it is not asked again. */
  async function fetchStatus(vin: string) {
    if (available.value === false) return
    if (inFlight) return inFlight
    inFlight = (async () => {
      try {
        status.value = await rdwApi.get(vin)
        available.value = true
        error.value = null
      } catch (e) {
        if (e instanceof ApiError && e.code === RDW_DISABLED_CODE) {
          available.value = false
          status.value = null
        } else {
          error.value = e as Error
        }
      } finally {
        inFlight = null
      }
    })()
    return inFlight
  }

  /** Reads the status the first time anything needs it. */
  async function ensureLoaded(vin: string) {
    if (available.value === null && !status.value) await fetchStatus(vin)
  }

  /** Saves the plate as typed (blank removes it); the API normalises it and looks it up. Throws when refused. */
  async function setLicensePlate(vin: string, plate: string) {
    status.value = await rdwApi.setLicensePlate(vin, plate.trim() || null)
    available.value = true
  }

  /** Whether the RDW card has anything to show: the feature is on and a plate is set. */
  const hasPlate = computed(() => available.value === true && !!status.value?.licensePlate)

  return { available, status, error, hasPlate, fetchStatus, ensureLoaded, setLicensePlate }
})
