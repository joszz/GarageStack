import { numberLocale } from '@/utils/format'
import type { Region } from '@/utils/region'

// Costs are kept as plain amounts, like every measurement is kept in metric: the currency is how
// this browser shows them, so changing it relabels the amounts and never converts them.

/** The currencies a cost can be shown in: those of the countries MG sells its cars in. */
export const CURRENCIES = [
  'EUR',
  'GBP',
  'USD',
  'AUD',
  'NZD',
  'CAD',
  'BRL',
  'CHF',
  'NOK',
  'SEK',
  'DKK',
  'PLN',
  'CZK',
  'HUF',
  'RON',
  'ISK',
  'THB',
  'MXN',
  'CLP',
  'ZAR',
] as const

export type Currency = (typeof CURRENCIES)[number]

// Where each currency other than the euro is the one in people's wallets. Every region left out,
// the euro area among them, falls back to the euro: MG sells most of its cars in Europe.
const REGION_CURRENCY: Readonly<Record<Region, Currency>> = {
  GB: 'GBP',
  GG: 'GBP',
  IM: 'GBP',
  JE: 'GBP',
  US: 'USD',
  AS: 'USD',
  GU: 'USD',
  MP: 'USD',
  PR: 'USD',
  UM: 'USD',
  VI: 'USD',
  AU: 'AUD',
  NZ: 'NZD',
  CA: 'CAD',
  BR: 'BRL',
  CH: 'CHF',
  LI: 'CHF',
  NO: 'NOK',
  SE: 'SEK',
  DK: 'DKK',
  PL: 'PLN',
  CZ: 'CZK',
  HU: 'HUF',
  RO: 'RON',
  IS: 'ISK',
  TH: 'THB',
  MX: 'MXN',
  CL: 'CLP',
  ZA: 'ZAR',
}

/** The currency a region pays in, which an "auto" setting stands for. */
export function regionCurrency(region: Region): Currency {
  return REGION_CURRENCY[region] ?? 'EUR'
}

export function isCurrency(value: unknown): value is Currency {
  return (CURRENCIES as readonly unknown[]).includes(value)
}

/** "€ 89,95" in Dutch, "€89.95" in English: an amount in the interface language's notation. */
export function formatCurrency(amount: number, currency: Currency): string {
  return amount.toLocaleString(numberLocale(), { style: 'currency', currency })
}

/** The currency's sign in the interface language, such as "€" or "£", for a field's label. */
export function currencySymbol(currency: Currency): string {
  return (
    new Intl.NumberFormat(numberLocale(), { style: 'currency', currency })
      .formatToParts(0)
      .find((part) => part.type === 'currency')?.value ?? currency
  )
}
