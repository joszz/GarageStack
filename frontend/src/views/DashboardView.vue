<script setup lang="ts">
import { onMounted, onUnmounted, computed, defineAsyncComponent, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useVehicleStore } from '@/stores/vehicle'
import { cardsHiddenByType, useDashboardSettingsStore } from '@/stores/settingsDashboard'
import { useUiSettingsStore } from '@/stores/settingsUi'
import type { CardId } from '@/cards/registry'
import { cardHasData, cardIcon } from '@/cards/registry'
import { useCardData } from '@/cards/useCardData'
import DashboardCardContent from '@/components/DashboardCardContent.vue'
import CardInfoWrap from '@/components/CardInfoWrap.vue'
import CarDiagram from '@/components/CarDiagram.vue'
import LocationMapWidget from '@/components/LocationMapWidget.vue'
import EditableCardSlot from '@/components/EditableCardSlot.vue'
import type EditableCardGridComponent from '@/components/EditableCardGrid.vue'
import SkeletonCard from '@/components/SkeletonCard.vue'
import SkeletonCarDiagram from '@/components/SkeletonCarDiagram.vue'
import SkeletonLocationMap from '@/components/SkeletonLocationMap.vue'
import { useUnits } from '@/composables/useUnits'
import { mayBurnFuel } from '@/utils/vehicleType'

// Only edit mode drags cards around, and the drag-and-drop library is the size of every card on
// this page together, so it loads when the layout editor opens rather than with the dashboard.
// The cast restores the grid's generic item type, which defineAsyncComponent does not carry over.
const EditableCardGrid = defineAsyncComponent(
  () => import('@/components/EditableCardGrid.vue'),
) as unknown as typeof EditableCardGridComponent

const { t } = useI18n()
const store = useVehicleStore()
const settings = useDashboardSettingsStore()
const uiSettings = useUiSettingsStore()

const vin = computed(() => store.activeVin)
const status = computed(() => store.currentStatus)
const editMode = ref(false)

const vehicleType = computed(() => store.effectiveVehicleType)
const cardData = useCardData()

// Shared prop set for the two <CarDiagram> invocations (edit mode + normal mode), which
// are otherwise identical aside from their v-if guard and wrapper class.
const carDiagramProps = computed(() => {
  const s = status.value
  return {
    frontLeft: s?.tyrePressureFrontLeft ?? null,
    frontRight: s?.tyrePressureFrontRight ?? null,
    rearLeft: s?.tyrePressureRearLeft ?? null,
    rearRight: s?.tyrePressureRearRight ?? null,
    driverDoorOpen: s?.driverDoorOpen ?? null,
    passengerDoorOpen: s?.passengerDoorOpen ?? null,
    rearLeftDoorOpen: s?.rearLeftDoorOpen ?? null,
    rearRightDoorOpen: s?.rearRightDoorOpen ?? null,
    trunkOpen: s?.trunkOpen ?? null,
    bonnetOpen: s?.bonnetOpen ?? null,
    lightsMainBeam: s?.lightsMainBeam ?? null,
    lightsDippedBeam: s?.lightsDippedBeam ?? null,
    lightsSide: s?.lightsSide ?? null,
    evSocPercent: s?.evSocPercent ?? null,
    fuelLevelPercent: mayBurnFuel(vehicleType.value) ? (s?.fuelLevelPercent ?? null) : null,
    chargerConnected: s?.chargerConnected ?? null,
    isCharging: s?.isCharging ?? null,
    speed: s?.speed ?? null,
  }
})

const skeletonCards = computed(() => {
  const hidden = cardsHiddenByType(vehicleType.value)
  return settings.cards.filter((c) => c.visible && !hidden.has(c.id))
})

const editableCards = computed({
  get: () => settings.applicableCards(vehicleType.value),
  set: (cards) => settings.setApplicableCards(vehicleType.value, cards),
})

watch(
  () => uiSettings.vehicleTypeOverride,
  () => settings.applyTypeDefaults(vehicleType.value),
)

const units = useUnits()

// Card descriptions that name a unit take it from here; the others ignore these parameters.
const cardDescriptionParams = computed(() => {
  const u = units.value
  return {
    distance: u.symbol('distance'),
    energyPerDistance: u.symbol('energyPerDistance'),
    distancePerPercent: u.symbol('distancePerPercent'),
    fuelConsumption: u.symbol('fuelConsumption'),
    fuelBetter: u.isReciprocal('fuelConsumption')
      ? t('units.higherIsBetter')
      : t('units.lowerIsBetter'),
  }
})

function toggleEditMode() {
  editMode.value = !editMode.value
}

function hasData(id: CardId): boolean {
  return cardData.value !== null && cardHasData(id, cardData.value)
}

function resetLayout() {
  settings.resetLayout(vehicleType.value, hasData)
}

// Everything the dashboard shows: vehicle list (cached after the first call), live status,
// capability config and the newest trip behind the active-trip and top-speed cards.
async function refresh() {
  await store.fetchVehicles()
  if (vin.value) {
    await Promise.all([
      store.fetchStatus(vin.value),
      store.fetchConfig(vin.value),
      store.fetchLatestTrip(vin.value),
    ])
  }
}

// Only what can have changed while the tab was hidden: the live status and, since a trip may
// have ended meanwhile, the newest trip. The vehicle list and capability config do not drift.
async function refreshLive() {
  if (!vin.value) return
  await Promise.all([store.fetchStatus(vin.value), store.fetchLatestTrip(vin.value)])
}

// A trip just ended: the last-trip and top-speed cards describe it now.
watch(
  () => store.tripJustCompleted,
  (completed) => {
    if (completed && vin.value) store.fetchLatestTrip(vin.value)
  },
)

function handleVisibilityChange() {
  if (document.visibilityState === 'visible') {
    // Catch any updates missed while the tab was hidden (e.g. SignalR reconnect gap)
    refreshLive()
  }
}

function handleSwMessage(event: MessageEvent) {
  // A push notification means the car reported something (engine start, door left open, ...)
  // that the live status should reflect.
  if (event.data?.type === 'NOTIFICATION_RECEIVED' && vin.value) {
    store.fetchStatus(vin.value)
  }
}

onMounted(async () => {
  await refresh()

  // Hidden now, so they stay out of the skeleton on the next load too.
  settings.hideCardsNotApplicable(vehicleType.value)
  // On a first visit (nothing saved yet), push no-data visible cards to the end. Once a
  // layout has been saved the user's ordering is theirs and is never reshuffled on mount.
  if (!settings.hasSavedLayout && status.value) settings.orderByData(hasData)
  document.addEventListener('visibilitychange', handleVisibilityChange)
  navigator.serviceWorker?.addEventListener('message', handleSwMessage)
})

onUnmounted(() => {
  document.removeEventListener('visibilitychange', handleVisibilityChange)
  navigator.serviceWorker?.removeEventListener('message', handleSwMessage)
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
      <!-- Tyre diagram + location map toggles -->
      <div class="overview-row overview-row--edit mt-4 mb-4">
        <EditableCardSlot
          class="card-slot--static overview-row__car"
          :visible="settings.showTyreDiagram"
          :draggable="false"
          @toggle-visible="settings.showTyreDiagram = !settings.showTyreDiagram"
        >
          <CarDiagram v-if="settings.showTyreDiagram && status" v-bind="carDiagramProps" />
          <div v-else class="card-slot__placeholder card-slot__placeholder--chart">
            <font-awesome-icon icon="car-side" />
            <span>{{ t('vehicle.overview') }}</span>
          </div>
        </EditableCardSlot>

        <EditableCardSlot
          class="card-slot--static overview-row__map"
          :visible="settings.showLocationMap"
          :draggable="false"
          @toggle-visible="settings.showLocationMap = !settings.showLocationMap"
        >
          <LocationMapWidget v-if="settings.showLocationMap" />
          <div v-else class="card-slot__placeholder card-slot__placeholder--chart">
            <font-awesome-icon icon="location-dot" />
            <span>{{ t('vehicle.location') }}</span>
          </div>
        </EditableCardSlot>
      </div>

      <EditableCardGrid
        v-model="editableCards"
        class="status-grid status-grid--edit"
        @toggle-visible="(card) => settings.toggleCard(card.id)"
      >
        <template #default="{ item: card }">
          <DashboardCardContent
            v-if="card.visible && status && hasData(card.id)"
            :card-id="card.id"
          />
          <div v-else class="card-slot__placeholder">
            <font-awesome-icon :icon="cardIcon(card.id)" />
            <span>{{ t(`settings.cards.${card.id}`) }}</span>
          </div>
        </template>
      </EditableCardGrid>

      <button class="btn btn-outline-secondary mt-3" @click="resetLayout">
        <font-awesome-icon icon="rotate-left" />
        {{ t('dashboard.resetLayout') }}
      </button>
    </template>

    <!-- Normal mode -->
    <template v-else>
      <template v-if="store.loading && !status">
        <div v-if="settings.showTyreDiagram || settings.showLocationMap" class="overview-row mb-4">
          <SkeletonCarDiagram v-if="settings.showTyreDiagram" class="overview-row__car" />
          <SkeletonLocationMap v-if="settings.showLocationMap" class="overview-row__map" />
        </div>
        <div class="status-grid">
          <SkeletonCard v-for="card in skeletonCards" :key="card.id" :icon="cardIcon(card.id)" />
        </div>
      </template>

      <div v-else-if="store.statusError && !status" class="error-state">
        <font-awesome-icon icon="triangle-exclamation" />
        {{ t('common.error') }}
      </div>

      <div v-else-if="!status" class="empty-state">
        {{ t('dashboard.noData') }}
      </div>

      <template v-else>
        <div v-if="settings.showTyreDiagram || settings.showLocationMap" class="overview-row mb-4">
          <CarDiagram
            v-if="settings.showTyreDiagram"
            v-bind="carDiagramProps"
            class="overview-row__car"
          />
          <LocationMapWidget v-if="settings.showLocationMap" class="overview-row__map" />
        </div>

        <div class="status-grid">
          <template v-for="card in settings.cards" :key="card.id">
            <template v-if="card.visible && hasData(card.id)">
              <CardInfoWrap
                :title="t(`settings.cards.${card.id}`)"
                :description="t(`dashboard.cardDesc.${card.id}`, cardDescriptionParams)"
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
