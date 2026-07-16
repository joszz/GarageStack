// Centralizes numeric-to-string formatting so every display value goes through one function.
// Today this just replaces ~20 duplicated toFixed() call sites; it's also a single place to
// switch to locale-aware formatting (Intl.NumberFormat) later, since toFixed() always renders
// with a '.' decimal separator regardless of the active locale (en/nl).
export function formatNumber(value: number, decimals = 1): string {
  return value.toFixed(decimals)
}
