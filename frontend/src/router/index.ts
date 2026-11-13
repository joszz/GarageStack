import { createRouter, createWebHistory, type RouteLocationNormalized } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useSettingsSyncStore } from '@/stores/settingsSync'
import { RASTER_TILE_ORIGIN } from '@/utils/rasterTiles'
import { preconnect } from '@/utils/preconnect'

declare module 'vue-router' {
  interface RouteMeta {
    /** Reachable without a session: the guard lets anyone through. */
    public?: boolean
    /** Other origins the view fetches from as it first renders (see preconnectRouteOrigins). */
    preconnect?: readonly string[]
    /** The locale key of the view's name, which the browser tab shows (see usePageTitle). */
    titleKey?: string
  }
}

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/login',
      name: 'login',
      component: () => import('@/views/LoginView.vue'),
      meta: { public: true, titleKey: 'auth.login' },
    },
    {
      path: '/',
      name: 'dashboard',
      component: () => import('@/views/DashboardView.vue'),
      // The map card's first raster tile is the dashboard's largest paint.
      meta: { preconnect: [RASTER_TILE_ORIGIN], titleKey: 'nav.dashboard' },
    },
    {
      path: '/statistics',
      name: 'statistics',
      meta: { titleKey: 'nav.statistics' },
      component: () => import('@/views/StatisticsView.vue'),
    },
    {
      path: '/map',
      name: 'map',
      meta: { titleKey: 'nav.map' },
      component: () => import('@/views/MapView.vue'),
    },
    {
      path: '/trip-log',
      name: 'tripLog',
      meta: { titleKey: 'nav.tripLog' },
      component: () => import('@/views/TripLogView.vue'),
    },
    {
      path: '/charge-history',
      name: 'chargeHistory',
      meta: { titleKey: 'nav.chargeHistory' },
      component: () => import('@/views/ChargeHistoryView.vue'),
    },
    {
      // A saved trip's route, opened from the trip log or the map.
      path: '/trips/:id(\\d+)/edit',
      name: 'tripEdit',
      meta: { titleKey: 'tripEdit.title' },
      component: () => import('@/views/TripEditView.vue'),
    },
    {
      path: '/climate-schedules',
      name: 'climateSchedules',
      meta: { titleKey: 'nav.climateSchedules' },
      component: () => import('@/views/ClimateSchedulesView.vue'),
    },
    {
      path: '/maintenance',
      name: 'maintenance',
      meta: { titleKey: 'nav.maintenance' },
      component: () => import('@/views/MaintenanceView.vue'),
    },
    {
      path: '/logs',
      name: 'logs',
      meta: { titleKey: 'nav.logs' },
      component: () => import('@/views/LogsView.vue'),
    },
    {
      // An address no view answers to (an old bookmark, a typo) lands on the dashboard rather than
      // on an empty page.
      path: '/:pathMatch(.*)*',
      name: 'notFound',
      redirect: { name: 'dashboard' },
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

/**
 * Opens the connections to other origins that a navigation's view will need, on the same cue as
 * preloadRouteComponents. The view only asks for its first resource there once its own code, the
 * session check and its data have arrived, and by then the handshake is already done.
 */
export function preconnectRouteOrigins(to: RouteLocationNormalized): void {
  to.meta.preconnect?.forEach(preconnect)
}

router.beforeEach(async (to) => {
  const auth = useAuthStore()
  const settingsSync = useSettingsSyncStore()
  const isPublic = to.meta.public === true

  // Only when the session held in storage makes it likely the guard lets this navigation
  // through: a visitor about to be sent to the login page has no use for the dashboard's code,
  // and no reason to contact its tile server.
  // The account's settings are asked for alongside the session check for the same reason, so a
  // cold load waits on one round trip rather than two.
  if (isPublic || auth.isAuthenticated) {
    preloadRouteComponents(to)
    preconnectRouteOrigins(to)
  }
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
