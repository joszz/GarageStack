<script setup lang="ts">
import '@/assets/carDiagram.css'
import { useI18n } from 'vue-i18n'
import { computed } from 'vue'
import CardInfoWrap from './CardInfoWrap.vue'
import CarLightBeams from './carDiagram/CarLightBeams.vue'
import LevelBadge from './carDiagram/LevelBadge.vue'
import SpeedGauge from './carDiagram/SpeedGauge.vue'
import TyreIndicators from './carDiagram/TyreIndicators.vue'
import TyreLabels from './carDiagram/TyreLabels.vue'
import { CAR_SILHOUETTE_VIEWBOX, CAR_SILHOUETTE_MARKUP } from '@/assets/carSilhouette'
import { useTyrePressureThresholds } from '@/composables/useTyrePressureThresholds'
import { useUnits } from '@/composables/useUnits'
import { evLevelVariant, fuelLevelVariant, type LevelVariant } from '@/utils/levels'

const { t } = useI18n()
const tyreThresholds = useTyrePressureThresholds()
const units = useUnits()

const props = defineProps<{
  frontLeft: number | null
  frontRight: number | null
  rearLeft: number | null
  rearRight: number | null
  driverDoorOpen?: boolean | null
  passengerDoorOpen?: boolean | null
  rearLeftDoorOpen?: boolean | null
  rearRightDoorOpen?: boolean | null
  trunkOpen?: boolean | null
  bonnetOpen?: boolean | null
  lightsMainBeam?: boolean | null
  lightsDippedBeam?: boolean | null
  lightsSide?: boolean | null
  evSocPercent?: number | null
  fuelLevelPercent?: number | null
  chargerConnected?: boolean | null
  isCharging?: boolean | null
  speed?: number | null
}>()

const tyrePressures = computed(() => ({
  frontLeft: props.frontLeft,
  frontRight: props.frontRight,
  rearLeft: props.rearLeft,
  rearRight: props.rearRight,
}))

// The colour bands, in the pressure unit this browser shows: the thresholds themselves are
// configured in bar, which is what the car reports.
const tyreLegendParams = computed(() => {
  const u = units.value
  const value = (bar: number) => u.measure('pressure', bar)!.value
  return {
    low: value(tyreThresholds.value.lowBar),
    good: value(tyreThresholds.value.goodBar),
    high: value(tyreThresholds.value.highBar),
    unit: u.symbol('pressure'),
  }
})

const doorBadges = computed(() => [
  {
    key: 'bonnet',
    open: props.bonnetOpen,
    suffix: 'bonnet',
    titleKey: 'vehicle.doors_detail.bonnet',
  },
  {
    key: 'driver',
    open: props.driverDoorOpen,
    suffix: 'door-fl',
    titleKey: 'vehicle.doors_detail.driver',
  },
  {
    key: 'passenger',
    open: props.passengerDoorOpen,
    suffix: 'door-fr',
    titleKey: 'vehicle.doors_detail.passenger',
  },
  {
    key: 'rearLeft',
    open: props.rearLeftDoorOpen,
    suffix: 'door-rl',
    titleKey: 'vehicle.doors_detail.rearLeft',
  },
  {
    key: 'rearRight',
    open: props.rearRightDoorOpen,
    suffix: 'door-rr',
    titleKey: 'vehicle.doors_detail.rearRight',
  },
  { key: 'trunk', open: props.trunkOpen, suffix: 'trunk', titleKey: 'vehicle.doors_detail.boot' },
])

const hasLights = computed(() => props.lightsMainBeam || props.lightsDippedBeam || props.lightsSide)

// A level to show on the diagram, or null when the car reports none. The same bands as the
// dashboard's fuel and battery cards, so both colour a level alike.
function level(
  percent: number | null | undefined,
  variantOf: (pct: number | null) => LevelVariant | undefined,
): { percent: number; variant: LevelVariant } | null {
  const variant = variantOf(percent ?? null)
  return percent != null && variant ? { percent, variant } : null
}

const fuelLevel = computed(() => level(props.fuelLevelPercent, fuelLevelVariant))
const batteryLevel = computed(() => level(props.evSocPercent, evLevelVariant))

const isMoving = computed(() => (props.speed ?? 0) > 0)

const roadAnimDuration = computed(() => {
  const s = props.speed ?? 0
  if (s <= 0) return '2s'
  return `${Math.max(0.1, Math.min(2, 30 / s)).toFixed(2)}s`
})

const motionBlurAmount = computed(() => {
  const s = props.speed ?? 0
  if (s <= 100) return 0
  return Math.min(10, (s - 100) / 5)
})
</script>

<template>
  <CardInfoWrap :title="t('vehicle.overview')">
    <template #info>
      <div class="card-info-sections">
        <div class="card-info-section">
          <p class="card-info-section__title">
            <font-awesome-icon icon="circle" class="tyre-legend-dot tyre-legend-dot--ok" />
            <font-awesome-icon icon="circle" class="tyre-legend-dot tyre-legend-dot--warning" />
            <font-awesome-icon icon="circle" class="tyre-legend-dot tyre-legend-dot--danger" />
            {{ t('vehicle.diagram.infoTyreTitle') }}
          </p>
          <p class="card-info-desc">
            {{ t('vehicle.diagram.infoTyreDesc', tyreLegendParams) }}
          </p>
        </div>
        <div class="card-info-section">
          <p class="card-info-section__title">
            <font-awesome-icon icon="lock-open" />
            {{ t('vehicle.diagram.infoDoorsTitle') }}
          </p>
          <p class="card-info-desc">{{ t('vehicle.diagram.infoDoorsDesc') }}</p>
        </div>
        <div class="card-info-section">
          <p class="card-info-section__title">
            <font-awesome-icon icon="lightbulb" />
            {{ t('vehicle.diagram.infoLightsTitle') }}
          </p>
          <p class="card-info-desc">{{ t('vehicle.diagram.infoLightsDesc') }}</p>
        </div>
        <div class="card-info-section">
          <p class="card-info-section__title">
            <font-awesome-icon icon="gas-pump" />
            {{ t('vehicle.diagram.infoLevelTitle') }}
          </p>
          <p class="card-info-desc">{{ t('vehicle.diagram.infoLevelDesc') }}</p>
        </div>
        <div class="card-info-section">
          <p class="card-info-section__title">
            <font-awesome-icon icon="plug" />
            {{ t('vehicle.diagram.infoChargingTitle') }}
          </p>
          <p class="card-info-desc">{{ t('vehicle.diagram.infoChargingDesc') }}</p>
        </div>
      </div>
    </template>
    <div class="tyre-diagram">
      <p class="tyre-diagram__title">
        <font-awesome-icon icon="car-side" />
        {{ t('vehicle.overview') }}
      </p>

      <div class="tyre-diagram__wrap">
        <div class="tyre-car-wrap">
          <!--
          ViewBox: -40 0 420 480  (widened by 40 px each side; car stays at x=170 = 50%)
          Orange car: nested SVG, original bounds x≈137–245 y≈237–445
            placed at x=105 y=115 width=130 height=250 in parent coords.
          Car body parent coords: x≈119–222; center x=170.
          Wheel corners (parent): FL(127,123) FR(213,123) RL(127,349) RR(213,349)
          Dots at outer side of each wheel: FL(110,145) FR(230,145) RL(110,327) RR(230,327)
          Labels at (~20° from horizontal):  FL→(72,131) FR→(268,131) RL→(72,341) RR→(268,341)
          CSS % = (svg_x + 40) / 420: FL/RL=26.7%  FR/RR=73.3%
          Side light cones: left x=72–118 y=137–147 / right x=222–268 y=137–147
          Door icon badges: see .car-badge-* CSS classes
        -->
          <svg
            viewBox="-40 0 420 480"
            xmlns="http://www.w3.org/2000/svg"
            class="tyre-diagram__svg"
            :aria-label="t('vehicle.diagramLabel')"
          >
            <defs>
              <!-- Motion blur above 100 km/h: ghost is shifted toward the rear (downward)
                   and blurred, then the sharp original is composited on top so the
                   blur trails behind the car rather than spreading symmetrically. -->
              <filter id="motion-blur" x="-5%" y="-5%" width="110%" height="160%">
                <feOffset in="SourceGraphic" :dy="motionBlurAmount * 2" result="shifted" />
                <feGaussianBlur
                  in="shifted"
                  :stdDeviation="`0 ${motionBlurAmount}`"
                  result="blurred"
                />
                <feMerge>
                  <feMergeNode in="blurred" />
                  <feMergeNode in="SourceGraphic" />
                </feMerge>
              </filter>
            </defs>

            <!-- Road animation: visible only while driving (speed > 0).
                 ViewBox is -40 0 420 480. 3 equal lanes of 140 px each.
                 Left lane: x=-40–100. Center lane: x=100–240 (car body x≈119–222, ~20 px margin).
                 Right lane: x=240–380. Edges at x=-38 and x=378. -->
            <g v-if="isMoving" class="road-anim-group">
              <rect x="-40" y="0" width="420" height="480" class="road-surface" />
              <line x1="-38" y1="0" x2="-38" y2="480" class="road-edge" />
              <line x1="378" y1="0" x2="378" y2="480" class="road-edge" />
              <line
                x1="100"
                y1="0"
                x2="100"
                y2="480"
                class="road-center-dash"
                :style="{ animationDuration: roadAnimDuration }"
              />
              <line
                x1="240"
                y1="0"
                x2="240"
                y2="480"
                class="road-center-dash"
                :style="{ animationDuration: roadAnimDuration }"
              />
            </g>

            <!-- Light beams drawn behind the car, which masks where they start. -->
            <CarLightBeams
              v-if="hasLights"
              :main-beam="!!lightsMainBeam"
              :dipped-beam="!!lightsDippedBeam"
            />

            <!-- Orange car illustration (top-view) -->
            <g :filter="motionBlurAmount > 0 ? 'url(#motion-blur)' : undefined">
              <svg
                :viewBox="CAR_SILHOUETTE_VIEWBOX"
                x="105"
                y="115"
                width="130"
                height="250"
                v-html="CAR_SILHOUETTE_MARKUP"
              />
            </g>

            <!-- Charging cable: rear bumper → charger off-screen below.
                 Path direction is bottom→top so dashoffset animation flows toward the car. -->
            <g
              v-if="chargerConnected || isCharging"
              class="charge-group"
              :class="{ 'charge-group--active': isCharging }"
            >
              <title>
                {{ isCharging ? t('vehicle.chargingYes') : t('vehicle.hvBattery.pluggedIn') }}
              </title>
              <circle cx="170" cy="367" r="16" class="charge-entry-glow" />
              <rect x="162" y="362" width="16" height="8" rx="2" class="charge-socket" />
              <!-- Cable enters the battery badge from below; endpoint inside badge so nothing shows under it -->
              <path d="M 170,440 L 170,370" class="charge-cable" />
            </g>

            <TyreIndicators :pressures="tyrePressures" />
          </svg>

          <TyreLabels :pressures="tyrePressures" />

          <!-- Open panel icon badges – positioned over SVG at each panel location -->
          <template v-for="badge in doorBadges" :key="badge.key">
            <div
              v-if="badge.open"
              class="car-badge"
              :class="`car-badge--${badge.suffix}`"
              :title="t(badge.titleKey)"
            >
              <font-awesome-icon icon="lock-open" />
            </div>
          </template>

          <!-- Fuel level (front) -->
          <LevelBadge
            v-if="fuelLevel"
            class="diagram-level--fuel"
            icon="gas-pump"
            :label="t('vehicle.fuel')"
            :percent="fuelLevel.percent"
            :variant="fuelLevel.variant"
          />

          <!-- EV battery (rear) -->
          <LevelBadge
            v-if="batteryLevel"
            class="diagram-level--battery"
            :class="{
              'diagram-level--charging': isCharging,
              'diagram-level--connected': chargerConnected && !isCharging,
            }"
            icon="bolt"
            :label="t('vehicle.evSoc')"
            :percent="batteryLevel.percent"
            :variant="batteryLevel.variant"
          />

          <SpeedGauge v-if="isMoving" :speed-kmh="speed ?? 0" />
        </div>
      </div>
    </div>
  </CardInfoWrap>
</template>
