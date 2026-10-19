import { useCallback, useEffect, useRef } from 'react'
import { agentSupervisionClient } from '../../../api/clients'
import { isAgentSupervisionWatching, toAgentSupervisionReading } from '../agentSupervision'
import type { AgentSupervisionReading } from '../agentSupervision'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'
import type { OwnedLifetime } from './useOwnedLifetime'

/** How long the current supervision waits between two reads while the selected Agent attempt is not terminal (ADR-0056). */
export const AGENT_SUPERVISION_REFRESH_INTERVAL_MS = 5000

/** Each read's request is aborted after this long: a hung request can never keep an earlier success current. */
export const AGENT_SUPERVISION_REQUEST_TIMEOUT_MS = 10_000

/** How many reads in a row may fail or be refused before polling stops; a later notification, refresh or remount reads again. */
export const AGENT_SUPERVISION_MAX_CONSECUTIVE_FAILURES = 3

/**
 * `read`: the request failed. `timeout`: it did not answer within ten seconds. `not-found`: the host no longer knows this run.
 * `invalid`: the answer is not coherent for it.
 */
export type AgentSupervisionReadFailure = 'read' | 'timeout' | 'not-found' | 'invalid'

export interface UseAgentSupervisionResult {
  /** The validated reading of the current lifetime; null while nothing was validated and after a failed, timed-out or refused read. */
  reading: AgentSupervisionReading | null
  /** True until the first read of this lifetime settled. */
  loading: boolean
  failure: AgentSupervisionReadFailure | null
  /** The browser's monotonic clock when the validated reading arrived (the age of the reading); null without a reading. Never a heartbeat. */
  receivedAtMilliseconds: number | null
  /** Whether further bounded reads are scheduled (the attempt can still change, or nothing was validated yet). */
  polling: boolean
  /** Reads the supervision again now; a callback retained from a replaced or unmounted lifetime does nothing. */
  refresh: () => void
}

// The one request a lifetime has in flight: whether its transport is still unsettled, whether a trigger is waiting behind it, and how
// to abort it.
interface FlightState {
  owner: OwnedLifetime | null
  busy: boolean
  queued: boolean
  cancel: (() => void) | null
}

// Everything one run's visit owns: the validated answer or a failure, whether the first read settled, and the bounded polling state.
interface Frame {
  reading: AgentSupervisionReading | null
  failure: AgentSupervisionReadFailure | null
  receivedAtMilliseconds: number | null
  settled: boolean
  readCount: number
  settledReads: number
  consecutiveFailures: number
  // Whether the last validated reading can still change (or none was validated yet): only then is it read again by timer.
  watching: boolean
}

const createFrame = (): Frame => ({
  reading: null,
  failure: null,
  receivedAtMilliseconds: null,
  settled: false,
  readCount: 0,
  settledReads: 0,
  consecutiveFailures: 0,
  watching: true,
})

function isNotFound(caught: unknown): boolean {
  return typeof caught === 'object' && caught !== null && (caught as { status?: unknown }).status === 404
}

type ReadAnswer = { raw: unknown } | { failure: AgentSupervisionReadFailure }

/**
 * The request or its ten-second bound, whichever settles first; the loser is ignored. The read owns its actual transport request: when
 * the bound elapses the request is aborted, not merely abandoned, and `cancel` (the lifetime that owned the read ended) aborts it and
 * ends the bound at once, so no timer is left running. `answer` settles what is shown; `transport` settles only once the request itself
 * has, so the next read of the lifetime never starts while an earlier request is still in flight.
 */
function readWithin(runId: string, milliseconds: number): { answer: Promise<ReadAnswer>; transport: Promise<void>; cancel: () => void } {
  const controller = new AbortController()
  let finish: (value: ReadAnswer) => void = () => undefined
  let timer: number | undefined
  let transport: Promise<void> = Promise.resolve()
  const answer = new Promise<ReadAnswer>((resolve) => {
    let settled = false
    finish = (value) => {
      if (!settled) {
        settled = true
        window.clearTimeout(timer)
        resolve(value)
      }
    }

    timer = window.setTimeout(() => {
      finish({ failure: 'timeout' })
      controller.abort()
    }, milliseconds)
    transport = agentSupervisionClient(controller.signal)
      .readAgentSupervision(runId)
      .then(
        (raw) => finish({ raw }),
        (caught: unknown) => finish({ failure: isNotFound(caught) ? 'not-found' : 'read' }),
      )
  })

  return {
    answer,
    transport,
    cancel: () => {
      window.clearTimeout(timer)
      controller.abort()
    },
  }
}

/**
 * The current host supervision of one run's selected Agent attempt (ADR-0056), read-only: it writes nothing and invokes nothing, and
 * nothing it shows is a heartbeat the browser produced. The run's visit owns everything it exposes: another run, or a return to an
 * earlier one, derives a fresh loading state and never exposes the previous reading, an older read can neither overwrite nor settle a
 * newer one, and a `refresh` retained from a replaced or unmounted visit starts no request.
 *
 * It reads once when the run is selected and again whenever the cockpit's latest event sequence changes (a durable hint) or `refresh`
 * is called. While the validated attempt is not terminal it also reads every five seconds, and stops after three consecutive failed
 * reads. Reads never overlap: a trigger that arrives while one is pending is coalesced into exactly one further read after its actual
 * request settles. Each request is aborted after ten seconds and when the visit ends (another run is selected or the view unmounts), so
 * an abandoned request never stays in flight. A pending re-read keeps the last validated reading, and a failed, timed-out, refused or
 * incoherent read masks it, so a stalled transport can never keep an earlier success current. Nothing is stored in the browser.
 */
export function useAgentSupervision(runId: string | null, latestEventSequence?: number): UseAgentSupervisionResult {
  const owner = useOwnedLifetime(runId)
  const [frame, commit] = useOwnedState(owner, createFrame)
  const { readCount, settledReads, consecutiveFailures, watching, settled } = frame
  const flight = useRef<FlightState>({ owner: null, busy: false, queued: false, cancel: null })

  const refresh = useCallback(() => {
    if (!owner.isActive() || owner.key === null) {
      return
    }

    commit((previous) => ({ ...previous, readCount: previous.readCount + 1 }))
  }, [owner, commit])

  useEffect(() => {
    if (runId === null) {
      return
    }

    if (flight.current.owner !== owner) {
      flight.current = { owner, busy: false, queued: false, cancel: null }
    }

    const state = flight.current
    if (state.busy) {
      // One request at a time: this trigger becomes exactly one further read once the pending one settles.
      state.queued = true
      return
    }

    state.busy = true
    const isCurrent = owner.begin('read')
    const settle = (answer: { reading: AgentSupervisionReading | null; failure: AgentSupervisionReadFailure | null }) => {
      if (isCurrent()) {
        commit((previous) => ({
          ...previous,
          reading: answer.reading,
          failure: answer.failure,
          receivedAtMilliseconds: answer.reading === null ? null : performance.now(),
          settled: true,
          settledReads: previous.settledReads + 1,
          consecutiveFailures: answer.reading === null ? previous.consecutiveFailures + 1 : 0,
          watching: answer.reading === null ? previous.watching : isAgentSupervisionWatching(answer.reading),
        }))
      }
    }

    const request = readWithin(runId, AGENT_SUPERVISION_REQUEST_TIMEOUT_MS)
    state.cancel = request.cancel
    void request.answer.then((answer) => {
      if ('failure' in answer) {
        settle({ reading: null, failure: answer.failure })
        return
      }

      const decoded = toAgentSupervisionReading(answer.raw, runId)
      settle(decoded === null ? { reading: null, failure: 'invalid' } : { reading: decoded, failure: null })
    })

    // The flight ends only when the actual request settled (answered, failed or aborted), never merely when its bound elapsed: a trigger
    // that arrived meanwhile becomes exactly one further read after it, so this lifetime never has two requests in flight.
    void request.transport.then(() => {
      state.busy = false
      state.cancel = null
      if (state.queued && owner.isActive()) {
        state.queued = false
        commit((previous) => ({ ...previous, readCount: previous.readCount + 1 }))
      }
    })
  }, [owner, commit, runId, latestEventSequence, readCount])

  // The lifetime ended (another run was selected or the view unmounted): its pending request is aborted and can no longer settle
  // anything, and its time bound stops with it.
  useEffect(
    () => () => {
      flight.current.cancel?.()
      flight.current.cancel = null
    },
    [owner],
  )

  const polling = runId !== null && watching && consecutiveFailures < AGENT_SUPERVISION_MAX_CONSECUTIVE_FAILURES
  useEffect(() => {
    if (!polling || settledReads === 0) {
      return
    }

    // One timer per settled read: the next read is scheduled only after the previous one settled, so reads never overlap.
    const timer = window.setTimeout(() => {
      commit((previous) => ({ ...previous, readCount: previous.readCount + 1 }))
    }, AGENT_SUPERVISION_REFRESH_INTERVAL_MS)
    return () => window.clearTimeout(timer)
  }, [polling, settledReads, commit])

  return {
    reading: runId === null ? null : frame.reading,
    loading: runId !== null && !settled,
    failure: runId === null ? null : frame.failure,
    receivedAtMilliseconds: runId === null ? null : frame.receivedAtMilliseconds,
    polling,
    refresh,
  }
}
