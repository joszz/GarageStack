/**
 * Writing CSV that a spreadsheet opens as intended: quoted where needed (RFC 4180), with the
 * separators the reader's locale expects, and with text that cannot run as a formula.
 */

export type CsvCell = string | number | null

export interface CsvFormat {
  delimiter: ',' | ';'
  decimalSeparator: '.' | ','
}

/**
 * The conventions a spreadsheet in this locale reads CSV with. Where the comma is the decimal
 * separator (Dutch, German), it cannot also separate fields, and spreadsheets there expect a
 * semicolon instead.
 */
export function csvFormatFor(locale: string): CsvFormat {
  const decimal = new Intl.NumberFormat(locale)
    .formatToParts(1.5)
    .find((part) => part.type === 'decimal')?.value
  return decimal === ','
    ? { delimiter: ';', decimalSeparator: ',' }
    : { delimiter: ',', decimalSeparator: '.' }
}

// A spreadsheet runs a cell starting with one of these as a formula. Text from outside the app
// (an OpenStreetMap address, a note someone typed) is defused with a leading apostrophe, which
// spreadsheets show as plain text.
const FORMULA_START = /^[=+\-@\t\r]/

function field(cell: CsvCell, format: CsvFormat): string {
  if (cell === null) return ''
  if (typeof cell === 'number') return String(cell).replace('.', format.decimalSeparator)
  return FORMULA_START.test(cell) ? `'${cell}` : cell
}

function quoted(value: string, delimiter: string): string {
  return value.includes(delimiter) || /["\r\n]/.test(value)
    ? `"${value.replace(/"/g, '""')}"`
    : value
}

/** The rows as CSV text, one line each, ending in CRLF as RFC 4180 has it. */
export function toCsv(rows: readonly (readonly CsvCell[])[], format: CsvFormat): string {
  return rows
    .map((row) =>
      row.map((cell) => quoted(field(cell, format), format.delimiter)).join(format.delimiter),
    )
    .map((line) => `${line}\r\n`)
    .join('')
}
