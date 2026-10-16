import { describe, it, expect, vi, beforeEach } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import type { ApiToken, CreatedApiToken } from '@/services/apiTokensApi'
import ApiTokenSettings from '../ApiTokenSettings.vue'

const api = vi.hoisted(() => ({
  list: vi.fn<() => Promise<ApiToken[]>>(),
  create: vi.fn<(name: string) => Promise<CreatedApiToken>>(),
  revoke: vi.fn<(id: number) => Promise<void>>(),
}))
vi.mock('@/services/apiTokensApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/services/apiTokensApi')>()),
  apiTokensApi: api,
}))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

const watch: ApiToken = {
  id: 1,
  name: 'Watch',
  prefix: 'gs_abc123',
  createdAt: '2026-10-01T08:00:00Z',
  lastUsedAt: null,
}

async function render() {
  const wrapper = mount(ApiTokenSettings, {
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true } },
  })
  await flushPromises()
  return wrapper
}

describe('ApiTokenSettings', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    api.list.mockReset().mockResolvedValue([watch])
    api.create.mockReset()
    api.revoke.mockReset().mockResolvedValue()
  })

  it('lists tokens by name and how they start, never the token itself', async () => {
    const wrapper = await render()

    expect(wrapper.text()).toContain('Watch')
    expect(wrapper.text()).toContain('gs_abc123…')
    expect(wrapper.text()).toContain(en.settings.apiTokens.neverUsed)
  })

  it('shows a new token once, to copy, and adds it to the list', async () => {
    api.create.mockResolvedValue({
      details: { ...watch, id: 2, name: 'Home Assistant', prefix: 'gs_xyz789' },
      token: 'gs_xyz789-the-whole-secret',
    })
    const wrapper = await render()

    await wrapper.find('input').setValue('  Home Assistant ')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(api.create).toHaveBeenCalledWith('Home Assistant')
    const shown = wrapper.find<HTMLInputElement>('input[readonly]')
    expect(shown.element.value).toBe('gs_xyz789-the-whole-secret')
    expect(wrapper.text()).toContain(en.settings.apiTokens.copyNow)
    expect(wrapper.text().indexOf('Home Assistant')).toBeLessThan(wrapper.text().indexOf('Watch'))
  })

  it('revokes on the second tap only', async () => {
    const wrapper = await render()
    const revoke = () => wrapper.findAll('button').find((b) => b.text().startsWith('Revoke'))!

    await revoke().trigger('click')
    expect(api.revoke).not.toHaveBeenCalled()
    expect(revoke().text()).toBe(en.settings.apiTokens.confirmRevoke)

    await revoke().trigger('click')
    await flushPromises()

    expect(api.revoke).toHaveBeenCalledWith(1)
    expect(wrapper.text()).toContain(en.settings.apiTokens.none)
  })

  it('names why a token could not be made', async () => {
    const { ApiError } = await import('@/services/apiCore')
    api.create.mockRejectedValue(new ApiError(400, '/api/tokens', { code: 'token.tooMany' }))
    const wrapper = await render()

    await wrapper.find('input').setValue('One too many')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.find('[role=alert]').text()).toBe(en.errors.codes.token.tooMany)
  })
})
