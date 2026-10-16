import { describe, it, expect } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import { useStoreAction } from '../useStoreAction'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

// A store's error ref, the way withLoading fills it, and the composable bound to it.
function setup() {
  const storeError = ref<Error | null>(null)
  let action!: ReturnType<typeof useStoreAction>
  mount(
    defineComponent({
      setup() {
        action = useStoreAction(() => storeError.value)
        return () => h('div')
      },
    }),
    { global: { plugins: [i18n] } },
  )
  return { storeError, action }
}

describe('useStoreAction', () => {
  it('resolves true and keeps no message when the store reports nothing', async () => {
    const { action } = setup()

    expect(await action.run(async () => {})).toBe(true)
    expect(action.error.value).toBeNull()
  })

  it('resolves false with the translated reason when the store kept an error', async () => {
    const { storeError, action } = setup()

    const ok = await action.run(async () => {
      storeError.value = new Error('boom')
    })

    expect(ok).toBe(false)
    expect(action.error.value).toBe(en.errors.unknown)
  })

  it('is busy only while the action runs', async () => {
    const { action } = setup()
    let busyDuring = false

    await action.run(async () => {
      busyDuring = action.busy.value
    })

    expect(busyDuring).toBe(true)
    expect(action.busy.value).toBe(false)
  })

  it('clears the previous message when it runs again', async () => {
    const { storeError, action } = setup()
    storeError.value = new Error('boom')
    await action.run(async () => {})

    storeError.value = null
    await action.run(async () => {})

    expect(action.error.value).toBeNull()
  })
})
