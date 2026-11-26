<script setup lang="ts">
import '@/assets/dashboard.css'
import {
  onMounted,
  onUnmounted,
  computed,
  shallowRef,
  watch,
  type ComponentPublicInstance,
} from 'vue'
import { useI18n } from 'vue-i18n'
import { useVehicleStore } from '@/stores/vehicle'
import { useRdwStore } from '@/stores/rdw'
import { cardsHiddenByType, useDashboardSettingsStore } from '@/stores/settingsDashboard'
import type { OverviewId } from '@/stores/settingsDashboard'
import type { CardId } from '@/cards/registry'
import { cardHasData, cardIcon } from '@/cards/registry'
import { useCardData } from '@/cards/useCardData'
import DashboardCardContent from '@/components/DashboardCardContent.vue'
import CardInfoWrap from '@/components/CardInfoWrap.vue'
import CarDiagram from '@/components/CarDiagram.vue'
import LocationMapWidget from '@/components/LocationMapWidget.vue'
import DashboardMapBackdrop from '@/components/DashboardMapBackdrop.vue'
import DashboardParkingSpot from '@/components/DashboardParkingSpot.vue'
import EditLayoutButton from '@/components/EditLayoutButton.vue'
import SkeletonCard from '@/components/SkeletonCard.vue'
import SkeletonCarDiagram from '@/components/SkeletonCarDiagram.vue'
import SkeletonLocationMap from '@/components/SkeletonLocationMap.vue'
import { useUnits } from '@/composables/useUnits'
import { EditableCardGrid, useEditMode } from '@/composables/useEditMode'
import type { BasemapKind } from '@/composables/useBasemap'
import { useReconnect } from '@/composables/useReconnect'
import { useMediaQuery } from '@/composables/useMediaQuery'
import { mayBurnFuel } from '@/utils/vehicleType'
import { energyUnit } from '@/utils/energy'
import { readLastVin } from '@/utils/lastVin'
import ViewHeader from '@/components/ViewHeader.vue'

const { t } = useI18n()
const store = useVehicleStore()
const rdw = useRdwStore()
const settings = useDashboardSettingsStore()

const vin = computed(() => store.activeVin)
const status = computed(() => store.currentStatus)
const { editMode, cardLongPress } = useEditMode()

const vehicleType = computed(() => store.effectiveVehicleType)
const cardData = useCardData()

// The car diagram and the location map: their slot class, and what edit mode shows while one is off.
const OVERVIEW_PARTS: Record<OverviewId, { className: string; icon: string; label: string }> = {
  car: { className: 'overview-row__car', icon: 'car-side', label: 'vehicle.overview' },
  map: { className: 'overview-row__map', icon: 'location-dot', label: 'vehicle.location' },
}

const shownOverview = computed(() => settings.overview.filter((part) => part.visible))

// The map behind the dashboard, where the user asked for it. Only beside the sidebar: on a phone
// the cards fill the width in one column and would leave no room to see the map, so the location
// card stays. Hiding the location map hides this map too.
const wideEnoughForBackdrop = useMediaQuery('(width >= 768px)')
const mapBackdrop = computed(
  () => settings.mapBackdrop && settings.showLocationMap && wideEnoughForBackdrop.value,
)

// The room in the overview row where the car stands on the map behind (DashboardParkingSpot, or
// its placeholder while editing). A component's ref is its instance, so its element is taken.
const parkingSpot = shallowRef<HTMLElement | null>(null)
function setParkingSpot(el: Element | ComponentPublicInstance | null) {
  const element = el && '$el' in el ? (el.$el as Element) : el
  parkingSpot.value = element instanceof HTMLElement ? element : null
}
// The basemap the map behind draws, which the spot's bar credits.
const backdropBasemap = shallowRef<BasemapKind | null>(null)

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

// Cards this deployment does not serve at all: not offered in edit mode, not even as a placeholder.
const unavailableCards = computed(
  (): ReadonlySet<CardId> => new Set<CardId>(rdw.available === true ? [] : ['rdw']),
)

const skeletonCards = computed(() => {
  const hidden = cardsHiddenByType(vehicleType.value)
  return settings.cards.filter(
    (c) => c.visible && !hidden.has(c.id) && !unavailableCards.value.has(c.id),
  )
})

const editableCards = computed({
  get: () => settings.applicableCards(vehicleType.value, unavailableCards.value),
  set: (cards) => settings.setApplicableCards(vehicleType.value, cards, unavailableCards.value),
})

const units = useUnits()

// Card descriptions that name a unit take it from here; the others ignore these parameters.
const cardDescriptionParams = computed(() => {
  const u = units.value
  return {
    distance: u.symbol('distance'),
    energyPerDistance: u.symbol('energyPerDistance'),
    distancePerPercent: u.symbol('distancePerPercent'),
    fuelConsumption: u.symbol('fuelConsumption'),
    energyBetter: u.isReciprocal('energyPerDistance')
      ? t('units.higherIsBetter')
      : t('units.lowerIsBetter'),
    fuelBetter: u.isReciprocal('fuelConsumption')
      ? t('units.higherIsBetter')
      : t('units.lowerIsBetter'),
  }
})

function hasData(id: CardId): boolean {
  return cardData.value !== null && cardHasData(id, cardData.value)
}

// A card on screen that a long press opens the editor from (EditLayoutButton). While the status
// loads its skeletons stand in for the cards, so the header button does not flash on a phone.
const cardsShown = computed(() => {
  if (!status.value) return store.loading
  return (
    shownOverview.value.length > 0 ||
    settings.cards.some((card) => card.visible && hasData(card.id))
  )
})

function resetLayout() {
  settings.resetLayout(vehicleType.value, hasData)
}

// What the dashboard shows of one car: live status, capability config, the newest trip behind the
// active-trip and top-speed cards, and the RDW lookup (which also says whether the deployment does
// RDW lookups at all).
function fetchCarData(carVin: string) {
  return Promise.all([
    store.fetchStatus(carVin),
    store.fetchConfig(carVin),
    store.fetchLatestTrip(carVin),
    rdw.fetchStatus(carVin),
  ])
}

// The vehicle list (cached after the first call) and the car's data. On a fresh page load the
// list is not known yet, so the car this browser saw last is asked for alongside it rather than
// after it: the map's first tiles wait on the car's position. When the list names another car
// after all, the guess is let finish first, so none of its answers can land after the right ones.
async function refresh() {
  const guess = store.vehicles.length === 0 ? readLastVin() : null
  const early = guess ? fetchCarData(guess) : null
  await store.fetchVehicles()
  if (early) await early
  if (vin.value && vin.value !== guess) await fetchCarData(vin.value)
}

// Only what can have changed while the tab was hidden: the live status and, since a trip may
// have ended meanwhile, the newest trip. The vehicle list and capability config do not drift.
async function refreshLive() {
  if (!vin.value) return
  await Promise.all([store.fetchStatus(vin.value), store.fetchLatestTrip(vin.value)])
}

// Every new reading can change today's energy, and only the API reads the whole day to see whether
// the car's day counter holds up (utils/energy). A hybrid's counter is fuel and needs no check.
watch(
  [vin, () => status.value?.recordedAt, () => energyUnit(vehicleType.value)],
  ([currentVin, recordedAt, unit]) => {
    if (currentVin && recordedAt && unit === 'kwh') store.fetchTodayStat(currentVin)
  },
  { immediate: true },
)

// A trip just ended: the last-trip and top-speed cards describe it now.
watch(
  () => store.tripJustCompleted,
  (completed) => {
    if (completed && vin.value) store.fetchLatestTrip(vin.value)
  },
)

// The server is back: catch up on what was missed, as on returning to the tab.
useReconnect(refreshLive)

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
  // On a first visit (nothing saved yet) the layout began as the defaults for a car not known
  // yet, which leave the plug-in cards (charging session, battery heating) off: take the
  // drivetrain's own defaults, then push no-data visible cards to the end. Once a layout has
  // been saved the user's choices and ordering are theirs and are never touched on mount.
  if (!settings.hasSavedLayout) {
    settings.applyTypeDefaults(vehicleType.value)
    if (status.value) settings.orderByData(hasData)
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
  <div
    class="view-container"
    :class="{
      'dashboard--map-backdrop': mapBackdrop && status,
      'dashboard--map-first': settings.mapFirst,
    }"
  >
    <DashboardMapBackdrop
      v-if="mapBackdrop && status"
      :spot="parkingSpot"
      @basemap="backdropBasemap = $event"
    />

    <ViewHeader :title="t('dashboard.title')" :subtitle="t('dashboard.subtitle')">
      <template #actions>
        <span v-if="store.loading && status" class="refresh-indicator text-muted" role="status">
          <font-awesome-icon icon="spinner" spin />
          <span class="refresh-indicator__label">{{ t('common.refreshing') }}</span>
        </span>
        <EditLayoutButton v-model="editMode" :cards-shown="cardsShown" @reset="resetLayout" />
      </template>
    </ViewHeader>

    <!-- Edit mode: same grid with draggable card-slot wrappers -->
    <template v-if="editMode">
      <!-- Tyre diagram + location map: shown or hidden, and swapped with each other only. Spaced
           like the row outside edit mode, so no card moves when editing starts. -->
      <EditableCardGrid
        v-model="settings.overview"
        class="overview-row mb-4"
        :slot-class="(part) => `card-slot--large ${OVERVIEW_PARTS[part.id].className}`"
        @toggle-visible="(part) => settings.toggleOverview(part.id)"
      >
        <template #default="{ item: part }">
          <CarDiagram v-if="part.id === 'car' && part.visible && status" v-bind="carDiagramProps" />
          <div
            v-else-if="part.id === 'map' && part.visible && mapBackdrop"
            :ref="setParkingSpot"
            class="card-slot__placeholder card-slot__placeholder--chart parking-spot--edit"
          >
            <font-awesome-icon icon="location-dot" />
            <span>{{ t('vehicle.location') }}</span>
          </div>
          <LocationMapWidget v-else-if="part.id === 'map' && part.visible" />
          <div v-else class="card-slot__placeholder card-slot__placeholder--chart">
            <font-awesome-icon :icon="OVERVIEW_PARTS[part.id].icon" />
            <span>{{ t(OVERVIEW_PARTS[part.id].label) }}</span>
          </div>
        </template>
      </EditableCardGrid>

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
            <span class="card-slot__placeholder-text">
              <span>{{ t(`settings.cards.${card.id}`) }}</span>
              <!-- Shown but empty: say why, or the card seems to vanish on Done (#570). -->
              <small v-if="card.visible && status" class="card-slot__placeholder-hint">
                {{ t('dashboard.cardWithoutData') }}
              </small>
            </span>
          </div>
        </template>
      </EditableCardGrid>
    </template>

    <!-- Normal mode -->
    <template v-else>
      <template v-if="store.loading && !status">
        <div v-if="shownOverview.length" class="overview-row mb-4">
          <template v-for="part in shownOverview" :key="part.id">
            <SkeletonCarDiagram v-if="part.id === 'car'" :class="OVERVIEW_PARTS.car.className" />
            <!-- The map behind comes with the car's status; until then its spot stays empty. -->
            <div v-else-if="mapBackdrop" :class="OVERVIEW_PARTS.map.className" />
            <SkeletonLocationMap v-else :class="OVERVIEW_PARTS.map.className" />
          </template>
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
        <div
          v-if="shownOverview.length"
          class="overview-row long-press-cards mb-4"
          v-bind="cardLongPress"
        >
          <template v-for="part in shownOverview" :key="part.id">
            <CarDiagram
              v-if="part.id === 'car'"
              v-bind="carDiagramProps"
              :class="OVERVIEW_PARTS.car.className"
            />
            <DashboardParkingSpot
              v-else-if="mapBackdrop"
              :ref="setParkingSpot"
              :basemap="backdropBasemap"
              :class="OVERVIEW_PARTS.map.className"
            />
            <LocationMapWidget v-else :class="OVERVIEW_PARTS.map.className" />
          </template>
        </div>

        <div class="status-grid long-press-cards" v-bind="cardLongPress">
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
