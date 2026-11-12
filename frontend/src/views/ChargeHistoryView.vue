<script setup lang="ts">
import '@/assets/chargeHistory.css'
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useVehicleStore } from '@/stores/vehicle'
import { vehicleApi, type ChargeSession } from '@/services/vehicleApi'
import { formatDateTime, formatNumber } from '@/utils/format'
import { formatDurationMs } from '@/utils/tripRows'
import { useUnits } from '@/composables/useUnits'
import { useErrorMessage } from '@/composables/useErrorMessage'
import ViewHeader from '@/components/ViewHeader.vue'
import StatusCard from '@/components/StatusCard.vue'
import ChargeSessionLocationMap from '@/components/ChargeSessionLocationMap.vue'

const { t } = useI18n()
const vehicleStore = useVehicleStore()
const units = useUnits()
const errorMessage = useErrorMessage()
const sessions = ref<ChargeSession[]>([])
const expandedSessions = ref(new Set<string>())
const loading = ref(false)
const loadError = ref<unknown>(null)

const vin = computed(() => vehicleStore.activeVin)
const sessionCount = computed(() => sessions.value.length)

async function load() {
  loading.value = true
  loadError.value = null
  try {
    await vehicleStore.fetchVehicles()
    if (vehicleStore.vehiclesError) throw vehicleStore.vehiclesError
    sessions.value = vin.value ? await vehicleApi.chargeSessions(vin.value) : []
  } catch (error) {
    loadError.value = error
  } finally {
    loading.value = false
  }
}

onMounted(load)

function duration(session: ChargeSession): string {
  if (!session.endedAt) return t('chargeHistory.ongoing')
  return formatDurationMs(
    new Date(session.endedAt).getTime() - new Date(session.startedAt).getTime(),
    t,
  )
}

function stateOfCharge(session: ChargeSession): string {
  const start =
    session.startSocPercent === null
      ? t('chargeHistory.notReported')
      : `${formatNumber(session.startSocPercent, 0)}%`
  const end =
    session.endSocPercent === null
      ? t('chargeHistory.notReported')
      : `${formatNumber(session.endSocPercent, 0)}%`
  return `${start} → ${end}`
}

function isExpanded(session: ChargeSession): boolean {
  return expandedSessions.value.has(session.startedAt)
}

function toggleGraph(session: ChargeSession): void {
  const next = new Set(expandedSessions.value)
  if (next.has(session.startedAt)) next.delete(session.startedAt)
  else next.add(session.startedAt)
  expandedSessions.value = next
}

function odometerLabel(km: number | null): string {
  if (km === null) return t('chargeHistory.notReported')
  const distance = units.value.measure('distance', km, { decimals: 0, grouped: true })
  return distance ? `${distance.value} ${distance.unit}` : t('chargeHistory.notReported')
}

function distanceSincePreviousCharge(index: number): number | null {
  const session = sessions.value[index]
  const previousSession = sessions.value[index + 1]
  if (
    !session ||
    !previousSession ||
    session.odometerKm === null ||
    previousSession.odometerKm === null
  )
    return null

  const distance = session.odometerKm - previousSession.odometerKm
  return Number.isFinite(distance) && distance >= 0 ? distance : null
}

function energyUsedSincePreviousCharge(index: number): number | null {
  const session = sessions.value[index]
  const previousSession = sessions.value[index + 1]
  if (
    !session ||
    !previousSession ||
    session.startBatteryKwh === null ||
    previousSession.endBatteryKwh === null
  )
    return null

  const energy = previousSession.endBatteryKwh - session.startBatteryKwh
  return Number.isFinite(energy) && energy >= 0 ? energy : null
}

function energyUsedLabel(index: number): string {
  const energy = energyUsedSincePreviousCharge(index)
  return energy === null
    ? t('chargeHistory.notReported')
    : `${formatNumber(energy, 1)} kWh`
}

function consumptionSincePreviousCharge(index: number): string {
  const distance = distanceSincePreviousCharge(index)
  const energy = energyUsedSincePreviousCharge(index)
  if (distance === null || distance <= 0 || energy === null)
    return t('chargeHistory.notReported')

  const consumption = units.value.measure('energyPerDistance', (energy * 1000) / distance)
  return consumption ? `${consumption.value} ${consumption.unit}` : t('chargeHistory.notReported')
}

function hasLocation(session: ChargeSession): boolean {
  return (
    session.latitude !== null &&
    session.longitude !== null &&
    Number.isFinite(session.latitude) &&
    Number.isFinite(session.longitude) &&
    Math.abs(session.latitude) <= 90 &&
    Math.abs(session.longitude) <= 180
  )
}

function graphTime(value: string): string {
  return new Intl.DateTimeFormat(undefined, { hour: 'numeric', minute: '2-digit' }).format(
    new Date(value),
  )
}

function powerGraph(session: ChargeSession): {
  line: string
  points: { x: number; y: number }[]
  maxPower: number
  startTime: string
  endTime: string
} {
  const points = session.powerPoints
  const start = Date.parse(points[0]?.recordedAt ?? session.startedAt)
  const end = Date.parse(points[points.length - 1]?.recordedAt ?? session.startedAt)
  const totalDuration = Math.max(end - start, 1)
  const maxPower = Math.max(...points.map((point) => point.powerKw), 0.1)

  const coordinates = points.map((point) => {
    const position =
      points.length === 1 ? 1 : (Date.parse(point.recordedAt) - start) / totalDuration
    const x = 58 + Math.min(Math.max(position, 0), 1) * 314
    const y = 120 - (point.powerKw / maxPower) * 96
    return { x, y }
  })
  return {
    line: coordinates.map(({ x, y }) => `${x.toFixed(1)},${y.toFixed(1)}`).join(' '),
    points: coordinates,
    maxPower,
    startTime: graphTime(points[0]?.recordedAt ?? session.startedAt),
    endTime: graphTime(points[points.length - 1]?.recordedAt ?? session.startedAt),
  }
}
</script>

<template>
  <div class="view-container">
    <ViewHeader :title="t('chargeHistory.title')" :subtitle="t('chargeHistory.subtitle')" />

    <div v-if="sessionCount > 0" class="charge-history-summary">
      <StatusCard
        icon="plug-circle-bolt"
        :label="t('chargeHistory.sessions')"
        :value="sessionCount"
        variant="info"
      />
    </div>

    <div v-if="loadError" class="empty-state text-danger" role="alert">
      <p>{{ errorMessage(loadError) }}</p>
      <button class="btn btn-outline-secondary btn-sm" :disabled="loading" @click="load">
        {{ t('common.retry') }}
      </button>
    </div>
    <div v-else-if="loading && sessions.length === 0" class="empty-state" aria-live="polite">
      {{ t('common.loading') }}
    </div>
    <div v-else-if="sessions.length === 0" class="empty-state">
      {{ t('chargeHistory.empty') }}
    </div>
    <ol v-else class="charge-history-list">
      <li
        v-for="(session, index) in sessions"
        :key="`${session.startedAt}-${index}`"
        class="charge-history-item"
      >
        <div class="charge-history-item__heading">
          <h2>{{ formatDateTime(session.startedAt) }}</h2>
          <div class="charge-history-item__badges">
            <span
              class="charge-history-item__status"
              :class="{ 'charge-history-item__status--active': !session.endedAt }"
            >
              {{ session.endedAt ? t('chargeHistory.completed') : t('chargeHistory.ongoing') }}
            </span>
            <span
              v-if="session.chargingType"
              class="charge-history-item__type"
              :class="{
                'charge-history-item__type--dc': session.chargingType.toUpperCase() === 'DC',
              }"
            >
              <font-awesome-icon
                v-if="session.chargingType.toUpperCase() === 'DC'"
                icon="bolt"
                aria-hidden="true"
              />
              {{ session.chargingType }}
            </span>
          </div>
        </div>
        <dl class="charge-history-item__details">
          <div>
            <dt>{{ t('chargeHistory.duration') }}</dt>
            <dd>{{ duration(session) }}</dd>
          </div>
          <div>
            <dt>{{ t('chargeHistory.stateOfCharge') }}</dt>
            <dd>{{ stateOfCharge(session) }}</dd>
          </div>
          <div>
            <dt>{{ t('chargeHistory.odometer') }}</dt>
            <dd>{{ odometerLabel(session.odometerKm) }}</dd>
          </div>
          <div>
            <dt>{{ t('chargeHistory.batteryEnergyGained') }}</dt>
            <dd>
              {{
                session.batteryEnergyGainedKwh === null
                  ? t('chargeHistory.notReported')
                  : `${formatNumber(session.batteryEnergyGainedKwh, 1)} kWh`
              }}
            </dd>
          </div>
          <div>
            <dt>{{ t('chargeHistory.maxPower') }}</dt>
            <dd>
              {{
                session.maxPowerKw === null
                  ? t('chargeHistory.notReported')
                  : `${formatNumber(session.maxPowerKw, 1)} kW`
              }}
            </dd>
          </div>
          <div v-if="session.endedAt">
            <dt>{{ t('chargeHistory.ended') }}</dt>
            <dd>{{ formatDateTime(session.endedAt) }}</dd>
          </div>
        </dl>
        <button
          class="btn btn-outline-secondary btn-sm charge-history-item__graph-toggle"
          :aria-expanded="isExpanded(session)"
          :aria-controls="`charge-power-${index}`"
          @click="toggleGraph(session)"
        >
          <font-awesome-icon icon="chart-line" aria-hidden="true" />
          {{
            isExpanded(session)
              ? t('chargeHistory.hidePowerGraph')
              : t('chargeHistory.showPowerGraph')
          }}
        </button>
        <div
          v-if="isExpanded(session)"
          :id="`charge-power-${index}`"
          class="charge-history-graph"
        >
          <div v-if="session.powerPoints.length > 0" class="charge-history-graph__heading">
            <span>{{ t('chargeHistory.power') }}</span>
            <span>{{ formatNumber(session.maxPowerKw ?? 0, 1) }} kW</span>
          </div>
          <div class="charge-history-graph__body">
            <dl class="charge-history-graph__metrics">
              <div>
                <dt>{{ t('chargeHistory.distanceSincePreviousCharge') }}</dt>
                <dd>{{ odometerLabel(distanceSincePreviousCharge(index)) }}</dd>
              </div>
              <div>
                <dt>{{ t('chargeHistory.energyUsedSincePreviousCharge') }}</dt>
                <dd>{{ energyUsedLabel(index) }}</dd>
              </div>
              <div>
                <dt>{{ t('chargeHistory.consumptionSincePreviousCharge') }}</dt>
                <dd>{{ consumptionSincePreviousCharge(index) }}</dd>
              </div>
            </dl>
            <template v-if="session.powerPoints.length > 0">
              <svg
                class="charge-history-graph__chart"
                viewBox="0 0 400 165"
                role="img"
                :aria-label="
                  t('chargeHistory.powerGraphLabel', {
                    max: `${formatNumber(session.maxPowerKw ?? 0, 1)} kW`,
                  })
                "
              >
                <line x1="58" y1="24" x2="58" y2="120" class="charge-history-graph__axis" />
                <line x1="58" y1="120" x2="372" y2="120" class="charge-history-graph__axis" />
                <line
                  v-for="tick in [0, 0.5, 1]"
                  :key="tick"
                  x1="58"
                  :y1="120 - tick * 96"
                  x2="372"
                  :y2="120 - tick * 96"
                  class="charge-history-graph__grid"
                />
                <text
                  v-for="tick in [0, 0.5, 1]"
                  :key="`power-${tick}`"
                  x="50"
                  :y="124 - tick * 96"
                  text-anchor="end"
                  class="charge-history-graph__label"
                >
                  {{ formatNumber(powerGraph(session).maxPower * tick, 1) }}
                </text>
                <text
                  x="12"
                  y="74"
                  transform="rotate(-90 12 74)"
                  class="charge-history-graph__axis-title"
                >
                  {{ t('chargeHistory.powerAxis') }}
                </text>
                <polyline :points="powerGraph(session).line" class="charge-history-graph__line" />
                <circle
                  v-for="(point, pointIndex) in powerGraph(session).points"
                  :key="`${session.powerPoints[pointIndex]?.recordedAt}-${pointIndex}`"
                  :cx="point.x"
                  :cy="point.y"
                  r="3"
                  class="charge-history-graph__point"
                />
                <text x="58" y="141" class="charge-history-graph__label">
                  {{ powerGraph(session).startTime }}
                </text>
                <text x="372" y="141" text-anchor="end" class="charge-history-graph__label">
                  {{ powerGraph(session).endTime }}
                </text>
                <text
                  x="215"
                  y="161"
                  text-anchor="middle"
                  class="charge-history-graph__axis-title"
                >
                  {{ t('chargeHistory.timeAxis') }}
                </text>
              </svg>
              <ChargeSessionLocationMap
                v-if="hasLocation(session)"
                :latitude="session.latitude ?? 0"
                :longitude="session.longitude ?? 0"
                :label="t('chargeHistory.locationMap')"
              />
              <div v-else class="charge-history-map charge-history-map--empty">
                {{ t('chargeHistory.locationUnavailable') }}
              </div>
            </template>
            <p v-else class="text-muted text-sm">
              {{ t('chargeHistory.noPowerData') }}
            </p>
          </div>
        </div>
      </li>
    </ol>
  </div>
</template>
