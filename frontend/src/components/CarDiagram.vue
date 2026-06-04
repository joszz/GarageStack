<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { computed } from 'vue'

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

const hasOpenParts = computed(
  () =>
    props.driverDoorOpen ||
    props.passengerDoorOpen ||
    props.rearLeftDoorOpen ||
    props.rearRightDoorOpen ||
    props.trunkOpen ||
    props.bonnetOpen,
)

const showBattery = computed(() => props.evSocPercent != null)

const batteryFillColor = computed(() => {
  const soc = props.evSocPercent ?? 0
  if (soc >= 60) return 'var(--color-success)'
  if (soc >= 25) return 'var(--color-warning)'
  return 'var(--color-danger)'
})

const headlightClass = computed(() => {
  if (props.lightsMainBeam) return 'car-headlight car-headlight--main'
  if (props.lightsDippedBeam) return 'car-headlight car-headlight--dipped'
  return 'car-headlight car-headlight--side'
})

const activeLightKey = computed(() => {
  if (props.lightsMainBeam) return 'vehicle.lights.mainBeam'
  if (props.lightsDippedBeam) return 'vehicle.lights.dippedBeam'
  return 'vehicle.lights.side'
})
</script>

<template>
  <div class="tyre-diagram">
    <p class="tyre-diagram__title">
      <font-awesome-icon icon="car-side" />
      {{ t('vehicle.overview') }}
    </p>
    <div class="tyre-diagram__wrap">
      <div class="tyre-car-wrap">
        <svg
          viewBox="0 0 220 340"
          xmlns="http://www.w3.org/2000/svg"
          class="tyre-diagram__svg"
          aria-label="Vehicle status diagram"
        >
          <!-- Headlight beam projections (behind everything) -->
          <template v-if="lightsMainBeam">
            <polygon points="58,58 76,58 86,12 47,16" class="car-light-beam car-light-beam--main" />
            <polygon
              points="144,58 162,58 173,16 134,12"
              class="car-light-beam car-light-beam--main"
            />
          </template>
          <template v-else-if="lightsDippedBeam">
            <polygon points="58,58 76,58 70,28 56,26" class="car-light-beam" />
            <polygon points="144,58 162,58 164,26 150,28" class="car-light-beam" />
          </template>

          <!-- Bonnet open -->
          <polygon v-if="bonnetOpen" points="70,60 150,60 148,28 72,28" class="car-panel--open" />

          <!-- Trunk open -->
          <polygon
            v-if="trunkOpen"
            points="72,280 148,280 150,312 70,312"
            class="car-panel--open"
          />

          <!-- Door open shapes (drawn before car body so the car edge covers overlap) -->
          <polygon
            v-if="driverDoorOpen"
            points="55,122 14,126 14,172 55,172"
            class="car-panel--open"
          />
          <polygon
            v-if="passengerDoorOpen"
            points="165,122 206,126 206,172 165,172"
            class="car-panel--open"
          />
          <polygon
            v-if="rearLeftDoorOpen"
            points="55,172 14,172 14,222 55,222"
            class="car-panel--open"
          />
          <polygon
            v-if="rearRightDoorOpen"
            points="165,172 206,172 206,222 165,222"
            class="car-panel--open"
          />

          <!-- Car body -->
          <rect x="55" y="60" width="110" height="220" rx="20" ry="20" class="car-body" />
          <!-- Windscreen -->
          <rect x="70" y="75" width="80" height="45" rx="6" class="car-glass" />
          <!-- Rear window -->
          <rect x="70" y="225" width="80" height="35" rx="6" class="car-glass" />

          <!-- Headlight indicator bars on the front edge -->
          <template v-if="hasLights">
            <rect x="58" y="57" width="18" height="6" rx="3" :class="headlightClass" />
            <rect x="144" y="57" width="18" height="6" rx="3" :class="headlightClass" />
          </template>

          <!-- EV battery indicator -->
          <template v-if="showBattery">
            <rect x="85" y="157" width="50" height="20" rx="4" class="car-battery-outer" />
            <rect x="135" y="162" width="6" height="10" rx="2" class="car-battery-terminal" />
            <rect
              x="87"
              y="159"
              :width="Math.max(2, 46 * ((props.evSocPercent ?? 0) / 100))"
              height="16"
              rx="3"
              :fill="batteryFillColor"
            />
            <text
              x="110"
              y="167"
              text-anchor="middle"
              dominant-baseline="middle"
              class="car-battery-text"
            >
              {{ Math.round(props.evSocPercent ?? 0) }}%
            </text>
          </template>

          <!-- Tyres (rendered last so they sit on top) -->
          <rect
            x="10"
            y="65"
            width="38"
            height="62"
            rx="8"
            :fill="pressureColor(props.frontLeft)"
            class="tyre"
            stroke-width="2.5"
          />
          <rect
            x="172"
            y="65"
            width="38"
            height="62"
            rx="8"
            :fill="pressureColor(props.frontRight)"
            class="tyre"
            stroke-width="2.5"
          />
          <rect
            x="10"
            y="213"
            width="38"
            height="62"
            rx="8"
            :fill="pressureColor(props.rearLeft)"
            class="tyre"
            stroke-width="2.5"
          />
          <rect
            x="172"
            y="213"
            width="38"
            height="62"
            rx="8"
            :fill="pressureColor(props.rearRight)"
            class="tyre"
            stroke-width="2.5"
          />
        </svg>

        <!-- Pressure labels overlaid on SVG -->
        <div class="tyre-labels">
          <div
            class="tyre-label tyre-label--fl"
            :class="`tyre-label--${pressureVariant(props.frontLeft)}`"
          >
            {{ fmt(props.frontLeft) }} {{ t('common.bar') }}
          </div>
          <div
            class="tyre-label tyre-label--fr"
            :class="`tyre-label--${pressureVariant(props.frontRight)}`"
          >
            {{ fmt(props.frontRight) }} {{ t('common.bar') }}
          </div>
          <div
            class="tyre-label tyre-label--rl"
            :class="`tyre-label--${pressureVariant(props.rearLeft)}`"
          >
            {{ fmt(props.rearLeft) }} {{ t('common.bar') }}
          </div>
          <div
            class="tyre-label tyre-label--rr"
            :class="`tyre-label--${pressureVariant(props.rearRight)}`"
          >
            {{ fmt(props.rearRight) }} {{ t('common.bar') }}
          </div>
        </div>
      </div>
    </div>

    <div class="tyre-legend">
      <span class="tyre-legend__item tyre-legend__item--ok">≥ 2.6 bar</span>
      <span class="tyre-legend__item tyre-legend__item--warning">2.2 – 2.6 bar</span>
      <span class="tyre-legend__item tyre-legend__item--danger">&lt; 2.2 bar</span>
    </div>

    <div v-if="hasOpenParts || hasLights" class="vehicle-state-legend">
      <span v-if="hasOpenParts" class="vehicle-state-legend__item vehicle-state-legend__item--open">
        <font-awesome-icon icon="door-open" />
        {{ t('vehicle.diagram.panelOpen') }}
      </span>
      <span v-if="hasLights" class="vehicle-state-legend__item vehicle-state-legend__item--lights">
        <font-awesome-icon icon="lightbulb" />
        {{ t(activeLightKey) }}
      </span>
    </div>
  </div>
</template>
