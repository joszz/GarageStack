<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useSettingsStore } from '@/stores/settings'
import gbFlag from 'flag-icons/flags/4x3/gb.svg'
import nlFlag from 'flag-icons/flags/4x3/nl.svg'
import { useVehicleStore } from '@/stores/vehicle'
import { useVehicleCommand } from '@/composables/useVehicleCommand'
import { usePush } from '@/composables/usePush'
import { useModal } from '@/composables/useModal'
import DetailModal from './DetailModal.vue'
import type { VehicleTypeOverride } from '@/stores/settings'
import { CAR_COLOR_SCHEMES } from '@/stores/settings'

const { t } = useI18n()
const settings = useSettingsStore()
const vehicleStore = useVehicleStore()
const { sending, send } = useVehicleCommand()
const { pushSupported, pushState, togglePush } = usePush()
const { isOpen: modalOpen, open: openModal, close: closeModal } = useModal()

const vin = computed(() => vehicleStore.vehicles[0]?.vin ?? null)

const detectedLabel = computed(() => {
  const type = vehicleStore.detectedVehicleType
  if (type === 'unknown') return null
  return type.toUpperCase()
})

const hwVersion = computed(() => vehicleStore.vehicleConfig['hw_version'] ?? null)

const typeOptions: { value: VehicleTypeOverride; label: string }[] = [
  { value: 'auto', label: t('settings.vehicleType.auto') },
  { value: 'hev', label: t('settings.vehicleType.hev') },
  { value: 'phev', label: t('settings.vehicleType.phev') },
  { value: 'bev', label: t('settings.vehicleType.bev') },
]

const isLightTheme = computed({
  get: () => settings.theme === 'light',
  set: (val: boolean) => {
    settings.theme = val ? 'light' : 'dark'
  },
})

const isNL = computed({
  get: () => settings.locale === 'nl',
  set: (val: boolean) => {
    settings.locale = val ? 'nl' : 'en'
  },
})

function refresh() {
  if (!vin.value) return
  send(vin.value, 'refresh', 'force')
}
</script>

<template>
  <footer class="app-footer">
    <a
      class="app-footer__copyright"
      href="https://github.com/joszz/GarageStack"
      target="_blank"
      rel="noopener"
      >&copy; 2026 GarageStack</a
    >
    <div class="app-footer__actions">
      <button
        class="app-footer__btn"
        :aria-label="t('control.refresh')"
        :disabled="vehicleStore.loading || sending === 'refresh' || !vin"
        @click="refresh"
      >
        <font-awesome-icon icon="rotate" :spin="vehicleStore.loading || sending === 'refresh'" />
        {{ t('control.refresh') }}
      </button>
      <button class="app-footer__btn" :aria-label="t('settings.title')" @click="openModal">
        <font-awesome-icon icon="gear" />
        {{ t('settings.title') }}
      </button>
    </div>
  </footer>

  <DetailModal :open="modalOpen" :title="t('settings.title')" @close="closeModal">
    <!-- Appearance -->
    <div class="detail-modal__section">
      <div class="detail-modal__section-title">{{ t('settings.theme.title') }}</div>
      <div class="settings-toggles">
        <div class="settings-toggle">
          <div class="settings-toggle__info">
            <span class="settings-toggle__label">{{
              isLightTheme ? t('settings.theme.light') : t('settings.theme.dark')
            }}</span>
            <span class="settings-toggle__desc text-muted">{{
              t('settings.theme.lightDesc')
            }}</span>
          </div>
          <div class="settings-toggle__control">
            <span
              class="settings-toggle__side-icon"
              :class="{ 'settings-toggle__side-icon--active': !isLightTheme }"
            >
              <font-awesome-icon icon="moon" />
            </span>
            <div class="form-check form-switch mb-0">
              <input
                id="footer-toggle-theme"
                v-model="isLightTheme"
                class="form-check-input"
                type="checkbox"
                role="switch"
              />
            </div>
            <span
              class="settings-toggle__side-icon"
              :class="{ 'settings-toggle__side-icon--active': isLightTheme }"
            >
              <font-awesome-icon icon="sun" />
            </span>
          </div>
        </div>
      </div>
    </div>

    <!-- Dashboard -->
    <div class="detail-modal__section">
      <div class="detail-modal__section-title">{{ t('settings.dashboard.title') }}</div>
      <div class="settings-toggles">
        <div class="settings-toggle">
          <div class="settings-toggle__info">
            <span class="settings-toggle__label">{{ t('settings.dashboard.cardInfoIcons') }}</span>
            <span class="settings-toggle__desc text-muted">{{
              t('settings.dashboard.cardInfoIconsDesc')
            }}</span>
          </div>
          <div class="settings-toggle__control">
            <div class="form-check form-switch mb-0">
              <input
                id="footer-toggle-card-info-icons"
                v-model="settings.showCardInfoIcons"
                class="form-check-input"
                type="checkbox"
                role="switch"
              />
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Language -->
    <div class="detail-modal__section">
      <div class="detail-modal__section-title">{{ t('settings.language.title') }}</div>
      <div class="settings-toggles">
        <div class="settings-toggle">
          <div class="settings-toggle__info">
            <span class="settings-toggle__label">{{ isNL ? 'Nederlands' : 'English' }}</span>
          </div>
          <div class="settings-toggle__control">
            <span
              class="settings-toggle__side-icon settings-toggle__side-icon--flag"
              :class="{ 'settings-toggle__side-icon--active': !isNL }"
            >
              <img :src="gbFlag" alt="English" class="settings-toggle__flag" />
            </span>
            <div class="form-check form-switch mb-0">
              <input
                id="footer-toggle-lang"
                v-model="isNL"
                class="form-check-input"
                type="checkbox"
                role="switch"
              />
            </div>
            <span
              class="settings-toggle__side-icon settings-toggle__side-icon--flag"
              :class="{ 'settings-toggle__side-icon--active': isNL }"
            >
              <img :src="nlFlag" alt="Nederlands" class="settings-toggle__flag" />
            </span>
          </div>
        </div>
      </div>
    </div>

    <!-- Car colour -->
    <div class="detail-modal__section">
      <div class="detail-modal__section-title">{{ t('settings.carColor.title') }}</div>
      <div class="car-color-picker">
        <span class="settings-toggle__label">{{
          t(`settings.carColor.${settings.carColorScheme}`)
        }}</span>
        <div class="car-color-swatches">
          <button
            v-for="scheme in CAR_COLOR_SCHEMES"
            :key="scheme.id"
            class="car-color-swatch"
            :class="{ 'car-color-swatch--active': settings.carColorScheme === scheme.id }"
            :title="t(`settings.carColor.${scheme.id}`)"
            :aria-label="t(`settings.carColor.${scheme.id}`)"
            :style="{ '--swatch-p': scheme.primary, '--swatch-s': scheme.secondary }"
            @click="settings.carColorScheme = scheme.id"
          />
        </div>
      </div>
    </div>

    <!-- Vehicle type -->
    <div class="detail-modal__section">
      <div class="detail-modal__section-title">{{ t('settings.vehicleType.title') }}</div>
      <div class="vehicle-type-row">
        <div class="vehicle-type-detected">
          <span class="text-muted">{{ t('settings.vehicleType.detected') }}:</span>
          <span v-if="detectedLabel" class="badge badge-info ms-2">{{ detectedLabel }}</span>
          <span v-if="hwVersion" class="text-muted ms-2 text-xs">({{ hwVersion }})</span>
          <span v-if="!detectedLabel" class="text-muted ms-2">{{
            t('settings.vehicleType.notDetected')
          }}</span>
        </div>
        <div class="vehicle-type-override">
          <label class="text-muted">{{ t('settings.vehicleType.override') }}</label>
          <select v-model="settings.vehicleTypeOverride" class="form-select form-select-sm">
            <option v-for="opt in typeOptions" :key="opt.value" :value="opt.value">
              {{ opt.label }}
            </option>
          </select>
        </div>
      </div>
    </div>

    <!-- Push notifications -->
    <div v-if="pushSupported" class="detail-modal__section">
      <div class="detail-modal__section-title">{{ t('notifications.title') }}</div>
      <div class="settings-toggles">
        <div class="settings-toggle">
          <div class="settings-toggle__info">
            <span class="settings-toggle__label">{{ t('push.enable') }}</span>
            <span class="settings-toggle__desc text-muted">{{ t('push.desc') }}</span>
          </div>
          <div class="settings-toggle__control">
            <span v-if="pushState === 'denied'" class="text-danger text-sm">{{
              t('push.permissionDenied')
            }}</span>
            <div v-else class="form-check form-switch mb-0">
              <input
                id="footer-toggle-push"
                :checked="pushState === 'subscribed'"
                class="form-check-input"
                type="checkbox"
                role="switch"
                @change="togglePush"
              />
            </div>
          </div>
        </div>
      </div>
    </div>
  </DetailModal>
</template>
