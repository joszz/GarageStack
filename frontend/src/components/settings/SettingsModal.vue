<script setup lang="ts">
import { computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { useRdwStore } from '@/stores/rdw'
import { useVehicleStore } from '@/stores/vehicle'
import DetailModal from '../DetailModal.vue'
import SettingsToggle from '../SettingsToggle.vue'
import RegionalSettings from '../RegionalSettings.vue'
import SettingsSelect from '../SettingsSelect.vue'
import SettingsSection from './SettingsSection.vue'
import SideIconSwitch from './SideIconSwitch.vue'
import CarColorPicker from './CarColorPicker.vue'
import VehicleTypeSetting from './VehicleTypeSetting.vue'
import LicensePlateSetting from './LicensePlateSetting.vue'
import NotificationTypeSettings from './NotificationTypeSettings.vue'
import ApiTokenSettings from './ApiTokenSettings.vue'
import { apiUrl } from '@/services/apiCore'

const props = defineProps<{ open: boolean }>()
defineEmits<{ close: [] }>()

const { t } = useI18n()
const settings = useUiSettingsStore()
const rdw = useRdwStore()
const vehicleStore = useVehicleStore()

// The plate field only exists where the deployment looks plates up, which only the API knows.
watch(
  () => [props.open, vehicleStore.activeVin] as const,
  ([open, vin]) => {
    if (open && vin) rdw.ensureLoaded(vin)
  },
  { immediate: true },
)

const isLightTheme = computed({
  get: () => settings.theme === 'light',
  set: (val: boolean) => {
    settings.theme = val ? 'light' : 'dark'
  },
})

const languageOptions = [
  { value: 'en', label: 'English' },
  { value: 'nl', label: 'Nederlands' },
  { value: 'es', label: 'Español' },
] as const
</script>

<template>
  <DetailModal :open="open" :title="t('settings.title')" @close="$emit('close')">
    <SettingsSection :title="t('settings.theme.title')">
      <div class="settings-toggles">
        <SideIconSwitch
          v-model="isLightTheme"
          :label="isLightTheme ? t('settings.theme.light') : t('settings.theme.dark')"
          :desc="t('settings.theme.lightDesc')"
          input-id="footer-toggle-theme"
        >
          <template #off><font-awesome-icon icon="moon" /></template>
          <template #on><font-awesome-icon icon="sun" /></template>
        </SideIconSwitch>
      </div>
    </SettingsSection>

    <SettingsSection :title="t('settings.dashboard.title')">
      <div class="settings-toggles">
        <SettingsToggle
          v-model="settings.showCardInfoIcons"
          :label="t('settings.dashboard.cardInfoIcons')"
          :desc="t('settings.dashboard.cardInfoIconsDesc')"
          input-id="footer-toggle-card-info-icons"
        />
      </div>
    </SettingsSection>

    <SettingsSection :title="t('settings.map.title')">
      <div class="settings-toggles">
        <SettingsToggle
          v-model="settings.placeNamesEnabled"
          :label="t('settings.map.placeNames')"
          :desc="t('settings.map.placeNamesDesc')"
          input-id="footer-toggle-place-names"
        />
        <SettingsToggle
          v-model="settings.colorfulMaps"
          :label="t('settings.map.colorful')"
          :desc="t('settings.map.colorfulDesc')"
          input-id="footer-toggle-colorful-maps"
        />
      </div>
    </SettingsSection>

    <SettingsSection :title="t('settings.language.title')">
      <div class="settings-toggles">
        <SettingsSelect
          id="footer-toggle-lang"
          v-model="settings.locale"
          :label="t('settings.language.title')"
          :options="languageOptions"
        />
      </div>
    </SettingsSection>

    <SettingsSection :title="t('settings.units.title')">
      <RegionalSettings />
    </SettingsSection>

    <SettingsSection :title="t('settings.carColor.title')">
      <CarColorPicker />
    </SettingsSection>

    <SettingsSection :title="t('settings.vehicleType.title')">
      <VehicleTypeSetting />
    </SettingsSection>

    <SettingsSection v-if="rdw.available" :title="t('settings.licensePlate.title')">
      <LicensePlateSetting />
    </SettingsSection>

    <SettingsSection :title="t('settings.notificationTypes.title')">
      <NotificationTypeSettings />
    </SettingsSection>

    <SettingsSection :title="t('settings.apiTokens.title')">
      <ApiTokenSettings />
    </SettingsSection>

    <SettingsSection :title="t('settings.export.title')">
      <div class="form-stack">
        <p class="settings-toggle__desc">{{ t('settings.export.desc') }}</p>
        <a class="btn btn-outline-secondary btn-sm" :href="apiUrl('/api/export')" download>
          <font-awesome-icon icon="download" />
          {{ t('settings.export.download') }}
        </a>
      </div>
    </SettingsSection>
  </DetailModal>
</template>
