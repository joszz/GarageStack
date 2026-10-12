import { describe, it, expect } from 'vitest'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import { gatewayLabel } from '@/utils/gatewayLabel'

const { global: i18n } = createI18n({ legacy: false, locale: 'en', messages: { en } })

describe('gatewayLabel', () => {
  it('words a code this version knows', () => {
    expect(gatewayLabel(i18n, 'vehicle.chargingSession.bmsStatuses', 'CHARGE_DONE')).toBe(
      'Charge complete',
    )
  })

  // A newer gateway can add a value; showing its code beats showing nothing.
  it('falls back to the code itself for one it does not know', () => {
    expect(gatewayLabel(i18n, 'vehicle.chargingSession.bmsStatuses', 'UNKNOWN 42')).toBe(
      'UNKNOWN 42',
    )
  })
})
