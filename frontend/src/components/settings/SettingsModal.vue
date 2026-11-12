<script setup lang="ts">
import { computed, nextTick, ref, useId, useTemplateRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { useRdwStore } from '@/stores/rdw'
import { useVehicleStore } from '@/stores/vehicle'
import DetailModal from '../DetailModal.vue'
import SettingsToggle from '../SettingsToggle.vue'
import RegionalSettings from '../RegionalSettings.vue'
import SettingsSelect from '../SettingsSelect.vue'
import SettingsSection from './SettingsSection.vue'
import ChipGroup from '../ChipGroup.vue'
import CarColorPicker from './CarColorPicker.vue'
import VehicleTypeSetting from './VehicleTypeSetting.vue'
import LicensePlateSetting from './LicensePlateSetting.vue'
import NotificationTypeSettings from './NotificationTypeSettings.vue'
import ApiTokenSettings from './ApiTokenSettings.vue'
import PhoneGpsSettings from './PhoneGpsSettings.vue'
import { apiUrl } from '@/services/apiCore'
import type { ChipOption } from '@/utils/chips'
import type { ThemeChoice } from '@/stores/settingsShared'

const props = defineProps<{ open: boolean }>()
defineEmits<{ close: [] }>()

const { t } = useI18n()
const settings = useUiSettingsStore()
const rdw = useRdwStore()
const vehicleStore = useVehicleStore()
const id = useId()
const tablist = useTemplateRef<HTMLElement>('tablist')
const panels = useTemplateRef<HTMLElement>('panels')
const phoneGpsVisible = ref(false)
const tabDefinitions = [
  { id: 'general', icon: 'sliders' },
  { id: 'vehicle', icon: 'car' },
  { id: 'map', icon: 'map' },
  { id: 'phone', icon: 'mobile-screen' },
  { id: 'notifications', icon: 'bell' },
  { id: 'data', icon: 'database' },
] as const
type SettingsTab = (typeof tabDefinitions)[number]['id']
const activeTab = ref<SettingsTab>('general')
const tabs = computed(() =>
  tabDefinitions.filter(
    (tab) => tab.id !== 'phone' || (!!vehicleStore.activeVin && phoneGpsVisible.value),
  ),
)

function selectTab(tab: SettingsTab, focus = false) {
  activeTab.value = tab
  const body = panels.value?.parentElement
  if (body) body.scrollTop = 0
  const button = tablist.value?.querySelector<HTMLButtonElement>(`[data-tab="${tab}"]`)
  if (focus) button?.focus({ preventScroll: true })
  button?.scrollIntoView({ block: 'nearest', inline: 'nearest' })
}

function onTabKeydown(event: KeyboardEvent) {
  const index = tabs.value.findIndex((tab) => tab.id === activeTab.value)
  let next: number
  switch (event.key) {
    case 'ArrowRight':
      next = (index + 1) % tabs.value.length
      break
    case 'ArrowLeft':
      next = (index - 1 + tabs.value.length) % tabs.value.length
      break
    case 'Home':
      next = 0
      break
    case 'End':
      next = tabs.value.length - 1
      break
    default:
      return
  }
  event.preventDefault()
  selectTab(tabs.value[next]!.id, true)
}

watch(tabs, async (visibleTabs) => {
  if (visibleTabs.some((tab) => tab.id === activeTab.value)) return
  const focused = document.activeElement
  const restoreFocus =
    focused === document.body || tablist.value?.contains(focused) || panels.value?.contains(focused)
  activeTab.value = 'general'
  await nextTick()
  if (props.open) selectTab('general', restoreFocus)
})

// The plate field only exists where the deployment looks plates up, which only the API knows.
watch(
  () => [props.open, vehicleStore.activeVin] as const,
  ([open, vin]) => {
    phoneGpsVisible.value = false
    if (!open) activeTab.value = 'general'
    if (open && vin) rdw.ensureLoaded(vin)
  },
  { immediate: true },
)

// Says what each choice does, rather than a switch whose label names the current theme.
const themeOptions = computed((): ChipOption<ThemeChoice>[] => [
  { value: 'light', label: t('settings.theme.light'), icon: 'sun' },
  { value: 'dark', label: t('settings.theme.dark'), icon: 'moon' },
  { value: 'system', label: t('settings.theme.system'), icon: 'circle-half-stroke' },
])

const languageOptions = [
  { value: 'en', label: 'English' },
  { value: 'nl', label: 'Nederlands' },
  { value: 'es', label: 'Español' },
] as const
</script>

<template>
  <DetailModal :open="open" :title="t('settings.title')" wide @close="$emit('close')">
    <template #navigation>
      <div class="settings-tabs-viewport">
        <div
          ref="tablist"
          class="settings-tabs"
          role="tablist"
          :aria-label="t('settings.tabs.label')"
          @keydown="onTabKeydown"
        >
          <button
            v-for="tab in tabs"
            :id="`${id}-tab-${tab.id}`"
            :key="tab.id"
            type="button"
            role="tab"
            class="settings-tabs__tab"
            :data-tab="tab.id"
            :aria-selected="activeTab === tab.id"
            :aria-controls="`${id}-panel-${tab.id}`"
            :aria-label="tab.id === 'phone' ? t('settings.tabs.phone') : undefined"
            :title="tab.id === 'phone' ? t('settings.tabs.phone') : undefined"
            :tabindex="activeTab === tab.id ? 0 : -1"
            @click="selectTab(tab.id)"
          >
            <font-awesome-icon :icon="tab.icon" aria-hidden="true" />
            {{ t(`settings.tabs.${tab.id === 'phone' ? 'phoneShort' : tab.id}`) }}
          </button>
        </div>
      </div>
    </template>
    <div ref="panels" class="settings-panels">
      <div
        :id="`${id}-panel-general`"
        role="tabpanel"
        :aria-labelledby="`${id}-tab-general`"
        :hidden="activeTab !== 'general'"
        tabindex="0"
      >
        <SettingsSection :title="t('settings.theme.title')">
          <div class="settings-toggles">
            <ChipGroup
              v-model="settings.theme"
              :options="themeOptions"
              :group-label="t('settings.theme.title')"
            />
            <p class="text-xs text-muted">{{ t('settings.theme.desc') }}</p>
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
      </div>

      <div
        :id="`${id}-panel-vehicle`"
        role="tabpanel"
        :aria-labelledby="`${id}-tab-vehicle`"
        :hidden="activeTab !== 'vehicle'"
        tabindex="0"
      >
        <SettingsSection :title="t('settings.carColor.title')">
          <CarColorPicker />
        </SettingsSection>

        <SettingsSection :title="t('settings.vehicleType.title')">
          <VehicleTypeSetting />
        </SettingsSection>

        <SettingsSection v-if="rdw.available" :title="t('settings.licensePlate.title')">
          <LicensePlateSetting />
        </SettingsSection>
      </div>

      <div
        :id="`${id}-panel-map`"
        role="tabpanel"
        :aria-labelledby="`${id}-tab-map`"
        :hidden="activeTab !== 'map'"
        tabindex="0"
      >
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
      </div>
      <!-- Keep panels mounted between tabs so edits and one-time API tokens are not lost.
           Phone GPS must also load while hidden to discover deployment availability. -->
      <div
        :id="`${id}-panel-phone`"
        role="tabpanel"
        :aria-labelledby="`${id}-tab-phone`"
        :hidden="activeTab !== 'phone' || !phoneGpsVisible"
        tabindex="0"
      >
        <PhoneGpsSettings
          v-if="open && vehicleStore.activeVin"
          :key="vehicleStore.activeVin"
          :vin="vehicleStore.activeVin"
          @visibility-change="phoneGpsVisible = $event"
        />
      </div>

      <div
        :id="`${id}-panel-notifications`"
        role="tabpanel"
        :aria-labelledby="`${id}-tab-notifications`"
        :hidden="activeTab !== 'notifications'"
        tabindex="0"
      >
        <SettingsSection :title="t('settings.notificationTypes.title')">
          <NotificationTypeSettings />
        </SettingsSection>
      </div>

      <div
        :id="`${id}-panel-data`"
        role="tabpanel"
        :aria-labelledby="`${id}-tab-data`"
        :hidden="activeTab !== 'data'"
        tabindex="0"
      >
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
      </div>
    </div>
  </DetailModal>
</template>
