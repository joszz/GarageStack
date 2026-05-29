<script setup lang="ts">
import { onMounted, onUnmounted, computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { VueDraggable } from 'vue-draggable-plus'
import { useVehicleStore } from '@/stores/vehicle'
import { useSettingsStore } from '@/stores/settings'
import type { VehicleType } from '@/stores/vehicle'
import type { CardId } from '@/stores/settings'
import { defaultCards } from '@/stores/settings'
import DashboardCardContent from '@/components/DashboardCardContent.vue'
import CardInfoWrap from '@/components/CardInfoWrap.vue'
import TyreDiagram from '@/components/TyreDiagram.vue'
import { useVehicleAlerts } from '@/composables/useVehicleAlerts'

const { t } = useI18n()
const store = useVehicleStore()
const settings = useSettingsStore()

const vin = computed(() => store.vehicles[0]?.vin ?? null)
const status = computed(() => store.currentStatus)
const editMode = ref(false)

const vehicleType = computed((): VehicleType | 'unknown' => {
  const override = settings.vehicleTypeOverride
  if (override !== 'auto') return override as VehicleType
  return store.detectedVehicleType
})

const isHev = computed(() => vehicleType.value === 'hev')

useVehicleAlerts(status)

function toggleEditMode() {
  editMode.value = !editMode.value
}

function toggleCardVisibility(card: { visible: boolean }) {
  card.visible = !card.visible
}

function cardHasData(id: CardId): boolean {
  const s = status.value
  if (!s) return false
  switch (id) {
    case 'fuelLevel':          return s.fuelLevelPercent !== null
    case 'fuelRange':          return s.fuelRangeKm !== null
    case 'evBattery':          return s.evSocPercent !== null
    case 'charging':           return s.isCharging !== null
    case 'sunRoof':            return s.sunRoofOpen !== null
    case 'efficiencyDistance': return s.mileageOfTheDay !== null
    case 'efficiencyEnergy':   return s.powerUsageOfDay !== null
    case 'efficiencyCharge':   return s.mileageSinceLastCharge !== null && !isHev.value
    case 'efficiencyRatio':    return s.powerUsageOfDay !== null && s.mileageOfTheDay !== null && s.mileageOfTheDay > 0
    default:                   return true
  }
}

const CARD_ICONS: Record<CardId, string> = {
  fuelLevel:          'gas-pump',
  fuelRange:          'road',
  evBattery:          'bolt',
  charging:           'plug',
  odometer:           'gauge',
  battery12v:         'battery-three-quarters',
  doors:              'lock',
  windows:            'car-side',
  sunRoof:            'sun',
  climate:            'wind',
  hvBattery:          'battery-half',
  findMyCar:          'car-burst',
  lights:             'lightbulb',
  efficiencyDistance: 'route',
  efficiencyEnergy:   'plug-circle-bolt',
  efficiencyCharge:   'battery-full',
  efficiencyRatio:    'leaf',
}

function resetLayout() {
  const fresh = defaultCards(vehicleType.value)
  const active = fresh.filter(c => c.visible && cardHasData(c.id))
  const noData = fresh.filter(c => c.visible && !cardHasData(c.id))
  const hidden = fresh.filter(c => !c.visible)
  settings.cards = [...active, ...noData, ...hidden]
}

async function refresh() {
  await store.fetchVehicles()
  if (vin.value) {
    await store.fetchStatus(vin.value)
    await store.fetchConfig(vin.value)
  }
}

let interval: ReturnType<typeof setInterval>

function resetInterval() {
  clearInterval(interval)
  interval = setInterval(refresh, 60_000)
}

function handleVisibilityChange() {
  if (document.visibilityState === 'visible') {
    refresh()
    resetInterval()
  }
}

onMounted(async () => {
  await refresh()
  // On a fresh start (no saved layout), push no-data visible cards to the end
  if (!localStorage.getItem('garagestack-settings') && status.value) {
    const current = [...settings.cards]
    const active = current.filter(c => c.visible && cardHasData(c.id))
    const noData = current.filter(c => c.visible && !cardHasData(c.id))
    const hidden = current.filter(c => !c.visible)
    settings.cards = [...active, ...noData, ...hidden]
  }
  interval = setInterval(refresh, 60_000)
  document.addEventListener('visibilitychange', handleVisibilityChange)
})

onUnmounted(() => {
  clearInterval(interval)
  document.removeEventListener('visibilitychange', handleVisibilityChange)
})
</script>

<template>
  <div class="view-container">
    <div class="view-header">
      <h1>{{ t('dashboard.title') }}</h1>
      <div class="view-header__actions">
        <button
          class="btn btn-sm"
          :class="editMode ? 'btn-primary' : 'btn-outline-secondary'"
          @click="toggleEditMode"
        >
          <font-awesome-icon :icon="editMode ? 'check' : 'pen-to-square'" />
          {{ editMode ? t('dashboard.doneEditing') : t('dashboard.editLayout') }}
        </button>
      </div>
    </div>

    <!-- Edit mode: same grid with draggable card-slot wrappers -->
    <template v-if="editMode">
      <VueDraggable
        v-model="settings.cards"
        class="status-grid status-grid--edit"
        :animation="200"
        ghost-class="card-slot--ghost"
        chosen-class="card-slot--chosen"
      >
        <div
          v-for="card in settings.cards"
          :key="card.id"
          class="card-slot"
          :class="{ 'card-slot--hidden': !card.visible }"
        >
          <div class="card-slot__content">
            <DashboardCardContent v-if="card.visible && status && cardHasData(card.id)" :card-id="card.id" />
            <div v-else class="card-slot__placeholder">
              <font-awesome-icon :icon="CARD_ICONS[card.id]" />
              <span>{{ t(`settings.cards.${card.id}`) }}</span>
            </div>
          </div>
          <button
            class="card-slot__badge"
            :class="card.visible ? 'card-slot__badge--hide' : 'card-slot__badge--show'"
            :aria-label="card.visible ? t('dashboard.hideCard') : t('dashboard.showCard')"
            @click.stop="toggleCardVisibility(card)"
          >
            <font-awesome-icon :icon="card.visible ? 'xmark' : 'plus'" />
          </button>
        </div>
      </VueDraggable>

      <button class="btn btn-outline-secondary mt-3" @click="resetLayout">
        <font-awesome-icon icon="rotate-left" />
        {{ t('dashboard.resetLayout') }}
      </button>
    </template>

    <!-- Normal mode -->
    <template v-else>
      <div v-if="store.loading && !status" class="loading-state">
        <font-awesome-icon icon="spinner" spin />
        {{ t('common.loading') }}
      </div>

      <div v-else-if="store.error && !status" class="error-state">
        <font-awesome-icon icon="triangle-exclamation" />
        {{ t('common.error') }}
      </div>

      <div v-else-if="!status" class="empty-state">
        {{ t('dashboard.noData') }}
      </div>

      <template v-else>
        <div class="status-grid">
          <template v-for="card in settings.cards" :key="card.id">
            <template v-if="card.visible && cardHasData(card.id)">
              <CardInfoWrap
                :title="t(`settings.cards.${card.id}`)"
                :description="t(`dashboard.cardDesc.${card.id}`)"
              >
                <DashboardCardContent :card-id="card.id" />
              </CardInfoWrap>
            </template>
          </template>
        </div>

        <TyreDiagram
          :front-left="status.tyrePressureFrontLeft"
          :front-right="status.tyrePressureFrontRight"
          :rear-left="status.tyrePressureRearLeft"
          :rear-right="status.tyrePressureRearRight"
          class="mt-4"
        />
      </template>
    </template>
  </div>
</template>
