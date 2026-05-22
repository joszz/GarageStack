<script setup lang="ts">
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

const props = defineProps<{
  frontLeft: number | null
  frontRight: number | null
  rearLeft: number | null
  rearRight: number | null
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
</script>

<template>
  <div class="tyre-diagram">
    <p class="tyre-diagram__title">
      <font-awesome-icon icon="circle" />
      {{ t('vehicle.tyres') }}
    </p>
    <div class="tyre-diagram__wrap">
      <div class="tyre-car-wrap">
        <svg viewBox="0 0 220 340" xmlns="http://www.w3.org/2000/svg" class="tyre-diagram__svg" aria-label="Tyre pressure diagram">
          <!-- Car body -->
          <rect x="55" y="60" width="110" height="220" rx="20" ry="20" class="car-body" />
          <!-- Windscreen -->
          <rect x="70" y="75" width="80" height="45" rx="6" class="car-glass" />
          <!-- Rear window -->
          <rect x="70" y="225" width="80" height="35" rx="6" class="car-glass" />
          <!-- Front left tyre -->
          <rect x="10" y="65" width="38" height="62" rx="8"
            :fill="pressureColor(props.frontLeft)" class="tyre" stroke-width="2.5" />
          <!-- Front right tyre -->
          <rect x="172" y="65" width="38" height="62" rx="8"
            :fill="pressureColor(props.frontRight)" class="tyre" stroke-width="2.5" />
          <!-- Rear left tyre -->
          <rect x="10" y="213" width="38" height="62" rx="8"
            :fill="pressureColor(props.rearLeft)" class="tyre" stroke-width="2.5" />
          <!-- Rear right tyre -->
          <rect x="172" y="213" width="38" height="62" rx="8"
            :fill="pressureColor(props.rearRight)" class="tyre" stroke-width="2.5" />
        </svg>

        <!-- Pressure labels overlaid on SVG -->
        <div class="tyre-labels">
          <div class="tyre-label tyre-label--fl" :class="`tyre-label--${pressureVariant(props.frontLeft)}`">
            {{ fmt(props.frontLeft) }} {{ t('common.bar') }}
          </div>
          <div class="tyre-label tyre-label--fr" :class="`tyre-label--${pressureVariant(props.frontRight)}`">
            {{ fmt(props.frontRight) }} {{ t('common.bar') }}
          </div>
          <div class="tyre-label tyre-label--rl" :class="`tyre-label--${pressureVariant(props.rearLeft)}`">
            {{ fmt(props.rearLeft) }} {{ t('common.bar') }}
          </div>
          <div class="tyre-label tyre-label--rr" :class="`tyre-label--${pressureVariant(props.rearRight)}`">
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
  </div>
</template>
