import { describe, expect, it, vi } from 'vitest'
import { flushPromises } from '@vue/test-utils'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { createUnauthorizedRedirect } from '@/plugins/unauthorizedRedirect'

const Empty = { template: '<div />' }

function makeRouter(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'dashboard', component: Empty },
      { path: '/map', name: 'map', component: Empty },
      { path: '/login', name: 'login', component: Empty },
    ],
  })
}

async function runHandler(router: Router) {
  const logout = vi.fn<() => Promise<void>>().mockResolvedValue()
  const done = vi.fn<() => void>()
  createUnauthorizedRedirect(router, logout, done)()
  await flushPromises()
  return { logout, done }
}

describe('createUnauthorizedRedirect', () => {
  it('signs out and shows the login page, to come back to the page the user was on', async () => {
    const router = makeRouter()
    await router.push('/map?period=week')

    const { logout, done } = await runHandler(router)

    expect(logout).toHaveBeenCalledOnce()
    expect(router.currentRoute.value.name).toBe('login')
    expect(router.currentRoute.value.query.redirect).toBe('/map?period=week')
    expect(done).toHaveBeenCalledOnce()
  })

  // A signed-out visitor's first session check answers 401 while the guard is still sending them
  // to the login page with the address they asked for; that address has to survive.
  it('leaves a login page that is already showing as it is, return address and all', async () => {
    const router = makeRouter()
    await router.push('/login?redirect=%2Fapi%2Freference%2F')

    const { done } = await runHandler(router)

    expect(router.currentRoute.value.fullPath).toBe('/login?redirect=%2Fapi%2Freference%2F')
    expect(done).toHaveBeenCalledOnce()
  })

  it('waits for the first navigation before deciding where to go', async () => {
    const router = makeRouter()
    const logout = vi.fn<() => Promise<void>>().mockResolvedValue()
    const done = vi.fn<() => void>()

    createUnauthorizedRedirect(router, logout, done)()
    await router.push('/login?redirect=%2Fmap')
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/login?redirect=%2Fmap')
    expect(done).toHaveBeenCalledOnce()
  })

  it('still shows the login page when signing out fails', async () => {
    const router = makeRouter()
    await router.push('/')
    const logout = vi.fn<() => Promise<void>>().mockRejectedValue(new Error('offline'))
    const done = vi.fn<() => void>()

    createUnauthorizedRedirect(router, logout, done)()
    await flushPromises()

    expect(router.currentRoute.value.name).toBe('login')
    expect(done).toHaveBeenCalledOnce()
  })
})
