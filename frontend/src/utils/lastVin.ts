const LAST_VIN_KEY = 'garagestack-last-vin'

/**
 * The VIN of the car this browser last had the vehicle list for. A page that has just loaded can
 * ask for that car's status alongside the list instead of after it, since the list nearly always
 * names the same car again. Only a guess: what the list says wins.
 */
export function readLastVin(): string | null {
  return localStorage.getItem(LAST_VIN_KEY)
}

export function rememberVin(vin: string): void {
  localStorage.setItem(LAST_VIN_KEY, vin)
}

/** Forgotten on sign-out, with the rest of the session, so the next account starts clean. */
export function forgetLastVin(): void {
  localStorage.removeItem(LAST_VIN_KEY)
}
