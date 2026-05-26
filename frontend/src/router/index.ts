import { createRouter, createWebHistory } from 'vue-router'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
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
  ],
})

export default router
