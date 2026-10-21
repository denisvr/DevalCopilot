import { useCallback, useEffect, useRef } from 'react'
import { localExecutionSequenceStatusClient } from '../../../api/clients'
import { deriveLocalExecutionSequenceControl, isLocalExecutionSequenceLive, toLocalExecutionSequenceStatus } from '../localExecutionSequence'
import type { LocalExecutionSequenceControl, LocalExecutionSequenceStatus } from '../localExecutionSequence'
import { startBoundedRead } from './boundedRead'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'
import type { OwnedLifetime } from './useOwnedLifetime'

/** How long the recorded status waits between two reads while authority or a linked owner is unresolved (ADR-0060). */
export const LOCAL_EXECUTION_POLL_INTERVAL_MS = 5000

/** Each read's request is aborted after this long. */
export const LOCAL_EXECUTION_READ_TIMEOUT_MS = 10_000

/** How many reads in a row may fail or be refused before polling stops; a later hint, refresh or remount reads again. */
export const LOCAL_EXECUTION_MAX_CONSECUTIVE_FAILURES = 3

/**
 * `read`/`timeout`: the request failed or exceeded its ten-second bound. `not-found`: the host no longer knows this run. `invalid`: the
 * answer is not coherent for it. `unavailable`: the host answered, coherently, that it could not establish the manual-control fence.
 */
export type LocalExecutionSequenceReadFailure = 'read' | 'timeout' | 'not-found' | 'invalid' | 'unavailable'

/** The change barrier of one command (arm or stop): its write settled, so only a read that began afterwards can lift the mask. */
export interface LocalExecutionCommandTicket {
  /** Call exactly when the command's actual transport settled (answered, failed or aborted). Idempotent; inert after its lifetime ended. */
  settled: () => void
}

export interface UseLocalExecutionSequenceResult {
  /** The run this reading belongs to; null when no run is selected. */
  runId: string | null
  /** The validated recorded status of the current lifetime; null while nothing was validated, after a failed read and during a command barrier. */
  status: LocalExecutionSequenceStatus | null
  /** True until the first read of this lifetime settled. */
  loading: boolean
  failure: LocalExecutionSequenceReadFailure | null
  /** Who holds automatic authority over the run; `unknown` for anything that is not a validated, established, unmasked answer. */
  control: LocalExecutionSequenceControl
  /** Whether further bounded status reads are scheduled by timer. */
  polling: boolean
  /** True from a command's submission until the first read that began after its transport settled has settled. */
  confirming: boolean
  /** Reads the recorded status again (coalesced into the single queued read); inert when retained from an ended lifetime. */
  refresh: () => void
  /** Starts the barrier of one command, or null when none can start (inactive lifetime, or another command is unconfirmed). */
  beginCommand: () => LocalExecutionCommandTicket | null
}

type Phase = 'idle' | 'applying' | 'confirming'

interface Flight {
  owner: OwnedLifetime | null
  busy: boolean
  queued: boolean
  cancel: (() => void) | null
  started: number
  phase: Phase
  confirmAfter: number
}

interface Frame {
  status: LocalExecutionSequenceStatus | null
  failure: LocalExecutionSequenceReadFailure | null
  settled: boolean
  readCount: number
  settledReads: number
  consecutiveFailures: number
  watching: boolean
  phase: Phase
}

const createFrame = (): Frame => ({
  status: null,
  failure: null,
  settled: false,
  readCount: 0,
  settledReads: 0,
  consecutiveFailures: 0,
  watching: true,
  phase: 'idle',
})

const createFlight = (owner: OwnedLifetime | null): Flight => ({ owner, busy: false, queued: false, cancel: null, started: 0, phase: 'idle', confirmAfter: 0 })

// Polling continues while authority or a linked owner is unresolved: a live state, an awaiting reconciliation or a Blocked fence.
const needsWatching = (status: LocalExecutionSequenceStatus): boolean =>
  status.manualFence === 'Blocked'
  || (status.sequence !== null && (isLocalExecutionSequenceLive(status.sequence.recordedState) || status.sequence.awaitingReconciliation))

/**
 * The one run-owned reader of the recorded local-execution-sequence status (ADR-0060), read-only. Its single result is shared by every
 * region of the cockpit; neither can read its own copy. The run's visit owns everything it exposes: another run, or a return to an
 * earlier one, derives a fresh loading state and never exposes the previous status, an older or ended read can neither overwrite nor
 * settle a newer one, and a `refresh` or ticket retained from an ended visit starts nothing.
 *
 * It reads once when the run is selected, again whenever `hint` changes (a durable run event or a notification) or `refresh` is called,
 * and at most once per five seconds by timer while authority or a linked owner is unresolved. Reads never overlap: a trigger that
 * arrives while one is pending is coalesced into exactly one further read after its actual request settles. Each request has a real
 * ten-second AbortController bound, also cancelled when the visit ends. A pending re-read keeps the last validated status as a hint;
 * a failed, timed-out, refused, incoherent or fence-unavailable read masks it, and timer polling stops after three consecutive failures.
 *
 * A command (arm or stop) masks the status from its submission until a read that began after its transport settled has settled: a read
 * already pending before it, a late answer or a stale callback restores nothing. Nothing is stored in the browser.
 */
export function useLocalExecutionSequence(runId: string | null, hint?: number): UseLocalExecutionSequenceResult {
  const owner = useOwnedLifetime(runId)
  const [frame, commit] = useOwnedState(owner, createFrame)
  const { readCount, settledReads, consecutiveFailures, watching, settled } = frame
  const flight = useRef<Flight>(createFlight(null))

  const flightOf = useCallback((lifetime: OwnedLifetime): Flight => {
    if (flight.current.owner !== lifetime) {
      flight.current = createFlight(lifetime)
    }

    return flight.current
  }, [])

  const refresh = useCallback(() => {
    if (!owner.isActive() || owner.key === null) {
      return
    }

    commit((previous) => ({ ...previous, readCount: previous.readCount + 1 }))
  }, [owner, commit])

  const beginCommand = useCallback((): LocalExecutionCommandTicket | null => {
    if (!owner.isActive() || owner.key === null) {
      return null
    }

    const state = flightOf(owner)
    if (state.phase !== 'idle') {
      return null
    }

    state.phase = 'applying'
    commit((previous) => ({ ...previous, phase: 'applying' }))
    const isCurrent = owner.begin('command')
    let done = false
    return {
      settled: () => {
        if (done || !isCurrent()) {
          return
        }

        done = true
        state.phase = 'confirming'
        state.confirmAfter = state.started
        commit((previous) => ({ ...previous, phase: 'confirming' }))
        // Every trigger coalesced while the command was unsettled becomes this one fresh read: behind an older read still in flight
        // (started when that transport settles) or right now, never both.
        if (state.busy) {
          state.queued = true
        } else {
          state.queued = false
          commit((previous) => ({ ...previous, readCount: previous.readCount + 1 }))
        }
      },
    }
  }, [owner, commit, flightOf])

  useEffect(() => {
    if (runId === null) {
      return
    }

    const state = flightOf(owner)
    if (state.busy || state.phase === 'applying') {
      state.queued = true
      return
    }

    state.busy = true
    const index = ++state.started
    const isCurrent = owner.begin('read')
    const settle = (answer: { status: LocalExecutionSequenceStatus | null; failure: LocalExecutionSequenceReadFailure | null }) => {
      if (!isCurrent()) {
        return
      }

      if (state.phase === 'applying' || (state.phase === 'confirming' && index <= state.confirmAfter)) {
        // A read that began before the command's transport settled says nothing about it: it neither restores an earlier status,
        // confirms the command nor rearms polling; the one confirming read follows the command.
        return
      }

      const confirmed = state.phase === 'confirming'
      if (confirmed) {
        state.phase = 'idle'
      }

      commit((previous) => ({
        ...previous,
        status: answer.status,
        failure: answer.failure,
        settled: true,
        settledReads: previous.settledReads + 1,
        consecutiveFailures: answer.status === null ? previous.consecutiveFailures + 1 : 0,
        watching: answer.status === null ? true : needsWatching(answer.status),
        phase: confirmed ? 'idle' : previous.phase,
      }))
    }

    const request = startBoundedRead((signal) => localExecutionSequenceStatusClient(signal).readLocalExecutionSequence(runId), LOCAL_EXECUTION_READ_TIMEOUT_MS)
    state.cancel = request.cancel
    void request.answer.then((answer) => {
      if ('failure' in answer) {
        settle({ status: null, failure: answer.failure })
        return
      }

      const decoded = toLocalExecutionSequenceStatus(answer.raw, runId)
      if (decoded === null) {
        settle({ status: null, failure: 'invalid' })
      } else if (decoded.manualFence === 'Unavailable') {
        settle({ status: null, failure: 'unavailable' })
      } else {
        settle({ status: decoded, failure: null })
      }
    })

    // The flight ends only when the actual request settled, never merely when its bound elapsed: a trigger that arrived meanwhile
    // becomes exactly one further read after it, so this lifetime never has two requests in flight.
    void request.transport.then(() => {
      state.busy = false
      state.cancel = null
      if (state.queued && owner.isActive()) {
        state.queued = false
        commit((previous) => ({ ...previous, readCount: previous.readCount + 1 }))
      }
    })
  }, [owner, commit, runId, hint, readCount, flightOf])

  useEffect(
    () => () => {
      flight.current.cancel?.()
      flight.current.cancel = null
    },
    [owner],
  )

  const polling = runId !== null && watching && consecutiveFailures < LOCAL_EXECUTION_MAX_CONSECUTIVE_FAILURES
  useEffect(() => {
    if (!polling || settledReads === 0) {
      return
    }

    // One timer per settled read: the next poll is scheduled only after the previous read settled, so polls never overlap or queue up.
    const timer = window.setTimeout(() => {
      commit((previous) => ({ ...previous, readCount: previous.readCount + 1 }))
    }, LOCAL_EXECUTION_POLL_INTERVAL_MS)
    return () => window.clearTimeout(timer)
  }, [polling, settledReads, commit])

  if (runId === null) {
    return { runId: null, status: null, loading: false, failure: null, control: 'none', polling: false, confirming: false, refresh, beginCommand }
  }

  const confirming = frame.phase !== 'idle'
  const status = confirming ? null : frame.status
  return {
    runId,
    status,
    loading: !settled,
    failure: frame.failure,
    control: deriveLocalExecutionSequenceControl(status),
    polling,
    confirming,
    refresh,
    beginCommand,
  }
}
