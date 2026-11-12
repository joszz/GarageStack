import { describe, it, expect, afterEach } from 'vitest'
import { defineComponent, h } from 'vue'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { createMemoryHistory, createRouter } from 'vue-router'
import en from '@/locales/en.json'
import nl from '@/locales/nl.json'
import { usePageTitle } from '../usePageTitle'

enableAutoUnmount(afterEach)

const Empty = defineComponent({ render: () => h('div') })

async function mountWithTitle() {
  const i18n = createI18n({ legacy: false, locale: 'en', messages: { en, nl } })
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: Empty, meta: { titleKey: 'nav.dashboard' } },
      { path: '/map', component: Empty, meta: { titleKey: 'nav.map' } },
      { path: '/plain', component: Empty },
    ],
  })
  await router.push('/')
  mount(
    defineComponent({
      setup() {
        usePageTitle()
        return () => h('div')
      },
    }),
    { global: { plugins: [i18n, router] } },
  )
  await flushPromises()
  return { i18n, router }
}

describe('usePageTitle', () => {
  it('names the tab after the view, and follows navigation', async () => {
    const { router } = await mountWithTitle()
    expect(document.title).toBe(`${en.nav.dashboard} - GarageStack`)

    await router.push('/map')
    expect(document.title).toBe(`${en.nav.map} - GarageStack`)

    await router.push('/plain')
    expect(document.title).toBe('GarageStack')
  })

  it('follows a language switch', async () => {
    const { i18n } = await mountWithTitle()

    i18n.global.locale.value = 'nl'
    await flushPromises()

    expect(document.title).toBe(`${nl.nav.dashboard} - GarageStack`)
  })
})
