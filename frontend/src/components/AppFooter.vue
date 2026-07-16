<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import Multiselect from '@vueform/multiselect'
import { useUiSettingsStore } from '@/stores/settingsUi'
import gbFlag from 'flag-icons/flags/4x3/gb.svg'
import nlFlag from 'flag-icons/flags/4x3/nl.svg'
import { useVehicleStore } from '@/stores/vehicle'
import { useVehicleCommand } from '@/composables/useVehicleCommand'
import { useNotificationPushSync } from '@/composables/useNotificationPushSync'
import { useModal } from '@/composables/useModal'
import DetailModal from './DetailModal.vue'
import SettingsToggle from './SettingsToggle.vue'
import type { VehicleTypeOverride } from '@/stores/settingsUi'
import { CAR_COLOR_SCHEMES } from '@/stores/settingsUi'
import {
  NOTIFICATION_CATEGORY_IDS,
  type NotificationCategoryId,
} from '@/utils/notificationCategories'

const { t } = useI18n()
const settings = useUiSettingsStore()
const vehicleStore = useVehicleStore()
const { sending, send } = useVehicleCommand()
const { showPermissionDeniedNotice } = useNotificationPushSync()
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

const NOTIFICATION_CATEGORY_LABEL_KEYS: Record<NotificationCategoryId, string> = {
  'low-tyre': 'lowTyre',
  'high-tyre': 'highTyre',
  'low-ev': 'lowEv',
  'charging-complete': 'chargingComplete',
  'engine-start': 'engineStart',
  'unlocked-parked': 'unlockedParked',
  'doors-open-parked': 'doorsOpenParked',
  'windows-open-parked': 'windowsOpenParked',
  maintenance: 'maintenance',
}

const notificationTypeOptions = computed(() =>
  NOTIFICATION_CATEGORY_IDS.map((id) => ({
    value: id,
    label: t(`notifications.categories.${NOTIFICATION_CATEGORY_LABEL_KEYS[id]}`),
  })),
)

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

function selectAllNotificationTypes() {
  settings.notificationTypeExclusions = []
}

function deselectAllNotificationTypes() {
  settings.notificationTypeExclusions = [...NOTIFICATION_CATEGORY_IDS]
}

function toggleNotificationType(id: NotificationCategoryId, checked: boolean) {
  settings.notificationTypeExclusions = checked
    ? settings.notificationTypeExclusions.filter((excludedId) => excludedId !== id)
    : [...settings.notificationTypeExclusions, id]
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
        <SettingsToggle
          :label="isLightTheme ? t('settings.theme.light') : t('settings.theme.dark')"
          :desc="t('settings.theme.lightDesc')"
          input-id="footer-toggle-theme"
        >
          <template #control>
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
          </template>
        </SettingsToggle>
      </div>
    </div>

    <!-- Dashboard -->
    <div class="detail-modal__section">
      <div class="detail-modal__section-title">{{ t('settings.dashboard.title') }}</div>
      <div class="settings-toggles">
        <SettingsToggle
          v-model="settings.showCardInfoIcons"
          :label="t('settings.dashboard.cardInfoIcons')"
          :desc="t('settings.dashboard.cardInfoIconsDesc')"
          input-id="footer-toggle-card-info-icons"
        />
      </div>
    </div>

    <!-- Language -->
    <div class="detail-modal__section">
      <div class="detail-modal__section-title">{{ t('settings.language.title') }}</div>
      <div class="settings-toggles">
        <SettingsToggle :label="isNL ? 'Nederlands' : 'English'" input-id="footer-toggle-lang">
          <template #control>
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
          </template>
        </SettingsToggle>
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
          <Multiselect
            v-model="settings.vehicleTypeOverride"
            :options="typeOptions"
            :searchable="false"
            :can-clear="false"
            :can-deselect="false"
            append-to="body"
            class="vehicle-type-select"
          />
        </div>
      </div>
    </div>

    <!-- Notification types (also controls push subscription: deselecting every type
         unsubscribes, selecting at least one (re)subscribes) -->
    <div class="detail-modal__section">
      <div class="detail-modal__section-title">{{ t('settings.notificationTypes.title') }}</div>
      <div class="notif-type-filter">
        <div class="notif-type-filter__header">
          <div class="settings-toggle__info">
            <span class="settings-toggle__label">{{ t('notifications.typeFilter') }}</span>
            <span class="settings-toggle__desc">{{ t('notifications.typeFilterDesc') }}</span>
          </div>
          <div class="notif-type-filter__actions">
            <button
              type="button"
              class="btn btn-sm btn-outline-secondary"
              @click="selectAllNotificationTypes"
            >
              {{ t('notifications.selectAll') }}
            </button>
            <button
              type="button"
              class="btn btn-sm btn-outline-secondary"
              @click="deselectAllNotificationTypes"
            >
              {{ t('notifications.deselectAll') }}
            </button>
          </div>
        </div>

        <div
          v-if="showPermissionDeniedNotice"
          class="notif-type-filter__denied text-danger text-sm"
        >
          {{ t('push.permissionDenied') }}
        </div>

        <div class="notif-type-checklist">
          <label
            v-for="opt in notificationTypeOptions"
            :key="opt.value"
            class="form-check form-switch notif-type-checklist__item"
          >
            <input
              class="form-check-input"
              type="checkbox"
              role="switch"
              :checked="!settings.notificationTypeExclusions.includes(opt.value)"
              @change="
                toggleNotificationType(opt.value, ($event.target as HTMLInputElement).checked)
              "
            />
            <span class="notif-type-checklist__label">{{ opt.label }}</span>
          </label>
        </div>
      </div>
    </div>
  </DetailModal>
</template>
