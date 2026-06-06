<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { computed } from 'vue'
import CardInfoWrap from './CardInfoWrap.vue'

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
          ViewBox: 0 0 340 480
          Orange car: nested SVG, original bounds x≈137–245 y≈237–445
            placed at x=105 y=115 width=130 height=250 in parent coords.
          Wheel corners (parent): FL(127,123) FR(213,123) RL(127,349) RR(213,349)
          Dots at outer side of each wheel: FL(110,145) FR(230,145) RL(110,327) RR(230,327)
          Labels at (~20° from horizontal):  FL→(72,131) FR→(268,131) RL→(72,341) RR→(268,341)
          Side light cones: left x=72–118 y=137–147 / right x=222–268 y=137–147
          Door icon badges: see .car-badge-* CSS classes
        -->
          <svg
            viewBox="0 0 340 480"
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
            </defs>

            <!--
              Light beams drawn behind car. Polygon bases extend into the car body area (y≈140-145)
              so the car SVG layer on top masks them, making beams appear to emerge from the headlights.
              Gradient anchored at y=138 (car front edge) fades toward each tip.
            -->
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
            <template v-if="lightsSide || lightsDippedBeam || lightsMainBeam">
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
            </template>

            <!-- Orange car illustration (top-view) -->
            <svg viewBox="137 237 108 208" x="105" y="115" width="130" height="250">
              <path
                class="car-svg-body"
                d="M148.853,256.308c-3.086,10.185-2.419,27.602-1.841,36.201c0.227,3.379,0.242,6.767,0.047,10.148
              c-1.162,20.18-0.234,55.6,0.352,73.267c0.205,6.167-0.378,12.325-1.6,18.373c-4.588,22.71,4.517,34.219,4.517,34.219
              c6.428,9.208,30.042,9.251,38.142,8.982c1.834-0.061,3.664-0.061,5.498,0c8.101,0.269,31.714,0.226,38.142-8.982
              c0,0,9.105-11.51,4.517-34.219c-1.222-6.048-1.805-12.206-1.6-18.373c0.586-17.666,1.515-53.087,0.352-73.267
              c-0.195-3.381-0.18-6.769,0.047-10.148c0.579-8.599,1.246-26.016-1.841-36.201c-2.951-9.738-12.719-15.138-25.182-17.936
              c-11.305-2.538-23.065-2.538-34.37,0C161.572,241.17,151.804,246.57,148.853,256.308z"
              />
              <path
                class="car-svg-detail"
                d="M159.625,414.852l1.875-1.298c0,0,29.72,15.457,58.246,0l2.744,1.591
              c0.938,0.544,0.899,1.909-0.068,2.398c-7.671,3.885-34.543,15.181-62.686-0.376
              C158.852,416.679,158.795,415.427,159.625,414.852z"
              />
              <path
                class="car-svg-accent"
                d="M203.663,239.819c-8.311-1.758-16.897-1.74-25.203,0.042c-3.687,0.791-7.485,1.698-10.36,2.606
              c-8.01,2.529-17.284,8.853-17.284,32.46s9.478,51.852,9.478,51.852v82.012c0,3.219,1.911,6.142,4.874,7.399
              c2.41,1.022,5.042,1.859,7.676,2.542c12.003,3.115,24.633,3.11,36.643,0.019c2.67-0.687,5.339-1.53,7.779-2.562
              c2.967-1.255,4.881-4.18,4.881-7.402V326.78c0,0,9.478-28.245,9.478-51.852s-9.274-29.931-17.284-32.46
              C211.493,241.568,207.519,240.635,203.663,239.819z"
              />
              <path
                class="car-svg-detail"
                d="M167.369,324.598c8.391-1.527,24.556-3.16,47.12,0.427c3.544,0.563,6.893-1.804,7.527-5.336l5.6-31.2
              c0.447-2.49-0.563-5.023-2.618-6.497c-4.428-3.178-14.184-7.696-33.861-7.696c-19.243,0-28.912,3.982-33.413,6.896
              c-2.18,1.412-3.324,3.973-2.925,6.539l4.886,31.401C160.255,322.792,163.725,325.261,167.369,324.598z"
              />
              <path
                class="car-svg-dark"
                d="M148.038,259.565c0,0,15.088-1.268,20.269-19.652C168.307,239.913,149.534,243.23,148.038,259.565z"
              />
              <path
                class="car-svg-dark"
                d="M234.446,259.565c0,0-15.088-1.268-20.269-19.652C214.177,239.913,232.951,243.23,234.446,259.565z"
              />
              <path
                class="car-svg-detail"
                d="M151.474,293.613c1.004,6.686,2.605,15.614,4.938,23.987c1.732,6.215,2.678,12.623,2.706,19.075
              l0.262,60.063c0.005,1.233-0.672,2.368-1.76,2.949c-2.136,1.139-4.734-0.31-4.884-2.726
              c-1.009-16.313-3.937-67.82-3.043-103.238C149.72,292.629,151.312,292.531,151.474,293.613z"
              />
              <path
                class="car-svg-detail"
                d="M231.396,293.613c-1.004,6.686-2.605,15.614-4.938,23.987c-1.732,6.215-2.678,12.623-2.706,19.075
              l-0.262,60.063c-0.005,1.233,0.672,2.368,1.76,2.949c2.136,1.139,4.734-0.31,4.884-2.726
              c1.009-16.313,3.937-67.82,3.043-103.238C233.15,292.629,231.559,292.531,231.396,293.613z"
              />
              <path
                class="car-svg-accent"
                d="M231.819,298.965l0.804,3.005c0.357,1.333,1.565,2.26,2.945,2.26l9.568,0.001
              c0.652,0,1.103-0.673,0.832-1.266c-0.992-2.169-4.124-6.248-13.462-5.015
              C231.017,298.014,231.692,298.489,231.819,298.965z"
              />
              <path
                class="car-svg-accent"
                d="M150.788,298.965l-0.804,3.005c-0.357,1.333-1.565,2.26-2.944,2.26l-9.568,0.001
              c-0.652,0-1.103-0.673-0.832-1.266c0.992-2.169,4.124-6.248,13.462-5.015
              C150.59,298.014,150.916,298.489,150.788,298.965z"
              />
              <path
                class="car-svg-red"
                d="M152.096,414.234c0,0,4.647,10.488,16.843,13.129c0,0-6.735,4.056-14.179,0.391
              c-2.935-1.445-5.01-4.275-5.283-7.535C149.307,418.194,149.824,415.956,152.096,414.234z"
              />
              <path
                class="car-svg-red"
                d="M230.326,414.217c0,0-4.621,10.5-16.811,13.17c0,0,6.745,4.04,14.18,0.357
              c2.931-1.452,4.999-4.288,5.264-7.548C233.125,418.17,232.602,415.933,230.326,414.217z"
              />
              <path
                class="car-svg-highlight"
                d="M170.175,411.232c-0.81,0-1.466-0.656-1.466-1.466v-58.24c0-4.476-0.492-8.962-1.463-13.332
              l-1.433-6.45c-0.176-0.79,0.323-1.573,1.113-1.749c0.788-0.175,1.573,0.323,1.749,1.113l1.433,6.45
              c1.017,4.579,1.533,9.278,1.533,13.967v58.24C171.641,410.576,170.985,411.232,170.175,411.232z"
              />
              <path
                class="car-svg-highlight"
                d="M212.05,411.232c0.81,0,1.466-0.656,1.466-1.466v-58.24c0-4.476,0.492-8.962,1.463-13.332
              l1.433-6.45c0.176-0.79-0.322-1.573-1.113-1.749c-0.788-0.175-1.573,0.323-1.749,1.113l-1.433,6.45
              c-1.017,4.579-1.533,9.278-1.533,13.967v58.24C210.584,410.576,211.24,411.232,212.05,411.232z"
              />
              <path
                class="car-svg-body"
                d="M182.903,332.804v77.908c0,1.563-1.279,2.842-2.842,2.842h-0.217c-1.563,0-2.842-1.279-2.842-2.842
              v-77.908c0-1.563,1.279-2.842,2.842-2.842h0.217C181.624,329.962,182.903,331.241,182.903,332.804z"
              />
              <path
                class="car-svg-body"
                d="M194.171,332.804v77.908c0,1.563-1.279,2.842-2.842,2.842h-0.217c-1.563,0-2.842-1.279-2.842-2.842
              v-77.908c0-1.563,1.279-2.842,2.842-2.842h0.217C192.892,329.962,194.171,331.241,194.171,332.804z"
              />
              <path
                class="car-svg-body"
                d="M205.439,332.804v77.908c0,1.563-1.279,2.842-2.842,2.842h-0.217c-1.563,0-2.842-1.279-2.842-2.842
              v-77.908c0-1.563,1.279-2.842,2.842-2.842h0.217C204.16,329.962,205.439,331.241,205.439,332.804z"
              />
              <path
                class="car-svg-detail"
                d="M227.263,244.576c0,0.484-0.119,0.974-0.37,1.427c-0.79,1.426-2.585,1.941-4.011,1.152
              c-30.501-16.894-63.047-0.731-63.373-0.566c-1.453,0.738-3.229,0.159-3.967-1.294c-0.739-1.452-0.159-3.229,1.294-3.968
              c0.363-0.184,9.031-4.538,21.839-6.665c17.027-2.828,33.303-0.293,47.067,7.33
              C226.714,242.531,227.263,243.538,227.263,244.576z"
              />
              <path
                class="car-svg-detail"
                d="M227.263,432.237c0-0.484-0.119-0.974-0.37-1.427c-0.79-1.426-2.585-1.941-4.011-1.152
              c-30.501,16.894-63.047,0.731-63.373,0.566c-1.453-0.738-3.229-0.159-3.967,1.294c-0.739,1.452-0.159,3.229,1.294,3.968
              c0.363,0.184,9.031,4.538,21.839,6.665c17.027,2.828,33.303,0.293,47.067-7.33
              C226.714,434.282,227.263,433.275,227.263,432.237z"
              />
              <polygon
                class="car-svg-gloss"
                points="162.733,278.637 183.308,322.957 178.233,323.238 158.579,280.664"
              />
              <polygon
                class="car-svg-gloss"
                points="171.301,276.097 192.896,322.939 187.604,322.87 166.362,277.358"
              />
            </svg>

            <!-- Charging cable: rear bumper → charger off-screen below.
                 Path direction is bottom→top so dashoffset animation flows toward the car. -->
            <g
              v-if="chargerConnected || isCharging"
              class="charge-group"
              :class="{ 'charge-group--active': isCharging }"
            >
              <circle cx="170" cy="367" r="16" class="charge-entry-glow" />
              <rect x="162" y="362" width="16" height="8" rx="2" class="charge-socket" />
              <!-- Cable enters the battery badge from below; endpoint inside badge so nothing shows under it -->
              <path d="M 170,440 L 170,370" class="charge-cable" />
            </g>

            <!-- Tyre pressure indicator lines -->
            <line
              x1="110"
              y1="145"
              x2="72"
              y2="131"
              class="tyre-indicator-line"
              :stroke="pressureColor(frontLeft)"
            />
            <circle
              cx="110"
              cy="145"
              r="5"
              class="tyre-indicator-dot"
              :fill="pressureColor(frontLeft)"
            />
            <line
              x1="230"
              y1="145"
              x2="268"
              y2="131"
              class="tyre-indicator-line"
              :stroke="pressureColor(frontRight)"
            />
            <circle
              cx="230"
              cy="145"
              r="5"
              class="tyre-indicator-dot"
              :fill="pressureColor(frontRight)"
            />
            <line
              x1="110"
              y1="327"
              x2="72"
              y2="341"
              class="tyre-indicator-line"
              :stroke="pressureColor(rearLeft)"
            />
            <circle
              cx="110"
              cy="327"
              r="5"
              class="tyre-indicator-dot"
              :fill="pressureColor(rearLeft)"
            />
            <line
              x1="230"
              y1="327"
              x2="268"
              y2="341"
              class="tyre-indicator-line"
              :stroke="pressureColor(rearRight)"
            />
            <circle
              cx="230"
              cy="327"
              r="5"
              class="tyre-indicator-dot"
              :fill="pressureColor(rearRight)"
            />
          </svg>

          <!-- Tyre pressure labels -->
          <div class="tyre-labels">
            <div
              class="tyre-label tyre-label--fl"
              :class="`tyre-label--${pressureVariant(frontLeft)}`"
            >
              {{ fmt(frontLeft) }} {{ t('common.bar') }}
            </div>
            <div
              class="tyre-label tyre-label--fr"
              :class="`tyre-label--${pressureVariant(frontRight)}`"
            >
              {{ fmt(frontRight) }} {{ t('common.bar') }}
            </div>
            <div
              class="tyre-label tyre-label--rl"
              :class="`tyre-label--${pressureVariant(rearLeft)}`"
            >
              {{ fmt(rearLeft) }} {{ t('common.bar') }}
            </div>
            <div
              class="tyre-label tyre-label--rr"
              :class="`tyre-label--${pressureVariant(rearRight)}`"
            >
              {{ fmt(rearRight) }} {{ t('common.bar') }}
            </div>
          </div>

          <!-- Open panel icon badges – positioned over SVG at each panel location -->
          <div
            v-if="bonnetOpen"
            class="car-badge car-badge--bonnet"
            :title="t('vehicle.doors_detail.bonnet')"
          >
            <font-awesome-icon icon="lock-open" />
          </div>
          <div
            v-if="driverDoorOpen"
            class="car-badge car-badge--door-fl"
            :title="t('vehicle.doors_detail.driver')"
          >
            <font-awesome-icon icon="lock-open" />
          </div>
          <div
            v-if="passengerDoorOpen"
            class="car-badge car-badge--door-fr"
            :title="t('vehicle.doors_detail.passenger')"
          >
            <font-awesome-icon icon="lock-open" />
          </div>
          <div
            v-if="rearLeftDoorOpen"
            class="car-badge car-badge--door-rl"
            :title="t('vehicle.doors_detail.rearLeft')"
          >
            <font-awesome-icon icon="lock-open" />
          </div>
          <div
            v-if="rearRightDoorOpen"
            class="car-badge car-badge--door-rr"
            :title="t('vehicle.doors_detail.rearRight')"
          >
            <font-awesome-icon icon="lock-open" />
          </div>
          <div
            v-if="trunkOpen"
            class="car-badge car-badge--trunk"
            :title="t('vehicle.doors_detail.boot')"
          >
            <font-awesome-icon icon="lock-open" />
          </div>

          <!-- Fuel level (front) -->
          <div
            v-if="showFuel"
            class="diagram-level diagram-level--fuel"
            :style="{ '--level-color': fuelColor }"
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
          >
            <font-awesome-icon icon="bolt" />
            <span>{{ Math.round(evSocPercent ?? 0) }}%</span>
          </div>
        </div>
      </div>

      <div class="tyre-legend">
        <span class="tyre-legend__item tyre-legend__item--ok">&#x2265; 2.6 bar</span>
        <span class="tyre-legend__item tyre-legend__item--warning">2.2 – 2.6 bar</span>
        <span class="tyre-legend__item tyre-legend__item--danger">&lt; 2.2 bar</span>
      </div>

      <div v-if="hasLights || isCharging || chargerConnected" class="vehicle-state-legend">
        <span
          v-if="hasLights"
          class="vehicle-state-legend__item vehicle-state-legend__item--lights"
        >
          <font-awesome-icon icon="lightbulb" />
          {{ t(activeLightKey) }}
        </span>
        <span
          v-if="isCharging"
          class="vehicle-state-legend__item vehicle-state-legend__item--charging"
        >
          <font-awesome-icon icon="bolt" />
          {{ t('vehicle.chargingYes') }}
        </span>
        <span
          v-else-if="chargerConnected"
          class="vehicle-state-legend__item vehicle-state-legend__item--connected"
        >
          <font-awesome-icon icon="plug" />
          {{ t('vehicle.hvBattery.pluggedIn') }}
        </span>
      </div>
    </div>
  </CardInfoWrap>
</template>
