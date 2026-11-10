import { computed, onMounted, ref } from 'vue'
import { dawarichApi, type DawarichStatus, type PhoneTrackers } from '@/services/dawarichApi'
import { useAsyncRequest } from './useAsyncRequest'

/** One settings panel per vehicle, keyed by VIN so late answers cannot replace another car's data. */
export function useDawarich(vin: string) {
  const status = ref<DawarichStatus | null>(null)
  const trackers = ref<PhoneTrackers | null>(null)
  const selected = ref<string[]>([])
  const saved = ref(false)
  const queued = ref(false)
  const { loading, error, run } = useAsyncRequest()
  const dirty = computed(() => {
    const linked = trackers.value?.linked.map((t) => t.trackerId) ?? []
    return (
      linked.length !== selected.value.length || linked.some((id) => !selected.value.includes(id))
    )
  })

  async function load() {
    if (loading.value) return
    await run(
      async () => {
        const state = await dawarichApi.status()
        return { state, list: state.configured ? await dawarichApi.trackers(vin) : null }
      },
      ({ state, list }) => {
        status.value = state
        trackers.value = list
        selected.value = list?.linked.map((t) => t.trackerId) ?? []
      },
    )
  }

  async function save() {
    if (loading.value) return
    saved.value = false
    queued.value = false
    await run(
      () => dawarichApi.save(vin, [...selected.value]),
      (linked) => {
        if (trackers.value) trackers.value.linked = linked
        selected.value = linked.map((t) => t.trackerId)
        saved.value = true
      },
    )
  }

  async function backfill(from: string) {
    if (loading.value || dirty.value) return
    queued.value = false
    await run(
      () => dawarichApi.backfill(vin, `${from}T00:00:00Z`),
      () => {
        queued.value = true
      },
    )
  }

  onMounted(load)
  return { status, trackers, selected, saved, queued, loading, error, dirty, load, save, backfill }
}
