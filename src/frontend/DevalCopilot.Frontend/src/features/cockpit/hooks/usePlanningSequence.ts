import { useCallback, useEffect } from 'react'
import { planningSequenceStatusClient } from '../../../api/clients'
import { derivePlanningSequenceControl, isPlanningSequenceLive, toPlanningSequenceStatus } from '../planningSequence'
import type { PlanningSequenceControl, PlanningSequenceStatus } from '../planningSequence'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'

/** How long the recorded status waits between two reads while a sequence can still advance. */
export const PLANNING_SEQUENCE_POLL_INTERVAL_MS = 2000

/** How many reads in a row may fail or be refused before polling stops; a later notification, refresh or remount reads again. */
export const PLANNING_SEQUENCE_MAX_CONSECUTIVE_FAILURES = 3

/**
 * `read`: the request failed. `not-found`: the host no longer knows this run. `invalid`: the answer is not coherent for it.
 * `unavailable`: the host answered, coherently, that it could not establish the manual-control fence.
 */
export type PlanningSequenceReadFailure = 'read' | 'not-found' | 'invalid' | 'unavailable'

export interface UsePlanningSequenceResult {
  /** The validated recorded status of the current lifetime; null while nothing was validated and after a failed read. */
  status: PlanningSequenceStatus | null
  /** True until the first read of this lifetime settled. */
  loading: boolean
  failure: PlanningSequenceReadFailure | null
  /** Who holds automatic authority over the run; `unknown` for anything that is not a validated, established answer. */
  control: PlanningSequenceControl
  /** `unknown` withholds both arming and every manual mutation action; `known` only says the fence was established. */
  ownership: 'unknown' | 'known'
  /** Whether further bounded status reads are scheduled (the sequence can still advance, or nothing was validated yet). */
  polling: boolean
  /** Reads the recorded status again now; a callback retained from a replaced or unmounted lifetime does nothing. */
  refresh: () => void
}

// Everything one run's visit owns: the validated answer or a failure, whether the first read settled, and the bounded polling state.
interface Frame {
  status: PlanningSequenceStatus | null
  failure: PlanningSequenceReadFailure | null
  settled: boolean
  readCount: number
  settledReads: number
  consecutiveFailures: number
  // Whether the last validated answer can still advance (or none was validated yet): only then is the status read again by timer.
  watching: boolean
}

const createFrame = (): Frame => ({
  status: null,
  failure: null,
  settled: false,
  readCount: 0,
  settledReads: 0,
  consecutiveFailures: 0,
  watching: true,
})

function isNotFound(caught: unknown): boolean {
  return typeof caught === 'object' && caught !== null && (caught as { status?: unknown }).status === 404
}

const canStillAdvance = (status: PlanningSequenceStatus): boolean => status.sequence !== null && isPlanningSequenceLive(status.sequence.recordedState)

/**
 * The recorded planning-sequence status of one run (ADR-0055), read-only: it writes nothing and invokes nothing. The run's visit owns
 * everything it exposes: another run, or a return to an earlier one, derives a fresh loading state and never exposes the previous
 * status, an older or ended read can neither overwrite nor settle a newer one, and a `refresh` retained from a replaced or unmounted
 * visit starts no request.
 *
 * It reads once when the run is selected and again whenever the cockpit's latest event sequence changes or `refresh` is called (after
 * an arming or a stop). While the validated status is Armed, PreparingStep, AwaitingStepResult or Stopping it also reads every two
 * seconds, and stops after three consecutive failed reads; closing the window never stops the host coordinator. A pending
 * re-read keeps the last validated answer, and a failed, refused or incoherent read masks it: nothing validated is `unknown`, which
 * withholds arming and every manual mutation action. Nothing is stored in the browser, and no mutation is ever retried here.
 */
export function usePlanningSequence(runId: string | null, latestEventSequence?: number): UsePlanningSequenceResult {
  const owner = useOwnedLifetime(runId)
  const [frame, commit] = useOwnedState(owner, createFrame)
  const { readCount, settledReads, consecutiveFailures, watching, settled } = frame

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

    const isCurrent = owner.begin('read')
    const settle = (answer: { status: PlanningSequenceStatus | null; failure: PlanningSequenceReadFailure | null }) => {
      if (!isCurrent()) {
        return
      }

      commit((previous) => ({
        ...previous,
        status: answer.status,
        failure: answer.failure,
        settled: true,
        settledReads: previous.settledReads + 1,
        consecutiveFailures: answer.status === null ? previous.consecutiveFailures + 1 : 0,
        watching: answer.status === null ? previous.watching : canStillAdvance(answer.status),
      }))
    }

    planningSequenceStatusClient()
      .readPlanningSequence(runId)
      .then(
        (raw) => {
          const decoded = toPlanningSequenceStatus(raw, runId)
          if (decoded === null) {
            settle({ status: null, failure: 'invalid' })
          } else if (decoded.manualFence === 'Unavailable') {
            settle({ status: null, failure: 'unavailable' })
          } else {
            settle({ status: decoded, failure: null })
          }
        },
        (caught: unknown) => settle({ status: null, failure: isNotFound(caught) ? 'not-found' : 'read' }),
      )
  }, [owner, commit, runId, latestEventSequence, readCount])

  const polling = runId !== null && watching && consecutiveFailures < PLANNING_SEQUENCE_MAX_CONSECUTIVE_FAILURES
  useEffect(() => {
    if (!polling || settledReads === 0) {
      return
    }

    // One timer per settled read: the next read is scheduled only after the previous one settled, so reads never overlap.
    const timer = window.setTimeout(() => {
      commit((previous) => ({ ...previous, readCount: previous.readCount + 1 }))
    }, PLANNING_SEQUENCE_POLL_INTERVAL_MS)
    return () => window.clearTimeout(timer)
  }, [polling, settledReads, commit])

  const control = derivePlanningSequenceControl(runId === null ? null : frame.status)
  return {
    status: runId === null ? null : frame.status,
    loading: runId !== null && !settled,
    failure: runId === null ? null : frame.failure,
    control,
    ownership: control === 'unknown' ? 'unknown' : 'known',
    polling,
    refresh,
  }
}
