import { describe, it, expect } from 'vitest'
import { ApiError } from '@/services/apiCore'
import { asError, errorMessageKey } from '@/utils/errors'

const translated = new Set(['errors.codes.maintenance.nameRequired'])
const hasMessage = (key: string) => translated.has(key)

describe('errorMessageKey', () => {
  it('uses the translation for the code the API gave', () => {
    const error = new ApiError(400, '/api/x', { code: 'maintenance.nameRequired' })

    expect(errorMessageKey(error, hasMessage)).toBe('errors.codes.maintenance.nameRequired')
  })

  it('falls back on the status for a code without a translation', () => {
    const error = new ApiError(400, '/api/x', { code: 'map.tooManyPoints' })

    expect(errorMessageKey(error, hasMessage)).toBe('errors.badRequest')
  })

  it.each([
    [400, 'errors.badRequest'],
    [401, 'errors.unauthorized'],
    [403, 'errors.forbidden'],
    [404, 'errors.notFound'],
    [409, 'errors.conflict'],
    [429, 'errors.rateLimited'],
    [500, 'errors.server'],
    [503, 'errors.server'],
    [418, 'errors.unknown'],
  ])('explains a %i', (status, key) => {
    expect(errorMessageKey(new ApiError(status, '/api/x'), hasMessage)).toBe(key)
  })

  it('reads a request that got no answer as being offline', () => {
    expect(errorMessageKey(new TypeError('Failed to fetch'), hasMessage)).toBe('errors.offline')
  })

  it('has a general message for anything else', () => {
    expect(errorMessageKey(new Error('boom'), hasMessage)).toBe('errors.unknown')
  })
})

describe('asError', () => {
  it('keeps an Error and wraps anything else', () => {
    const error = new Error('kept')

    expect(asError(error)).toBe(error)
    expect(asError('thrown text').message).toBe('thrown text')
  })
})
