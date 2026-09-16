<script setup lang="ts">
import { onMounted, onUnmounted, computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { VueDraggable } from 'vue-draggable-plus'
import { useVehicleStore } from '@/stores/vehicle'
import { useDashboardSettingsStore } from '@/stores/settingsDashboard'
import { useUiSettingsStore } from '@/stores/settingsUi'
import type { VehicleType } from '@/stores/vehicle'
import type { CardConfig, CardId } from '@/cards/registry'
import { cardHasData, cardIcon, defaultCards } from '@/cards/registry'
import { useCardData } from '@/cards/useCardData'
import DashboardCardContent from '@/components/DashboardCardContent.vue'
import CardInfoWrap from '@/components/CardInfoWrap.vue'
import CarDiagram from '@/components/CarDiagram.vue'
import LocationMapWidget from '@/components/LocationMapWidget.vue'
import EditableCardSlot from '@/components/EditableCardSlot.vue'
import SkeletonCard from '@/components/SkeletonCard.vue'
import SkeletonCarDiagram from '@/components/SkeletonCarDiagram.vue'
import SkeletonLocationMap from '@/components/SkeletonLocationMap.vue'
import { useVehicleAlerts } from '@/composables/useVehicleAlerts'
import { usePush } from '@/composables/usePush'
import { daysAgoIso } from '@/utils/dates'

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
    fuelLevelPercent: vehicleType.value === 'bev' ? null : (s?.fuelLevelPercent ?? null),
    chargerConnected: s?.chargerConnected ?? null,
    isCharging: s?.isCharging ?? null,
    speed: s?.speed ?? null,
  }
})

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

// Cards that are genuinely inapplicable to the detected vehicle type (e.g. fuelLevel on a
// BEV). Cards that are merely off by default for every type (e.g. sunRoof, speed) are not
// included here, so they stay user-togglable and their visibility survives a reload.
const hiddenByTypeIds = computed((): Set<CardId> => {
  if (vehicleType.value === 'unknown') return new Set()
  const typeDefaults = defaultCards(vehicleType.value)
  return new Set(
    typeDefaults.filter((c) => !c.visible && TYPE_SPECIFIC_CARD_IDS.has(c.id)).map((c) => c.id),
  )
})

const skeletonCards = computed(() => {
  const visible = settings.cards.filter((c) => c.visible)
  return visible.filter((c) => !hiddenByTypeIds.value.has(c.id))
})

const editableCards = computed({
  get: () => settings.cards.filter((c) => !hiddenByTypeIds.value.has(c.id)),
  set: (newVal) => {
    const restricted = settings.cards.filter((c) => hiddenByTypeIds.value.has(c.id))
    settings.cards = [...newVal, ...restricted]
  },
})

watch(
  () => uiSettings.vehicleTypeOverride,
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

// A browser that is subscribed to push already receives these alerts from the Worker, so this
// tab only raises its own notification when it is not subscribed.
const { pushState } = usePush()
useVehicleAlerts(status, t, { shouldNotify: () => pushState.value !== 'subscribed' })

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

function hasData(id: CardId): boolean {
  return cardData.value !== null && cardHasData(id, cardData.value)
}

// Visible cards that have something to show come first, then visible-but-empty ones, then the
// hidden ones. Used for a fresh layout and for the explicit reset.
function orderByData(cards: CardConfig[]): CardConfig[] {
  return [
    ...cards.filter((c) => c.visible && hasData(c.id)),
    ...cards.filter((c) => c.visible && !hasData(c.id)),
    ...cards.filter((c) => !c.visible),
  ]
}

function resetLayout() {
  settings.cards = orderByData(defaultCards(vehicleType.value))
  settings.showTyreDiagram = true
}

// Everything the dashboard shows: vehicle list (cached after the first call), live status,
// capability config and the recent trips behind the active-trip and top-speed cards.
async function refresh() {
  await store.fetchVehicles()
  if (vin.value) {
    await Promise.all([
      store.fetchStatus(vin.value),
      store.fetchConfig(vin.value),
      store.fetchTrips(vin.value, daysAgoIso(uiSettings.filterDays)),
    ])
  }
}

// Only what can have changed while the tab was hidden: the live status and, since a trip may
// have ended meanwhile, the trip list. The vehicle list and capability config do not drift.
async function refreshLive() {
  if (!vin.value) return
  await Promise.all([
    store.fetchStatus(vin.value),
    store.fetchTrips(vin.value, daysAgoIso(uiSettings.filterDays)),
  ])
}

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

  // Hide cards that don't apply to the detected vehicle type so they don't
  // appear in the skeleton on subsequent loads. Cards merely off by default
  // (e.g. sunRoof, speed) are left alone so a user's manual toggle survives reloads.
  const shouldHide = hiddenByTypeIds.value
  if (settings.cards.some((c) => shouldHide.has(c.id) && c.visible)) {
    const updated = settings.cards.map((c) => (shouldHide.has(c.id) ? { ...c, visible: false } : c))
    settings.cards = [...updated.filter((c) => c.visible), ...updated.filter((c) => !c.visible)]
  }
  // On a first visit (nothing saved yet), push no-data visible cards to the end. Once a
  // layout has been saved the user's ordering is theirs and is never reshuffled on mount.
  if (!settings.hasSavedLayout && status.value) {
    settings.cards = orderByData([...settings.cards])
  }
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

      <VueDraggable
        v-model="editableCards"
        class="status-grid status-grid--edit"
        :animation="200"
        ghost-class="card-slot--ghost"
        chosen-class="card-slot--chosen"
        handle=".card-slot__handle"
      >
        <EditableCardSlot
          v-for="card in editableCards"
          :key="card.id"
          :visible="card.visible"
          @toggle-visible="toggleCardVisibility(card)"
        >
          <DashboardCardContent
            v-if="card.visible && status && hasData(card.id)"
            :card-id="card.id"
          />
          <div v-else class="card-slot__placeholder">
            <font-awesome-icon :icon="cardIcon(card.id)" />
            <span>{{ t(`settings.cards.${card.id}`) }}</span>
          </div>
        </EditableCardSlot>
      </VueDraggable>

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
