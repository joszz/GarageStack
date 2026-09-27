import { delay } from '@/utils/async'

/** What one kind of lookup (coordinates to name, trips to give addresses) tells the loop. */
export interface Rounds<T> {
  /** Whether the run should go on; asked before every round. */
  active(): boolean
  /** The next request's worth of what is still wanted; an empty batch ends the run. */
  nextBatch(): T[]
  /**
   * Asks the server about a batch and keeps what it found. Resolves to the items it left
   * unanswered, or to null when the deployment has the service switched off, which ends the run.
   */
  ask(batch: T[]): Promise<T[] | null>
  /** Counts a round against an item, which stops being offered once it has had enough of them. */
  countAttempt(item: T): void
  /** Whether anything is left to ask about, so the loop knows to pause before the next round. */
  hasMore(): boolean
  /**
   * Runs as the loop ends, however it ends, before the returned promise settles. A caller that
   * turns new requests away while a run is going clears that flag here, so there is no moment in
   * which the run has already decided to stop but still turns a request away.
   */
  onEnd(): void
}

/**
 * Works through lookups the server can only answer part of at a time (the geocoder behind it
 * answers about one per second): a batch per round, with a pause between rounds. Only a round
 * that resolved nothing counts against its items. A server that answers some of every batch is
 * working through its upstream budget, and counting that would abandon a long list after a few
 * rounds of normal progress. A request that fails outright rejects, so the caller decides what
 * that costs.
 */
export async function resolveInRounds<T>(rounds: Rounds<T>, pauseMs: number): Promise<void> {
  try {
    while (rounds.active()) {
      const batch = rounds.nextBatch()
      if (batch.length === 0) return

      const unanswered = await rounds.ask(batch)
      if (unanswered === null) return
      if (unanswered.length === batch.length) {
        for (const item of unanswered) rounds.countAttempt(item)
      }

      if (rounds.hasMore()) await delay(pauseMs)
    }
  } finally {
    rounds.onEnd()
  }
}
