import { createRouter, createWebHistory, type RouteLocationNormalized } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useSettingsSyncStore } from '@/stores/settingsSync'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/login',
      name: 'login',
      component: () => import('@/views/LoginView.vue'),
      meta: { public: true },
    },
    {
      path: '/',
      name: 'dashboard',
      component: () => import('@/views/DashboardView.vue'),
    },
    {
      path: '/statistics',
      name: 'statistics',
      component: () => import('@/views/StatisticsView.vue'),
    },
    {
      path: '/map',
      name: 'map',
      component: () => import('@/views/MapView.vue'),
    },
    {
      path: '/trip-log',
      name: 'tripLog',
      component: () => import('@/views/TripLogView.vue'),
    },
    {
      path: '/maintenance',
      name: 'maintenance',
      component: () => import('@/views/MaintenanceView.vue'),
    },
  ],
  // No scrollBehavior: the scroll container is the main content element, not the window, so
  // App.vue scrolls it to the top on every route change itself.
})

/**
 * Starts downloading the lazy view(s) a navigation is headed for. The router only fetches them
 * once every beforeEach guard has settled, and the guard below waits on a session check, so on a
 * cold load the view's chunks queued behind that round trip. The module loader shares the
 * download with the router's own import() of the same chunk later, so it is never fetched twice.
 */
export function preloadRouteComponents(to: RouteLocationNormalized): void {
  for (const record of to.matched) {
    for (const component of Object.values(record.components ?? {})) {
      // Views are declared as () => import(...); a component already resolved is an object.
      if (typeof component === 'function') {
        void (component as () => Promise<unknown>)().catch(() => {
          // The router's own import surfaces a failed download; nothing to add here.
        })
      }
    }
  }
}

router.beforeEach(async (to) => {
  const auth = useAuthStore()
  const settingsSync = useSettingsSyncStore()
  const isPublic = to.meta.public === true

  // Only when the session held in storage makes it likely the guard lets this navigation
  // through: a visitor about to be sent to the login page has no use for the dashboard's code.
  // The account's settings are asked for alongside the session check for the same reason, so a
  // cold load waits on one round trip rather than two.
  if (isPublic || auth.isAuthenticated) preloadRouteComponents(to)
  if (auth.isAuthenticated) void settingsSync.start()
  await auth.ensureVerified()

  // Signed out, or the stored session turned out to be stale: nothing more goes to the account.
  if (!auth.isAuthenticated) settingsSync.stop()

  if (isPublic) {
    if (to.name === 'login' && auth.isAuthenticated) {
      return { name: 'dashboard' }
    }
    return true
  }

  if (!auth.isAuthenticated) {
    return {
      name: 'login',
      query: { redirect: to.fullPath },
    }
  }

  // Views read the settings as they mount, and some write them back (the dashboard arranges its
  // cards on a first visit), so none may mount before the account's copy has been taken in.
  // Right after signing in, this is where it is first asked for.
  await settingsSync.start()
  return true
})

export default router
