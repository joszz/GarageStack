import { defineStore } from 'pinia'
import { onScopeDispose } from 'vue'
import { settingsApi, type AccountSettings, type SettingsSection } from '@/services/settingsApi'

/**
 * Keeps the settings stores on the signed-in account, so settings follow it to every device. Each
 * store keeps its own copy in this browser as well: that copy is what the page starts with, and all
 * there is while signed out.
 *
 * Settings travel key by key. The page remembers what the account held for each key when it last
 * heard, so it can tell a change made here from one made on another device: changes made here go
 * to the account, and the account's copy fills in everything this page left alone.
 */

type Fields = Record<string, unknown>

/** What a settings store hands over to have its settings kept on the account. */
export interface SyncedSection {
  section: SettingsSection
  /** The store's reactive settings. The account's copy is written into it key by key. */
  state: object
  /** Reads the account's copy the way the store reads its own: unknown values fall back. */
  parse: (raw: Fields) => object
  /** Whether this browser has stored the section itself, so it may fill an account that has none. */
  hasOwnCopy: () => boolean
  /** Runs when the account's copy of the section has been taken in. */
  onAdopted?: () => void
  /** Keys that belong to this device alone: never sent to the account, never taken from it. */
  localKeys?: readonly string[]
}

/** Each key's value as JSON, as the account was last known to hold it. */
type Baseline = Map<string, string>

interface Entry {
  binding: SyncedSection
  /** Null while nothing is being kept on the account. */
  baseline: Baseline | null
  /** Saves run one after another, so an older one can never land after a newer one. */
  saving: Promise<void>
}

/**
 * How long a page may go without hearing of changes made on other devices before coming back into
 * view asks again: long enough that flicking between tabs does not ask every time.
 */
export const PULL_INTERVAL_MS = 30_000

function snapshot(fields: object): Baseline {
  return new Map(Object.entries(fields).map(([key, value]) => [key, JSON.stringify(value)]))
}

function changedKeys(fields: object, baseline: Baseline): string[] {
  return Object.entries(fields)
    .filter(([key, value]) => baseline.get(key) !== JSON.stringify(value))
    .map(([key]) => key)
}

function isFields(value: unknown): value is Fields {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/** The keys of `fields` that travel between this device and the account. */
function syncedFields(binding: SyncedSection, fields: object): Fields {
  const local = binding.localKeys ?? []
  return Object.fromEntries(Object.entries(fields).filter(([key]) => !local.includes(key)))
}

export const useSettingsSyncStore = defineStore('settingsSync', () => {
  const entries = new Map<SettingsSection, Entry>()

  let started = false
  // Bumped whenever keeping starts or stops, so an answer meant for an earlier sign-in is dropped.
  let session = 0
  /** The account's settings as last read in this sign-in; null until the first read succeeds. */
  let remote: AccountSettings | null = null
  let ready: Promise<void> = Promise.resolve()
  let pulling: Promise<void> | null = null
  let lastPullAt = 0

  async function save(entry: Entry) {
    // Until the account's copy has been read, a value here cannot be told apart from a default
    // the account already overrode, so nothing goes out before it has.
    if (!started || remote === null || entry.baseline === null) return
    const state = entry.binding.state as Fields
    const keys = changedKeys(syncedFields(entry.binding, state), entry.baseline)
    if (keys.length === 0) return

    const changes = Object.fromEntries(keys.map((key) => [key, state[key]]))
    const sent = snapshot(changes)
    const current = session
    try {
      await settingsApi.save(entry.binding.section, changes)
      if (current !== session) return
      for (const [key, json] of sent) entry.baseline?.set(key, json)
    } catch {
      // Offline, or refused: the keys still count as changed, and go with the next save or read.
    }
  }

  function queueSave(entry: Entry) {
    entry.saving = entry.saving.then(() => save(entry))
  }

  // The account's copy of a section as the store reads it, or null when there is none or this
  // build cannot make sense of it.
  function parseRemote(binding: SyncedSection): Fields | null {
    const theirs = remote?.[binding.section]
    if (!isFields(theirs)) return null
    try {
      return syncedFields(binding, binding.parse(theirs))
    } catch {
      return null
    }
  }

  function adopt(entry: Entry) {
    const { binding } = entry
    const state = binding.state as Fields
    const parsed = parseRemote(binding)
    // Changed on this page since keeping began: the latest word, which the account's copy must
    // not undo. They go to the account below.
    const mine = new Set(
      entry.baseline ? changedKeys(syncedFields(binding, state), entry.baseline) : [],
    )

    if (parsed === null) {
      // The account holds none yet, or one that cannot be read: this browser's own copy fills
      // it, when it has one. An empty baseline sends every key.
      const replace = remote?.[binding.section] !== undefined || binding.hasOwnCopy()
      entry.baseline = replace
        ? new Map()
        : (entry.baseline ?? snapshot(syncedFields(binding, state)))
    } else {
      for (const [key, value] of Object.entries(parsed)) {
        // Only what differs is written, so a list that did not change is not handed to every
        // component showing it as a new one.
        if (!mine.has(key) && JSON.stringify(state[key]) !== JSON.stringify(value)) {
          state[key] = value
        }
      }
      entry.baseline = snapshot(parsed)
      binding.onAdopted?.()
    }
    queueSave(entry)
  }

  async function load(current: number) {
    try {
      const settings = await settingsApi.load()
      if (current !== session) return
      remote = settings ?? {}
      lastPullAt = Date.now()
      for (const entry of entries.values()) adopt(entry)
    } catch {
      // Offline, or the session is gone: the page carries on with this browser's own copy.
    }
  }

  /**
   * Reads the account's settings and takes them in. Resolves once done, whether or not the server
   * could be reached; a read already under way is shared rather than asked for twice.
   */
  function pull(): Promise<void> {
    if (!started) return Promise.resolve()
    if (pulling) return pulling
    const attempt: Promise<void> = load(session).finally(() => {
      if (pulling === attempt) pulling = null
    })
    pulling = attempt
    return attempt
  }

  /**
   * Starts keeping settings on the signed-in account, beginning with its copy. Resolves once that
   * copy has been taken in, or could not be read. While already keeping, it changes nothing.
   */
  function start(): Promise<void> {
    if (started) return ready
    started = true
    session++
    for (const entry of entries.values())
      entry.baseline = snapshot(syncedFields(entry.binding, entry.binding.state))
    ready = pull()
    return ready
  }

  /** Stops keeping settings on the account, on sign-out. This browser keeps its own copy. */
  function stop() {
    if (!started) return
    started = false
    session++
    remote = null
    pulling = null
    for (const entry of entries.values()) entry.baseline = null
  }

  /** Called by each settings store as it is created. */
  function register(binding: SyncedSection) {
    const entry: Entry = {
      binding,
      baseline: started ? snapshot(syncedFields(binding, binding.state)) : null,
      saving: Promise.resolve(),
    }
    entries.set(binding.section, entry)
    // A store first used after the account's copy came in takes it at once, before anything
    // has read the store.
    if (started && remote !== null) adopt(entry)
  }

  /** Sends what changed in a section since the account last heard. Called after each local write. */
  function push(section: SettingsSection) {
    const entry = entries.get(section)
    if (!entry || !started) return
    // Not read yet (the server was out of reach at start-up, say): reading it sends the change too.
    if (remote === null) void pull()
    else queueSave(entry)
  }

  // Changes made on another device show up when this one is looked at again, or back online.
  function onVisibilityChange() {
    if (document.visibilityState === 'visible' && Date.now() - lastPullAt >= PULL_INTERVAL_MS) {
      void pull()
    }
  }
  function onOnline() {
    void pull()
  }
  document.addEventListener('visibilitychange', onVisibilityChange)
  window.addEventListener('online', onOnline)
  onScopeDispose(() => {
    document.removeEventListener('visibilitychange', onVisibilityChange)
    window.removeEventListener('online', onOnline)
  })

  return { register, start, stop, pull, push }
})
