/** The parts of vue-i18n this needs, so a component passes what useI18n() gave it. */
export interface GatewayLabelI18n {
  t: (key: string) => string
  te: (key: string) => boolean
}

/**
 * The UI's words for a code the gateway sends as an enum name (CHARGE_DONE), looked up under
 * `group`, or the code itself when this version has no words for it, so a value a newer gateway
 * adds still shows something.
 */
export function gatewayLabel(i18n: GatewayLabelI18n, group: string, code: string): string {
  const key = `${group}.${code}`
  return i18n.te(key) ? i18n.t(key) : code
}
