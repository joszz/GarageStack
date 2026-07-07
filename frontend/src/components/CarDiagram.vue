<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { computed } from 'vue'
import CardInfoWrap from './CardInfoWrap.vue'
import { CAR_SILHOUETTE_VIEWBOX, CAR_SILHOUETTE_MARKUP } from '@/assets/carSilhouette'

const { t } = useI18n()

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

function pressureVariant(bar: number | null): string {
  if (bar === null) return 'unknown'
  if (bar < 2.2) return 'danger'
  if (bar < 2.6) return 'warning'
  return 'ok'
}

function pressureColor(bar: number | null): string {
  const v = pressureVariant(bar)
  if (v === 'danger') return 'var(--tyre-danger, #dc3545)'
  if (v === 'warning') return 'var(--tyre-warning, #fd7e14)'
  if (v === 'ok') return 'var(--tyre-ok, #198754)'
  return 'var(--tyre-unknown, #6c757d)'
}

function fmt(bar: number | null): string {
  return bar !== null ? bar.toFixed(2) : '-'
}

// Tyre indicator line/dot endpoints - see the ViewBox comment above the template for how
// these coordinates were derived from the car body bounds.
const tyreIndicators = computed(() => [
  { key: 'frontLeft', value: props.frontLeft, x1: 110, y1: 145, x2: 72, y2: 131, cx: 110, cy: 145 },
  {
    key: 'frontRight',
    value: props.frontRight,
    x1: 230,
    y1: 145,
    x2: 268,
    y2: 131,
    cx: 230,
    cy: 145,
  },
  { key: 'rearLeft', value: props.rearLeft, x1: 110, y1: 327, x2: 72, y2: 341, cx: 110, cy: 327 },
  {
    key: 'rearRight',
    value: props.rearRight,
    x1: 230,
    y1: 327,
    x2: 268,
    y2: 341,
    cx: 230,
    cy: 327,
  },
])

const tyreLabels = computed(() => [
  { key: 'frontLeft', suffix: 'fl', value: props.frontLeft },
  { key: 'frontRight', suffix: 'fr', value: props.frontRight },
  { key: 'rearLeft', suffix: 'rl', value: props.rearLeft },
  { key: 'rearRight', suffix: 'rr', value: props.rearRight },
])

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

const showBattery = computed(() => props.evSocPercent != null)
const showFuel = computed(() => props.fuelLevelPercent != null)

const batteryColor = computed(() => {
  const soc = props.evSocPercent ?? 0
  if (soc >= 60) return 'var(--color-success)'
  if (soc >= 25) return 'var(--color-warning)'
  return 'var(--color-danger)'
})

const fuelColor = computed(() => {
  const pct = props.fuelLevelPercent ?? 0
  if (pct >= 30) return 'var(--color-success)'
  if (pct >= 15) return 'var(--color-warning)'
  return 'var(--color-danger)'
})

const isMoving = computed(() => (props.speed ?? 0) > 0)

// Speedometer gauge: 270° arc opening at the bottom, needle sweeps from
// -135deg (0 km/h) to +135deg (MAX_SPEED_KMH) through the top of the dial.
const MAX_SPEED_KMH = 200
const GAUGE_CX = 50
const GAUGE_CY = 48
const GAUGE_R = 36
const GAUGE_ARC_LENGTH = 1.5 * Math.PI * GAUGE_R

function polarToCartesian(cx: number, cy: number, r: number, bearingDeg: number) {
  const rad = (bearingDeg * Math.PI) / 180
  return { x: cx + r * Math.sin(rad), y: cy - r * Math.cos(rad) }
}

const gaugeArcPath = (() => {
  const start = polarToCartesian(GAUGE_CX, GAUGE_CY, GAUGE_R, -135)
  const end = polarToCartesian(GAUGE_CX, GAUGE_CY, GAUGE_R, 135)
  return `M ${start.x} ${start.y} A ${GAUGE_R} ${GAUGE_R} 0 1 1 ${end.x} ${end.y}`
})()

const showSpeedGauge = computed(() => (props.speed ?? 0) > 0)

const speedFraction = computed(() => {
  const s = props.speed ?? 0
  return Math.min(1, Math.max(0, s / MAX_SPEED_KMH))
})

const needleRotation = computed(() => -135 + speedFraction.value * 270)

const gaugeDashOffset = computed(() => GAUGE_ARC_LENGTH * (1 - speedFraction.value))

const gaugeColor = computed(() => {
  const s = props.speed ?? 0
  if (s >= 140) return 'var(--color-danger)'
  if (s >= 100) return 'var(--color-warning)'
  return 'var(--color-primary-light)'
})

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

const activeLightKey = computed(() => {
  if (props.lightsMainBeam) return 'vehicle.lights.mainBeam'
  if (props.lightsDippedBeam) return 'vehicle.lights.dippedBeam'
  return 'vehicle.lights.side'
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
          <p class="card-info-desc">{{ t('vehicle.diagram.infoTyreDesc') }}</p>
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
            aria-label="Vehicle status diagram"
          >
            <defs>
              <!--
                Beam gradients use userSpaceOnUse so the bright end is anchored at the car's
                front edge (y≈138) where the beam becomes visible, fading toward the tip.
                The polygon bases sit under the car body (y≈140-145) and are masked by the
                car SVG layer on top, giving the effect of light emerging from the headlights.
              -->
              <linearGradient
                id="beam-grad-main"
                gradientUnits="userSpaceOnUse"
                x1="170"
                y1="138"
                x2="170"
                y2="52"
              >
                <stop offset="0%" stop-color="#fef9c3" stop-opacity="0.72" />
                <stop offset="65%" stop-color="#fef9c3" stop-opacity="0.20" />
                <stop offset="100%" stop-color="#fef9c3" stop-opacity="0" />
              </linearGradient>
              <linearGradient
                id="beam-grad-dipped"
                gradientUnits="userSpaceOnUse"
                x1="170"
                y1="138"
                x2="170"
                y2="80"
              >
                <stop offset="0%" stop-color="#fde047" stop-opacity="0.65" />
                <stop offset="65%" stop-color="#fde047" stop-opacity="0.18" />
                <stop offset="100%" stop-color="#fde047" stop-opacity="0" />
              </linearGradient>
              <!-- Side light gradients: radial from front-fender car body edge, fading outward -->
              <radialGradient
                id="beam-grad-side-l"
                gradientUnits="userSpaceOnUse"
                cx="119"
                cy="140"
                r="18"
              >
                <stop offset="0%" stop-color="#fb923c" stop-opacity="0.70" />
                <stop offset="100%" stop-color="#fb923c" stop-opacity="0" />
              </radialGradient>
              <radialGradient
                id="beam-grad-side-r"
                gradientUnits="userSpaceOnUse"
                cx="221"
                cy="140"
                r="18"
              >
                <stop offset="0%" stop-color="#fb923c" stop-opacity="0.70" />
                <stop offset="100%" stop-color="#fb923c" stop-opacity="0" />
              </radialGradient>
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

            <!--
              Light beams drawn behind car. Polygon bases extend into the car body area (y≈140-145)
              so the car SVG layer on top masks them, making beams appear to emerge from the headlights.
              Gradient anchored at y=138 (car front edge) fades toward each tip.
            -->
            <g v-if="hasLights">
              <title>{{ t(activeLightKey) }}</title>
              <template v-if="lightsMainBeam">
                <polygon
                  points="132,145 162,140 148,52 102,62"
                  class="car-light-beam car-light-beam--main"
                />
                <polygon
                  points="208,145 178,140 192,52 238,62"
                  class="car-light-beam car-light-beam--main"
                />
              </template>
              <template v-else-if="lightsDippedBeam">
                <polygon points="130,145 148,140 144,80 118,85" class="car-light-beam" />
                <polygon points="210,145 192,140 196,80 222,85" class="car-light-beam" />
              </template>
              <!--
                Side marker pie-sectors. Centers sit inside car body (y=145) so the car SVG masks
                the source; only the outward crescent is visible. Positioned on the front fender,
                angled forward-and-outward. R=14. 170° arc sweep.
                Lower arm at SVG 90° (straight down from center) lands inside the car body so the
                arc is naturally clipped by the car layer, giving a wide visible crescent.
                Left  – center (121,145), upper arm 260° → (119,131), lower arm 90° → (121,159), CCW sweep=0.
                Right – center (219,145), upper arm 280° → (221,131), lower arm 90° → (219,159), CW  sweep=1.
              -->
              <path
                d="M 121,145 L 119,131 A 14,14 0 0 0 121,159 Z"
                class="car-light-beam--side-l"
              />
              <path
                d="M 219,145 L 221,131 A 14,14 0 0 1 219,159 Z"
                class="car-light-beam--side-r"
              />
            </g>

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

            <!-- Tyre pressure indicator lines -->
            <g v-for="tyre in tyreIndicators" :key="tyre.key">
              <title>
                {{ t(`vehicle.diagram.tyrePosition.${tyre.key}`) }}: {{ fmt(tyre.value) }}
                {{ t('common.bar') }}
              </title>
              <line
                :x1="tyre.x1"
                :y1="tyre.y1"
                :x2="tyre.x2"
                :y2="tyre.y2"
                class="tyre-indicator-line"
                :stroke="pressureColor(tyre.value)"
              />
              <circle
                :cx="tyre.cx"
                :cy="tyre.cy"
                r="5"
                class="tyre-indicator-dot"
                :fill="pressureColor(tyre.value)"
              />
            </g>
          </svg>

          <!-- Tyre pressure labels -->
          <div class="tyre-labels">
            <div
              v-for="label in tyreLabels"
              :key="label.key"
              :class="[
                'tyre-label',
                `tyre-label--${label.suffix}`,
                `tyre-label--${pressureVariant(label.value)}`,
              ]"
              :title="t(`vehicle.diagram.tyrePosition.${label.key}`)"
            >
              {{ fmt(label.value) }} {{ t('common.bar') }}
            </div>
          </div>

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
          <div
            v-if="showFuel"
            class="diagram-level diagram-level--fuel"
            :style="{ '--level-color': fuelColor }"
            :title="`${t('vehicle.fuel')}: ${Math.round(fuelLevelPercent ?? 0)}%`"
          >
            <font-awesome-icon icon="gas-pump" />
            <span>{{ Math.round(fuelLevelPercent ?? 0) }}%</span>
          </div>

          <!-- EV battery (rear) -->
          <div
            v-if="showBattery"
            class="diagram-level diagram-level--battery"
            :class="{
              'diagram-level--charging': isCharging,
              'diagram-level--connected': chargerConnected && !isCharging,
            }"
            :style="{ '--level-color': batteryColor }"
            :title="`${t('vehicle.evSoc')}: ${Math.round(evSocPercent ?? 0)}%`"
          >
            <font-awesome-icon icon="bolt" />
            <span>{{ Math.round(evSocPercent ?? 0) }}%</span>
          </div>

          <div
            v-if="showSpeedGauge"
            class="speed-gauge"
            role="img"
            :aria-label="`${t('vehicle.speed')}: ${Math.round(speed ?? 0)} km/h`"
            :title="`${t('vehicle.speed')}: ${Math.round(speed ?? 0)} km/h`"
          >
            <svg viewBox="0 0 100 92" class="speed-gauge__svg">
              <path :d="gaugeArcPath" class="speed-gauge__track" />
              <path
                :d="gaugeArcPath"
                class="speed-gauge__progress"
                :style="{
                  strokeDasharray: GAUGE_ARC_LENGTH,
                  strokeDashoffset: gaugeDashOffset,
                  stroke: gaugeColor,
                }"
              />
              <line
                x1="50"
                y1="48"
                x2="50"
                y2="20"
                class="speed-gauge__needle"
                :style="{ transform: `rotate(${needleRotation}deg)` }"
              />
              <circle cx="50" cy="48" r="3" class="speed-gauge__pivot" />
              <text x="50" y="64" text-anchor="middle" class="speed-gauge__value">
                {{ Math.round(speed ?? 0) }}
              </text>
              <text x="50" y="78" text-anchor="middle" class="speed-gauge__unit">km/h</text>
            </svg>
          </div>
        </div>
      </div>
    </div>
  </CardInfoWrap>
</template>
