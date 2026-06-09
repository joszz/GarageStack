<script setup lang="ts">
import { onMounted, onUnmounted, computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { VueDraggable } from 'vue-draggable-plus'
import { useVehicleStore } from '@/stores/vehicle'
import { useSettingsStore } from '@/stores/settings'
import type { VehicleType } from '@/stores/vehicle'
import type { CardId } from '@/stores/settings'
import { defaultCards } from '@/stores/settings'
import DashboardCardContent from '@/components/DashboardCardContent.vue'
import CardInfoWrap from '@/components/CardInfoWrap.vue'
import CarDiagram from '@/components/CarDiagram.vue'
import SkeletonCard from '@/components/SkeletonCard.vue'
import SkeletonCarDiagram from '@/components/SkeletonCarDiagram.vue'
import { useVehicleAlerts } from '@/composables/useVehicleAlerts'

const { t } = useI18n()
const store = useVehicleStore()
const settings = useSettingsStore()

const vin = computed(() => store.vehicles[0]?.vin ?? null)
const status = computed(() => store.currentStatus)
const editMode = ref(false)
const skeletonCards = computed(() => {
  const visible = settings.cards.filter((c) => c.visible)
  if (vehicleType.value === 'unknown') return visible
  const typeDefaults = defaultCards(vehicleType.value)
  const hiddenByType = new Set(typeDefaults.filter((c) => !c.visible).map((c) => c.id))
  return visible.filter((c) => !hiddenByType.has(c.id))
})

const vehicleType = computed((): VehicleType | 'unknown' => {
  const override = settings.vehicleTypeOverride
  if (override !== 'auto') return override as VehicleType
  return store.detectedVehicleType
})

const isHev = computed(() => vehicleType.value === 'hev')

// Card IDs whose visibility differs between vehicle types (derived from defaultCards).
// When the override changes these are reset to the new type's defaults.
const TYPE_SPECIFIC_CARD_IDS = (() => {
  const knownTypes: VehicleType[] = ['hev', 'phev', 'bev']
  const unknownMap = new Map(defaultCards('unknown').map((c) => [c.id, c.visible]))
  return new Set(
    knownTypes.flatMap((t) =>
      defaultCards(t)
        .filter((c) => c.visible !== unknownMap.get(c.id))
        .map((c) => c.id),
    ),
  )
})()

watch(
  () => settings.vehicleTypeOverride,
  () => {
    const newType = vehicleType.value
    if (newType === 'unknown') return
    const newDefaultMap = new Map(defaultCards(newType).map((c) => [c.id, c.visible]))
    const updated = settings.cards.map((c) =>
      TYPE_SPECIFIC_CARD_IDS.has(c.id)
        ? { ...c, visible: newDefaultMap.get(c.id) ?? c.visible }
        : c,
    )
    settings.cards = [...updated.filter((c) => c.visible), ...updated.filter((c) => !c.visible)]
  },
)

useVehicleAlerts(status)

function toggleEditMode() {
  editMode.value = !editMode.value
}

function toggleCardVisibility(card: { id: CardId; visible: boolean }) {
  const cards = settings.cards
  const idx = cards.indexOf(card)
  if (idx === -1) return
  card.visible = !card.visible
  if (!card.visible) {
    // Move to end so hidden cards cluster at the bottom
    cards.splice(idx, 1)
    cards.push(card)
  } else {
    // Move before the first hidden card so visible order is preserved
    cards.splice(idx, 1)
    const firstHidden = cards.findIndex((c) => !c.visible)
    cards.splice(firstHidden === -1 ? cards.length : firstHidden, 0, card)
  }
}

function cardHasData(id: CardId): boolean {
  const s = status.value
  if (!s) return false
  switch (id) {
    case 'fuelLevel':
      return s.fuelLevelPercent !== null
    case 'fuelRange':
      return s.fuelRangeKm !== null
    case 'evBattery':
      return s.evSocPercent !== null
    case 'charging':
      return s.isCharging !== null
    case 'sunRoof':
      return s.sunRoofOpen !== null
    case 'efficiencyDistance':
      return s.mileageOfTheDay !== null
    case 'efficiencyEnergy':
      return s.powerUsageOfDay !== null
    case 'efficiencyCharge':
      return s.mileageSinceLastCharge !== null && !isHev.value && vehicleType.value !== 'unknown'
    case 'efficiencyRatio':
      return (
        (s.powerUsageOfDay !== null && s.mileageOfTheDay !== null && s.mileageOfTheDay > 0) ||
        ((isHev.value || vehicleType.value === 'phev') &&
          s.fuelRangeKm !== null &&
          s.fuelLevelPercent !== null &&
          s.fuelLevelPercent > 0)
      )
    case 'remainingCharge':
      return s.remainingChargingTime !== null
    case 'chargingSession':
    case 'batteryHeating':
      return vehicleType.value === 'phev' || vehicleType.value === 'bev'
    default:
      return true
  }
}

const CARD_ICONS: Record<CardId, string> = {
  fuelLevel: 'gas-pump',
  fuelRange: 'road',
  evBattery: 'bolt',
  charging: 'plug',
  odometer: 'gauge',
  battery12v: 'battery-three-quarters',
  doors: 'lock',
  windows: 'car-side',
  sunRoof: 'sun',
  climate: 'wind',
  hvBattery: 'battery-half',
  findMyCar: 'car-burst',
  lights: 'lightbulb',
  efficiencyDistance: 'route',
  efficiencyEnergy: 'plug-circle-bolt',
  efficiencyCharge: 'battery-full',
  efficiencyRatio: 'leaf',
  speed: 'gauge-high',
  activeTrip: 'location-arrow',
  remainingCharge: 'clock',
  chargingSession: 'plug-circle-bolt',
  batteryHeating: 'temperature-arrow-up',
}

function resetLayout() {
  const fresh = defaultCards(vehicleType.value)
  const active = fresh.filter((c) => c.visible && cardHasData(c.id))
  const noData = fresh.filter((c) => c.visible && !cardHasData(c.id))
  const hidden = fresh.filter((c) => !c.visible)
  settings.cards = [...active, ...noData, ...hidden]
  settings.showTyreDiagram = true
}

async function refresh() {
  await store.fetchVehicles()
  if (vin.value) {
    await Promise.all([store.fetchStatus(vin.value), store.fetchConfig(vin.value)])
    store.fetchLastTrip(vin.value)
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
  // Hide cards that don't apply to the detected vehicle type so they don't
  // appear in the skeleton on subsequent loads
  if (vehicleType.value !== 'unknown') {
    const typeDefaults = defaultCards(vehicleType.value)
    const shouldHide = new Set(typeDefaults.filter((c) => !c.visible).map((c) => c.id))
    if (settings.cards.some((c) => shouldHide.has(c.id) && c.visible)) {
      const updated = settings.cards.map((c) =>
        shouldHide.has(c.id) ? { ...c, visible: false } : c,
      )
      settings.cards = [...updated.filter((c) => c.visible), ...updated.filter((c) => !c.visible)]
    }
  }
  // On a fresh start (no saved layout), push no-data visible cards to the end
  if (!localStorage.getItem('garagestack-settings') && status.value) {
    const current = [...settings.cards]
    const active = current.filter((c) => c.visible && cardHasData(c.id))
    const noData = current.filter((c) => c.visible && !cardHasData(c.id))
    const hidden = current.filter((c) => !c.visible)
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
        <span v-if="store.loading && status" class="text-muted small me-2">
          <font-awesome-icon icon="spinner" spin />
          {{ t('common.refreshing') }}
        </span>
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
      <!-- Tyre diagram toggle -->
      <div
        class="card-slot card-slot--static mt-4 mb-4"
        :class="{ 'card-slot--hidden': !settings.showTyreDiagram }"
      >
        <div class="card-slot__content">
          <CarDiagram
            v-if="settings.showTyreDiagram && status"
            :front-left="status.tyrePressureFrontLeft"
            :front-right="status.tyrePressureFrontRight"
            :rear-left="status.tyrePressureRearLeft"
            :rear-right="status.tyrePressureRearRight"
            :driver-door-open="status.driverDoorOpen"
            :passenger-door-open="status.passengerDoorOpen"
            :rear-left-door-open="status.rearLeftDoorOpen"
            :rear-right-door-open="status.rearRightDoorOpen"
            :trunk-open="status.trunkOpen"
            :bonnet-open="status.bonnetOpen"
            :lights-main-beam="status.lightsMainBeam"
            :lights-dipped-beam="status.lightsDippedBeam"
            :lights-side="status.lightsSide"
            :ev-soc-percent="status.evSocPercent"
            :fuel-level-percent="status.fuelLevelPercent"
            :charger-connected="status.chargerConnected"
            :is-charging="status.isCharging"
            :speed="status.speed"
          />
          <div v-else class="card-slot__placeholder card-slot__placeholder--chart">
            <font-awesome-icon icon="car-side" />
            <span>{{ t('vehicle.overview') }}</span>
          </div>
        </div>
        <button
          class="card-slot__badge"
          :class="settings.showTyreDiagram ? 'card-slot__badge--hide' : 'card-slot__badge--show'"
          :aria-label="settings.showTyreDiagram ? t('dashboard.hideCard') : t('dashboard.showCard')"
          @click.stop="settings.showTyreDiagram = !settings.showTyreDiagram"
        >
          <font-awesome-icon :icon="settings.showTyreDiagram ? 'xmark' : 'plus'" />
        </button>
      </div>

      <VueDraggable
        v-model="settings.cards"
        class="status-grid status-grid--edit"
        :animation="200"
        ghost-class="card-slot--ghost"
        chosen-class="card-slot--chosen"
        handle=".card-slot__handle"
      >
        <div
          v-for="card in settings.cards"
          :key="card.id"
          class="card-slot"
          :class="{ 'card-slot--hidden': !card.visible }"
        >
          <div class="card-slot__handle">
            <font-awesome-icon icon="grip-lines" />
          </div>
          <div class="card-slot__content">
            <DashboardCardContent
              v-if="card.visible && status && cardHasData(card.id)"
              :card-id="card.id"
            />
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
      <template v-if="store.loading && !status">
        <SkeletonCarDiagram v-if="settings.showTyreDiagram" class="mb-4" />
        <div class="status-grid">
          <SkeletonCard v-for="card in skeletonCards" :key="card.id" :icon="CARD_ICONS[card.id]" />
        </div>
      </template>

      <div v-else-if="store.error && !status" class="error-state">
        <font-awesome-icon icon="triangle-exclamation" />
        {{ t('common.error') }}
      </div>

      <div v-else-if="!status" class="empty-state">
        {{ t('dashboard.noData') }}
      </div>

      <template v-else>
        <CarDiagram
          v-if="settings.showTyreDiagram"
          :front-left="status.tyrePressureFrontLeft"
          :front-right="status.tyrePressureFrontRight"
          :rear-left="status.tyrePressureRearLeft"
          :rear-right="status.tyrePressureRearRight"
          :driver-door-open="status.driverDoorOpen"
          :passenger-door-open="status.passengerDoorOpen"
          :rear-left-door-open="status.rearLeftDoorOpen"
          :rear-right-door-open="status.rearRightDoorOpen"
          :trunk-open="status.trunkOpen"
          :bonnet-open="status.bonnetOpen"
          :lights-main-beam="status.lightsMainBeam"
          :lights-dipped-beam="status.lightsDippedBeam"
          :lights-side="status.lightsSide"
          :ev-soc-percent="status.evSocPercent"
          :fuel-level-percent="status.fuelLevelPercent"
          :charger-connected="status.chargerConnected"
          :is-charging="status.isCharging"
          :speed="status.speed"
          class="mb-4"
        />

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
      </template>
    </template>
  </div>
</template>
