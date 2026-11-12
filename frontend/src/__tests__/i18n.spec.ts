import { describe, it, expect, afterEach, vi } from 'vitest'
import { nextTick } from 'vue'
import { i18n } from '@/i18n'

/** A fresh i18n module, so each test starts with only English loaded. */
async function freshI18n() {
  vi.resetModules()
  return import('@/i18n')
}

describe('i18n', () => {
  afterEach(() => {
    i18n.global.locale.value = 'en'
    vi.doUnmock('@/locales/nl.json')
    vi.doUnmock('@/locales/es.json')
    vi.doUnmock('@/locales/pt.json')
    vi.restoreAllMocks()
  })

  it('names the interface language on the page', async () => {
    expect(document.documentElement.lang).toBe('en')

    i18n.global.locale.value = 'nl'
    await nextTick()

    expect(document.documentElement.lang).toBe('nl')
  })

  it('ships English only, and fetches Dutch the first time it is chosen', async () => {
    const fresh = await freshI18n()
    expect(fresh.i18n.global.availableLocales).toEqual(['en'])

    await fresh.setLocale('nl')

    expect(fresh.i18n.global.locale.value).toBe('nl')
    expect(fresh.i18n.global.t('notifications.title')).toBe('Meldingen')
  })

  it('fetches Spanish the first time it is chosen', async () => {
    const fresh = await freshI18n()
    expect(fresh.i18n.global.availableLocales).toEqual(['en'])

    await fresh.setLocale('es')

    expect(fresh.i18n.global.locale.value).toBe('es')
    expect(fresh.i18n.global.t('notifications.title')).toBe('Notificaciones')
  })

  it('fetches Portuguese the first time it is chosen', async () => {
    const fresh = await freshI18n()
    expect(fresh.i18n.global.availableLocales).toEqual(['en'])

    await fresh.setLocale('pt')

    expect(fresh.i18n.global.locale.value).toBe('pt')
    expect(fresh.i18n.global.t('notifications.title')).toBe('Notificações')
  })

  it('keeps the language chosen last when an earlier download finishes after it', async () => {
    const fresh = await freshI18n()

    const dutch = fresh.setLocale('nl')
    await fresh.setLocale('en')
    await dutch

    expect(fresh.i18n.global.locale.value).toBe('en')
    // Downloaded all the same, so choosing it again is instant.
    expect(fresh.i18n.global.availableLocales).toContain('nl')
  })

  it('stays in the current language when the messages cannot be fetched', async () => {
    vi.doMock('@/locales/nl.json', () => {
      throw new Error('offline')
    })
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    const fresh = await freshI18n()

    await fresh.setLocale('nl')

    expect(fresh.i18n.global.locale.value).toBe('en')
    expect(console.warn).toHaveBeenCalled()
  })
})
