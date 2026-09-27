import { describe, it, expect, vi, afterEach } from 'vitest'
import { resolveInRounds, type Rounds } from '@/utils/resolveInRounds'

const PAUSE_MS = 900
const MAX_ATTEMPTS = 2

/**
 * A lookup over named items: `answers` says which items each round resolves, in order. Items
 * are dropped once resolved or once they have used up their attempts.
 */
function lookup(items: string[], answers: string[][], batchSize = 10) {
  const wanted = new Set(items)
  const attempts = new Map<string, number>()
  const asked: string[][] = []
  let round = 0
  let ended = 0
  const rounds: Rounds<string> = {
    active: () => true,
    nextBatch: () => [...wanted].slice(0, batchSize),
    ask: async (batch) => {
      asked.push(batch)
      const resolved = new Set(answers[round++] ?? [])
      for (const item of resolved) wanted.delete(item)
      return batch.filter((item) => !resolved.has(item))
    },
    countAttempt: (item) => {
      const count = (attempts.get(item) ?? 0) + 1
      attempts.set(item, count)
      if (count >= MAX_ATTEMPTS) wanted.delete(item)
    },
    hasMore: () => wanted.size > 0,
    onEnd: () => {
      ended += 1
    },
  }
  return { rounds, asked, attempts, wanted, ended: () => ended }
}

async function run(rounds: Rounds<string>) {
  vi.useFakeTimers()
  const done = resolveInRounds(rounds, PAUSE_MS)
  await vi.runAllTimersAsync()
  await done
}

afterEach(() => {
  vi.useRealTimers()
})

describe('resolveInRounds', () => {
  it('asks in batches until everything is answered', async () => {
    const { rounds, asked } = lookup(['a', 'b', 'c'], [['a', 'b'], ['c']], 2)

    await run(rounds)

    expect(asked).toEqual([['a', 'b'], ['c']])
  })

  it('counts nothing against a round that answered part of its batch', async () => {
    const { rounds, attempts } = lookup(['a', 'b'], [['a'], [], ['b']])

    await run(rounds)

    expect(attempts.get('b')).toBe(1)
    expect(attempts.has('a')).toBe(false)
  })

  it('gives an item up after its attempts are used up', async () => {
    const { rounds, asked, wanted } = lookup(['a'], [])

    await run(rounds)

    expect(asked).toEqual([['a'], ['a']])
    expect(wanted.size).toBe(0)
  })

  it('pauses between rounds, and not after the last one', async () => {
    vi.useFakeTimers()
    const { rounds, asked } = lookup(['a', 'b'], [['a'], ['b']], 1)
    const done = resolveInRounds(rounds, PAUSE_MS)

    await vi.advanceTimersByTimeAsync(PAUSE_MS - 1)
    expect(asked).toHaveLength(1)
    await vi.advanceTimersByTimeAsync(1)
    await done

    expect(asked).toHaveLength(2)
    expect(vi.getTimerCount()).toBe(0)
  })

  it('stops when the service turns out to be switched off', async () => {
    const { rounds, asked } = lookup(['a', 'b'], [], 1)
    rounds.ask = async (batch) => {
      asked.push(batch)
      return null
    }

    await run(rounds)

    expect(asked).toEqual([['a']])
  })

  it('ends before its promise settles, so a request right after it is not turned away', async () => {
    const { rounds, ended } = lookup(['a'], [['a']])
    let endedBeforeSettling = false
    const done = resolveInRounds(rounds, PAUSE_MS).then(() => {
      endedBeforeSettling = ended() === 1
    })

    await done

    expect(endedBeforeSettling).toBe(true)
  })

  it('ends when a request fails, and passes the failure on', async () => {
    const { rounds, ended } = lookup(['a'], [])
    rounds.ask = () => Promise.reject(new Error('offline'))

    await expect(resolveInRounds(rounds, PAUSE_MS)).rejects.toThrow('offline')
    expect(ended()).toBe(1)
  })

  it('stops once no longer active', async () => {
    const { rounds, asked } = lookup(['a', 'b'], [['a'], ['b']], 1)
    rounds.active = () => asked.length === 0

    await run(rounds)

    expect(asked).toEqual([['a']])
  })
})
