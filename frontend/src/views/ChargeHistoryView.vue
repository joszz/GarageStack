<script setup lang="ts">
import '@/assets/chargeHistory.css'
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useVehicleStore } from '@/stores/vehicle'
import { vehicleApi, type ChargeSession } from '@/services/vehicleApi'
import { mayPlugIn } from '@/utils/vehicleType'
import type { ChargeEnergyContext } from '@/utils/chargeSessions'
import { useErrorMessage } from '@/composables/useErrorMessage'
import ViewHeader from '@/components/ViewHeader.vue'
import StatusCard from '@/components/StatusCard.vue'
import ChargeSessionItem from '@/components/ChargeSessionItem.vue'

/** The periods offered, in days. The API reads at most a year at a time. */
const PERIOD_DAYS = [30, 90, 365] as const
type PeriodDays = (typeof PERIOD_DAYS)[number]

const { t } = useI18n()
const router = useRouter()
const vehicleStore = useVehicleStore()
const errorMessage = useErrorMessage()
const sessions = ref<ChargeSession[]>([])
const loading = ref(false)
const loadError = ref<unknown>(null)
const periodDays = ref<PeriodDays>(90)

const vin = computed(() => vehicleStore.activeVin)
const energyContext = computed((): ChargeEnergyContext => ({
  vehicleType: vehicleStore.effectiveVehicleType,
  configuredCapacityKwh: vehicleStore.hvBatteryCapacityKwh,
}))

// A plain hybrid never charges from a plug. The menu leaves the page out for one; someone who
// opens its address anyway lands on the dashboard.
const charges = computed(() => mayPlugIn(vehicleStore.effectiveVehicleType))

async function load(): Promise<void> {
  loading.value = true
  loadError.value = null
  try {
    await vehicleStore.fetchVehicles()
    if (vehicleStore.vehiclesError) throw vehicleStore.vehiclesError
    if (!charges.value) {
      await router.replace({ name: 'dashboard' })
      return
    }
    const to = new Date()
    const from = new Date(to.getTime() - periodDays.value * 24 * 60 * 60 * 1000)
    sessions.value = vin.value
      ? await vehicleApi.chargeSessions(vin.value, from.toISOString(), to.toISOString())
      : []
  } catch (error) {
    loadError.value = error
  } finally {
    loading.value = false
  }
}

onMounted(load)
watch(periodDays, load)
// The first load sets the active car itself; only switching to another one loads again.
watch(vin, (now, before) => {
  if (before) void load()
})
watch(charges, (now) => {
  if (!now) void router.replace({ name: 'dashboard' })
})
</script>

<template>
  <div class="view-container">
    <ViewHeader :title="t('chargeHistory.title')" :subtitle="t('chargeHistory.subtitle')" />

    <div class="charge-history-toolbar">
      <label class="charge-history-toolbar__field">
        <span class="text-xs text-muted">{{ t('chargeHistory.period') }}</span>
        <select v-model.number="periodDays" class="form-select">
          <option v-for="days in PERIOD_DAYS" :key="days" :value="days">
            {{ t('chargeHistory.lastDays', { days }) }}
          </option>
        </select>
      </label>
      <div v-if="sessions.length > 0" class="charge-history-toolbar__summary">
        <StatusCard
          icon="plug-circle-bolt"
          :label="t('chargeHistory.sessions')"
          :value="sessions.length"
          variant="info"
        />
      </div>
    </div>

    <div v-if="loadError" class="empty-state text-danger" role="alert">
      <p>{{ errorMessage(loadError) }}</p>
      <button
        type="button"
        class="btn btn-outline-secondary btn-sm"
        :disabled="loading"
        @click="load"
      >
        {{ t('common.retry') }}
      </button>
    </div>
    <div v-else-if="loading && sessions.length === 0" class="empty-state" aria-live="polite">
      {{ t('common.loading') }}
    </div>
    <div v-else-if="sessions.length === 0" class="empty-state">
      {{ t('chargeHistory.empty') }}
    </div>
    <ol v-else-if="vin" class="charge-history-list">
      <ChargeSessionItem
        v-for="(session, index) in sessions"
        :key="session.startedAt"
        :vin="vin"
        :session="session"
        :previous="sessions[index + 1] ?? null"
        :energy-context="energyContext"
      />
    </ol>
  </div>
</template>
