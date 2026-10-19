import { useCallback } from 'react'
import { stopPlanningSequenceClient } from '../../../api/clients'
import { toPlanningSequenceStopAnswer } from '../planningSequence'
import type { PlanningSequenceStopAnswer } from '../planningSequence'
import { PlanningSequenceAnswerInvalid, planningSequenceFailureMessage } from '../planningSequenceCopy'
import { useRunScopedAction } from './useRunScopedAction'

export interface UseStopPlanningSequenceResult {
  requesting: boolean
  error: string | null
  /**
   * Stops exactly `requestSequenceId` of exactly `requestRunId` after its current step. Resolves true only when the host's answer was
   * exactly the answer for that run and sequence AND this owner is still current; false for a refusal, an unknown outcome, a duplicate
   * of a request in flight, a run or sequence that is not the owner's, or an obsolete completion.
   */
  stop: (requestRunId: string, requestSequenceId: string) => Promise<boolean>
}

/**
 * Stop after current step for one recorded sequence of one run (ADR-0055). It is not process cancellation, run Stop or Pause: a step
 * that is already claimed may still dispatch and finish, and the answer says whether this call changed anything (`changed`) and what
 * the sequence is now. The run and the sequence own the whole interaction (request guard, pending state, error): replacing either ends
 * the owner, A to B to A is a new owner, an obsolete completion never touches a replacement, and a second request in flight on one
 * owner is ignored. A handler retained from an ended owner is dropped before any request is sent, so a stale click can never stop a
 * replaced run or sequence.
 *
 * The request is never resent. A stop can race a claim, a result or a deadline, so every attempt that reached the host is followed by
 * one read of the recorded status (`refreshStatus`, owned by the run's visit and never awaited) whatever the answer was; a late stop
 * of an ended sequence is answered unchanged and shows what it already was.
 */
export function useStopPlanningSequence(
  runId: string,
  sequenceId: string | null,
  refreshStatus: () => void,
  onAnswer?: (answer: PlanningSequenceStopAnswer) => void,
): UseStopPlanningSequenceResult {
  const ownerKey = JSON.stringify([runId, sequenceId])
  const { busy, error, run } = useRunScopedAction(ownerKey)

  const stop = useCallback(
    async (requestRunId: string, requestSequenceId: string) => {
      if (sequenceId === null || requestRunId !== runId || requestSequenceId !== sequenceId) {
        return false
      }

      let answer: PlanningSequenceStopAnswer | null = null
      return run(
        ownerKey,
        async () => {
          try {
            const raw = await stopPlanningSequenceClient().stopPlanningSequenceRaw(requestRunId, requestSequenceId)
            answer = toPlanningSequenceStopAnswer(raw, requestRunId, requestSequenceId)
            if (answer === null) {
              throw new PlanningSequenceAnswerInvalid()
            }
          } catch (caught: unknown) {
            refreshStatus()
            throw caught
          }
          refreshStatus()
        },
        {
          toMessage: planningSequenceFailureMessage,
          onSuccess: () => {
            if (answer !== null) {
              onAnswer?.(answer)
            }
          },
        },
      )
    },
    [run, runId, sequenceId, ownerKey, refreshStatus, onAnswer],
  )

  return { requesting: busy, error, stop }
}
