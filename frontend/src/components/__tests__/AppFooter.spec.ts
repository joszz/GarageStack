import { describe, it, expect, vi, beforeEach } from 'vitest'
import { ref } from 'vue'
import { flushPromises, mount, shallowMount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import AppFooter from '../AppFooter.vue'

const appVersion = vi.hoisted(() => ({ value: null as string | null }))

const update = vi.hoisted(() => ({
  fails: false,
  answer: { updateAvailable: false, latestVersion: null, releaseUrl: null } as {
    updateAvailable: boolean
    latestVersion: string | null
    releaseUrl: string | null
  },
}))

vi.mock('@/services/updatesApi', () => ({
  updatesApi: {
    get: () =>
      update.fails ? Promise.reject(new Error('offline')) : Promise.resolve(update.answer),
  },
}))

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
    update.fails = false
    update.answer = { updateAvailable: false, latestVersion: null, releaseUrl: null }
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

  it('links to a newer release when the API found one', async () => {
    update.answer = {
      updateAvailable: true,
      latestVersion: '0.9.3',
      releaseUrl: 'https://github.com/joszz/GarageStack/releases/tag/v0.9.3',
    }
    const wrapper = mountFooter()
    await flushPromises()

    const link = wrapper.find('a.app-footer__update')
    expect(link.attributes('href')).toBe('https://github.com/joszz/GarageStack/releases/tag/v0.9.3')
    expect(link.attributes('rel')).toContain('noopener')
  })

  it('shows no update link while the running version is the latest', async () => {
    const wrapper = mountFooter()
    await flushPromises()

    expect(wrapper.find('a.app-footer__update').exists()).toBe(false)
  })

  // The API builds the link itself; a link anywhere else is not followed regardless.
  it('ignores a release link that does not point at GitHub', async () => {
    update.answer = {
      updateAvailable: true,
      latestVersion: '0.9.3',
      releaseUrl: 'https://example.com/v0.9.3',
    }
    const wrapper = mountFooter()
    await flushPromises()

    expect(wrapper.find('a.app-footer__update').exists()).toBe(false)
  })

  it('shows nothing when the API cannot be asked', async () => {
    update.fails = true
    const wrapper = mountFooter()
    await flushPromises()

    expect(wrapper.find('a.app-footer__update').exists()).toBe(false)
  })
})
