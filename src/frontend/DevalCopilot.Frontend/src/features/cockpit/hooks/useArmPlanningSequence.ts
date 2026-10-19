import { useCallback } from 'react'
import { ArmPlanningSequenceRequest, armPlanningSequenceClient } from '../../../api/clients'
import { toPlanningSequenceArmReceipt } from '../planningSequence'
import type { PlanningSequenceArmReceipt, PlanningSequenceTuple } from '../planningSequence'
import { PlanningSequenceAnswerInvalid, planningSequenceFailureMessage } from '../planningSequenceCopy'
import { useRunScopedAction } from './useRunScopedAction'

export interface UseArmPlanningSequenceResult {
  requesting: boolean
  error: string | null
  /**
   * Arms exactly `tuple` for exactly `requestRunId`. Resolves true only when the host's receipt was exactly the receipt of that run and
   * tuple AND this owner is still current; false for a refusal, an unknown outcome, a duplicate of a request in flight, a run or tuple
   * that is not the owner's, or an obsolete completion.
   */
  arm: (requestRunId: string, tuple: PlanningSequenceTuple) => Promise<boolean>
}

const sameTuple = (left: PlanningSequenceTuple, right: PlanningSequenceTuple) =>
  left.workspaceId === right.workspaceId && left.checkpointId === right.checkpointId && left.fingerprintSha256 === right.fingerprintSha256

/**
 * Arms the one explicitly authorized planning sequence of a run (ADR-0055). The run and the exact candidate tuple the Human is looking
 * at own the whole interaction (request guard, pending state, error): replacing either ends the owner, A to B to A is a new owner, an
 * obsolete completion never touches a replacement, and a second request in flight on one owner is ignored. A handler retained from an
 * ended owner is dropped before any request is sent, so a stale click can never arm a replaced run or candidate.
 *
 * The request is never resent. A receipt that replays the identical tuple is the recorded receipt of the sequence and is reported as
 * such through `onReceipt`; it never rearms anything. Every attempt that reached the host is followed by one read of the recorded
 * status (`refreshStatus`, owned by the run's visit and never awaited), whatever the answer was, and an accepted request stays real
 * even when its owner was replaced. Fixed copy is chosen from the status and the stable problem code only.
 */
export function useArmPlanningSequence(
  runId: string,
  candidate: PlanningSequenceTuple | null,
  refreshStatus: () => void,
  onReceipt?: (receipt: PlanningSequenceArmReceipt) => void,
): UseArmPlanningSequenceResult {
  const ownerKey = JSON.stringify([runId, candidate])
  const { busy, error, run } = useRunScopedAction(ownerKey)

  const arm = useCallback(
    async (requestRunId: string, tuple: PlanningSequenceTuple) => {
      if (candidate === null || requestRunId !== runId || !sameTuple(tuple, candidate)) {
        return false
      }

      let receipt: PlanningSequenceArmReceipt | null = null
      return run(
        ownerKey,
        async () => {
          try {
            const raw = await armPlanningSequenceClient().armPlanningSequenceRaw(
              requestRunId,
              new ArmPlanningSequenceRequest({
                workspaceId: tuple.workspaceId,
                checkpointId: tuple.checkpointId,
                fingerprintSha256: tuple.fingerprintSha256,
              }),
            )
            receipt = toPlanningSequenceArmReceipt(raw, requestRunId, tuple)
            if (receipt === null) {
              throw new PlanningSequenceAnswerInvalid()
            }
          } catch (caught: unknown) {
            // Whatever the host did, only the recorded status can say; the read is the owner's own and starts nothing else.
            refreshStatus()
            throw caught
          }
          refreshStatus()
        },
        {
          toMessage: planningSequenceFailureMessage,
          onSuccess: () => {
            if (receipt !== null) {
              onReceipt?.(receipt)
            }
          },
        },
      )
    },
    [run, runId, candidate, ownerKey, refreshStatus, onReceipt],
  )

  return { requesting: busy, error, arm }
}
