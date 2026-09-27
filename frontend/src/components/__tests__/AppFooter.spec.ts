import { describe, it, expect, vi, beforeEach } from 'vitest'
import { ref } from 'vue'
import { flushPromises, mount, shallowMount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import AppFooter from '../AppFooter.vue'

const appVersion = vi.hoisted(() => ({ value: null as string | null }))

// Every value the modal's `open` prop takes, from its first render on.
const modalOpenHistory = vi.hoisted(() => [] as boolean[])

vi.mock('../settings/SettingsModal.vue', async () => {
  const { defineComponent, watch } = await import('vue')
  return {
    default: defineComponent({
      name: 'SettingsModal',
      props: { open: { type: Boolean, required: true } },
      setup(props) {
        watch(
          () => props.open,
          (open) => modalOpenHistory.push(open),
          { immediate: true },
        )
        return () => null
      },
    }),
  }
})

vi.mock('@/utils/appVersion', () => ({
  get APP_VERSION() {
    return appVersion.value
  },
}))

vi.mock('@/stores/vehicle', () => ({
  useVehicleStore: () => ({ loading: false, activeVin: null }),
}))

vi.mock('@/composables/useVehicleCommand', () => ({
  useVehicleCommand: () => ({ sending: ref(null), send: vi.fn<() => void>() }),
}))

const i18n = createI18n({ legacy: false, locale: 'en', missingWarn: false, fallbackWarn: false })

function mountFooter() {
  return shallowMount(AppFooter, {
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true } },
  })
}

describe('AppFooter', () => {
  beforeEach(() => {
    appVersion.value = null
    modalOpenHistory.length = 0
  })

  it('loads the settings modal on first use and opens it after mounting it closed', async () => {
    const wrapper = mount(AppFooter, {
      global: { plugins: [i18n], stubs: { FontAwesomeIcon: true } },
    })
    expect(wrapper.findComponent({ name: 'SettingsModal' }).exists()).toBe(false)

    await wrapper.find('button[aria-label="settings.title"]').trigger('click')
    await flushPromises()

    // Closed first, then open: arriving already open would skip the entrance transition.
    expect(modalOpenHistory).toEqual([false, true])
    expect(wrapper.findComponent({ name: 'SettingsModal' }).props('open')).toBe(true)
  })

  it('separates the project name and version with a space', () => {
    appVersion.value = 'v0.5.0'
    expect(mountFooter().find('.app-footer__copyright').text()).toBe('© 2026 GarageStack v0.5.0')
  })

  it('shows only the project name when no version was injected', () => {
    expect(mountFooter().find('.app-footer__copyright').text()).toBe('© 2026 GarageStack')
  })
})
