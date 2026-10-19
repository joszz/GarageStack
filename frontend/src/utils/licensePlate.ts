/** As the API's LicensePlate.MaxLength: letters and digits, without the dashes. */
export const LICENSE_PLATE_MAX_CHARACTERS = 8

const PLATE_CHARACTER = /[A-Z0-9]/

/** The letters and digits of a plate as the RDW keys it: "ab-123-c" is "AB123C". */
export function normalizePlate(text: string): string {
  return [...text.toUpperCase()]
    .filter((c) => PLATE_CHARACTER.test(c))
    .join('')
    .slice(0, LICENSE_PLATE_MAX_CHARACTERS)
}

/**
 * A plate written the way it is on the car, in the Dutch side codes: a dash wherever letters
 * change to digits or back (99-XXX-9, XXX-99-X, 9-XX-999, ...), and a group of four split in two,
 * since no side code has one (XX-99-99, 99-XX-XX). Works on a plate still being typed: "AB123"
 * reads "AB-123" until a fourth digit turns that group into two pairs, "AB-12-34".
 */
export function formatPlate(text: string): string {
  const plate = normalizePlate(text)
  const groups: string[] = []
  for (const c of plate) {
    const last = groups[groups.length - 1]
    if (last && isDigit(last[last.length - 1]!) === isDigit(c)) groups[groups.length - 1] = last + c
    else groups.push(c)
  }
  return groups.flatMap((g) => (g.length === 4 ? [g.slice(0, 2), g.slice(2)] : [g])).join('-')
}

/** Where the caret goes in `formatted` to sit after its first `count` letters and digits. */
export function caretAfter(formatted: string, count: number): number {
  if (count <= 0) return 0
  let seen = 0
  for (let i = 0; i < formatted.length; i++) {
    if (PLATE_CHARACTER.test(formatted[i]!) && ++seen === count) return i + 1
  }
  return formatted.length
}

/** How many plate characters `text` holds: what a caret position means once the dashes move. */
export function countPlateCharacters(text: string): number {
  return [...text.toUpperCase()].filter((c) => PLATE_CHARACTER.test(c)).length
}

function isDigit(c: string): boolean {
  return c >= '0' && c <= '9'
}
