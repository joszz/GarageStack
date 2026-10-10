import { describe, it, expect, afterEach } from 'vitest'
import { nextTick } from 'vue'
import { i18n } from '@/i18n'

describe('i18n', () => {
  afterEach(() => {
    i18n.global.locale.value = 'en'
  })

  it('names the interface language on the page', async () => {
    expect(document.documentElement.lang).toBe('en')

    i18n.global.locale.value = 'nl'
    await nextTick()

    expect(document.documentElement.lang).toBe('nl')
  })
})
