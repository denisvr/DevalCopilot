import { useCallback, useEffect, useRef } from 'react'
import { localExecutionSequenceAvailabilityClient } from '../../../api/clients'
import { toLocalExecutionSequenceAvailability } from '../localExecutionSequence'
import type { LocalExecutionSequenceAvailability } from '../localExecutionSequence'
import { startBoundedRead } from './boundedRead'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'
import type { OwnedLifetime } from './useOwnedLifetime'

/** Each availability read's request is aborted after this long. */
export const LOCAL_EXECUTION_AVAILABILITY_TIMEOUT_MS = 10_000

export type LocalExecutionAvailabilityFailure = 'read' | 'timeout' | 'not-found' | 'invalid'

export interface UseLocalExecutionSequenceAvailabilityResult {
  /** The validated availability of exactly this run and plan from this visit; never an answer of another run, plan or earlier visit. */
  availability: LocalExecutionSequenceAvailability | null
  /** True until the first read of this visit settled. */
  loading: boolean
  /** True while a read of this visit is in flight: the shown availability is then not yet settled for submission. */
  reading: boolean
  failure: LocalExecutionAvailabilityFailure | null
  /** Reads the availability again (coalesced into the single queued read); inert when retained from an ended visit. */
  refresh: () => void
}

interface Flight {
  owner: OwnedLifetime | null
  busy: boolean
  queued: boolean
  cancel: (() => void) | null
}

interface Frame {
  availability: LocalExecutionSequenceAvailability | null
  failure: LocalExecutionAvailabilityFailure | null
  settled: boolean
  reading: boolean
  readCount: number
}

const createFrame = (): Frame => ({ availability: null, failure: null, settled: false, reading: false, readCount: 0 })
const createFlight = (owner: OwnedLifetime | null): Flight => ({ owner, busy: false, queued: false, cancel: null })

/**
 * The recorded arming availability of one explicitly selected plan of one run (ADR-0060), read-only: it performs no Git observation and
 * starts nothing. The run and the plan together own the visit: another plan, another run or a return to an earlier one (A to B to A) is
 * a fresh visit that never exposes an earlier answer, an older or ended read can neither overwrite nor settle a newer one, and a
 * `refresh` retained from an ended visit starts no request. It reads once per visit and again when `hint` changes or `refresh` is
 * called; reads never overlap (a trigger meanwhile becomes exactly one further read after the actual request settled), and each has a
 * real ten-second AbortController bound that also ends with the visit. A failed, timed-out or incoherent read masks the last answer.
 */
export function useLocalExecutionSequenceAvailability(
  runId: string | null,
  planProposalMessageId: string | null,
  hint?: number,
): UseLocalExecutionSequenceAvailabilityResult {
  const key = runId === null || planProposalMessageId === null ? null : JSON.stringify([runId, planProposalMessageId])
  const owner = useOwnedLifetime(key)
  const [frame, commit] = useOwnedState(owner, createFrame)
  const { readCount } = frame
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

  useEffect(() => {
    if (runId === null || planProposalMessageId === null) {
      return
    }

    const state = flightOf(owner)
    if (state.busy) {
      state.queued = true
      return
    }

    state.busy = true
    const isCurrent = owner.begin('read')
    commit((previous) => ({ ...previous, reading: true }))
    const settle = (answer: { availability: LocalExecutionSequenceAvailability | null; failure: LocalExecutionAvailabilityFailure | null }) => {
      if (!isCurrent()) {
        return
      }

      commit((previous) => ({ ...previous, availability: answer.availability, failure: answer.failure, settled: true, reading: false }))
    }

    const request = startBoundedRead(
      (signal) => localExecutionSequenceAvailabilityClient(signal).readAvailability(runId, planProposalMessageId),
      LOCAL_EXECUTION_AVAILABILITY_TIMEOUT_MS,
    )
    state.cancel = request.cancel
    void request.answer.then((answer) => {
      if ('failure' in answer) {
        settle({ availability: null, failure: answer.failure })
        return
      }

      const decoded = toLocalExecutionSequenceAvailability(answer.raw, runId, planProposalMessageId)
      settle(decoded === null ? { availability: null, failure: 'invalid' } : { availability: decoded, failure: null })
    })

    void request.transport.then(() => {
      state.busy = false
      state.cancel = null
      if (state.queued && owner.isActive()) {
        state.queued = false
        commit((previous) => ({ ...previous, readCount: previous.readCount + 1 }))
      }
    })
  }, [owner, commit, runId, planProposalMessageId, hint, readCount, flightOf])

  useEffect(
    () => () => {
      flight.current.cancel?.()
      flight.current.cancel = null
    },
    [owner],
  )

  if (key === null) {
    return { availability: null, loading: false, reading: false, failure: null, refresh }
  }

  return { availability: frame.availability, loading: !frame.settled, reading: frame.reading || !frame.settled, failure: frame.failure, refresh }
}
