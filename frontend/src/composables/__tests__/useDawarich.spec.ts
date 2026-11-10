import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import { dawarichApi } from '@/services/dawarichApi'
import { useDawarich } from '../useDawarich'

vi.mock('@/services/dawarichApi', () => ({
  dawarichApi: {
    status: vi.fn<typeof dawarichApi.status>(),
    trackers: vi.fn<typeof dawarichApi.trackers>(),
    save: vi.fn<typeof dawarichApi.save>(),
    backfill: vi.fn<typeof dawarichApi.backfill>(),
  },
}))

async function setup(vin = 'FAKEVN00000000001') {
  let state: ReturnType<typeof useDawarich> | undefined
  mount(
    defineComponent({
      setup() {
        state = useDawarich(vin)
        return () => h('div')
      },
    }),
    { global: { plugins: [createI18n({ legacy: false, locale: 'en', messages: { en } })] } },
  )
  await flushPromises()
  return state!
}

beforeEach(() => {
  vi.resetAllMocks()
  vi.mocked(dawarichApi.status).mockResolvedValue({ configured: true, status: 'ok' })
  vi.mocked(dawarichApi.trackers).mockResolvedValue({
    linked: [{ trackerId: 'fake-phone', createdAt: '2026-01-01T00:00:00Z' }],
    available: [],
    error: null,
  })
})

describe('useDawarich', () => {
  it('requires changed links to be saved before backfilling', async () => {
    const state = await setup()
    state.selected.value = ['different']
    expect(state.dirty.value).toBe(true)
    await state.backfill('2026-01-01')
    expect(dawarichApi.backfill).not.toHaveBeenCalled()
  })

  it('keeps unsaved selection and reports a failed save', async () => {
    const state = await setup()
    state.selected.value = []
    vi.mocked(dawarichApi.save).mockRejectedValue(new Error('offline'))
    await state.save()
    expect(state.saved.value).toBe(false)
    expect(state.error.value).not.toBeNull()
    expect(state.dirty.value).toBe(true)
  })
})
