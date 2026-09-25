import { describe, it, expect } from 'vitest'
import { csvFormatFor, toCsv, type CsvFormat } from '../csv'

const comma: CsvFormat = { delimiter: ',', decimalSeparator: '.' }
const semicolon: CsvFormat = { delimiter: ';', decimalSeparator: ',' }

describe('csvFormatFor', () => {
  it('separates fields with semicolons where the comma is the decimal separator', () => {
    expect(csvFormatFor('nl-NL')).toEqual(semicolon)
  })

  it('uses commas where the point is the decimal separator', () => {
    expect(csvFormatFor('en-US')).toEqual(comma)
  })
})

describe('toCsv', () => {
  it('writes one CRLF-terminated line per row', () => {
    expect(
      toCsv(
        [
          ['a', 'b'],
          ['c', 'd'],
        ],
        comma,
      ),
    ).toBe('a,b\r\nc,d\r\n')
  })

  it('writes numbers with the decimal separator of the format', () => {
    expect(toCsv([[24010.5, 3]], comma)).toBe('24010.5,3\r\n')
    expect(toCsv([[24010.5, 3]], semicolon)).toBe('24010,5;3\r\n')
  })

  it('leaves an empty field for a missing value', () => {
    expect(toCsv([['a', null, 'c']], comma)).toBe('a,,c\r\n')
  })

  it('quotes a field holding the delimiter, a quote or a line break', () => {
    expect(toCsv([['Brink 2, Deventer']], comma)).toBe('"Brink 2, Deventer"\r\n')
    expect(toCsv([['Brink 2, Deventer']], semicolon)).toBe('Brink 2, Deventer\r\n')
    expect(toCsv([['a;b']], semicolon)).toBe('"a;b"\r\n')
    expect(toCsv([['say "hi"']], comma)).toBe('"say ""hi"""\r\n')
    expect(toCsv([['two\nlines']], comma)).toBe('"two\nlines"\r\n')
  })

  it('defuses text a spreadsheet would run as a formula', () => {
    expect(toCsv([['=HYPERLINK("x")']], comma)).toBe('"\'=HYPERLINK(""x"")"\r\n')
    expect(toCsv([['+31 6 1234']], comma)).toBe("'+31 6 1234\r\n")
    expect(toCsv([['-rit']], comma)).toBe("'-rit\r\n")
    expect(toCsv([['@home']], comma)).toBe("'@home\r\n")
  })

  it('leaves a negative number a number', () => {
    expect(toCsv([[-1.5]], comma)).toBe('-1.5\r\n')
  })
})
