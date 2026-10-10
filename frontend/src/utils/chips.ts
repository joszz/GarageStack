/** One choice in a ChipGroup. */
export interface ChipOption<T extends string | number> {
  value: T
  label: string
  /** FontAwesome icon shown before the label. */
  icon?: string
  /** Extra class on this chip, e.g. a modifier that sets its own --chip-color. */
  class?: string
}
