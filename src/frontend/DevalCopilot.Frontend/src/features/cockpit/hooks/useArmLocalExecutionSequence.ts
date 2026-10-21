import { useCallback, useEffect, useRef } from 'react'
import { armLocalExecutionSequenceClient } from '../../../api/clients'
import { toLocalExecutionArmReceipt } from '../localExecutionSequence'
import type { LocalExecutionArmReceipt, LocalExecutionArmTuple } from '../localExecutionSequence'
import { LocalExecutionAnswerInvalid, localExecutionFailureMessage } from '../localExecutionSequenceCopy'
import { startBoundedCommand } from './boundedCommand'
import type { BoundedCommand } from './boundedCommand'
import type { UseLocalExecutionSequenceResult } from './useLocalExecutionSequence'
import { useRunScopedAction } from './useRunScopedAction'

export const LOCAL_EXECUTION_COMMAND_BUSY = 'Another local execution request is still being confirmed. Nothing was sent.'

/** Raised when the run's reader cannot start a command barrier: nothing was sent. */
export class LocalExecutionCommandNotStarted extends Error {
  constructor() {
    super(LOCAL_EXECUTION_COMMAND_BUSY)
    this.name = 'LocalExecutionCommandNotStarted'
  }
}

export const localExecutionCommandMessage = (caught: unknown): string =>
  caught instanceof LocalExecutionCommandNotStarted ? LOCAL_EXECUTION_COMMAND_BUSY : localExecutionFailureMessage(caught)

export interface UseArmLocalExecutionSequenceResult {
  requesting: boolean
  error: string | null
  /**
   * Arms exactly `tuple` for exactly `requestRunId`. Resolves true only when the host's receipt was exactly the receipt of that run and
   * tuple AND this owner is still current; false for a refusal, an unknown outcome, a duplicate of a request in flight, a run or tuple
   * that is not the owner's, or an obsolete completion.
   */
  arm: (requestRunId: string, tuple: LocalExecutionArmTuple) => Promise<boolean>
}

const sameTuple = (left: LocalExecutionArmTuple, right: LocalExecutionArmTuple) =>
  left.planProposalMessageId === right.planProposalMessageId
  && left.workspaceId === right.workspaceId
  && left.checkpointId === right.checkpointId
  && left.fingerprintSha256 === right.fingerprintSha256
  && left.confirmationSha256 === right.confirmationSha256

/**
 * Arms the one explicitly authorized local execution sequence of a run (ADR-0060). The run and the exact confirmed tuple own the whole
 * interaction (request guard, pending state, error): replacing either ends the owner, A to B to A is a new owner, an obsolete completion
 * never touches a replacement, and a handler retained from an ended owner is dropped before any request is sent.
 *
 * The request is submitted once with a real ten-second AbortController bound and is NEVER resent. From submission, the run's one status
 * reader is masked by its change barrier (`reader.beginCommand`): it stays so until a status read that began after this request's
 * transport settled has settled. An ambiguous, failed or aborted request reconciles only through that status read; an accepted one stays
 * real even when its owner was replaced. `onSettled` (the availability read) is a hint and is never awaited.
 */
export function useArmLocalExecutionSequence(
  runId: string,
  candidate: LocalExecutionArmTuple | null,
  reader: Pick<UseLocalExecutionSequenceResult, 'beginCommand'>,
  onSettled: () => void,
  onReceipt?: (receipt: LocalExecutionArmReceipt) => void,
): UseArmLocalExecutionSequenceResult {
  const ownerKey = JSON.stringify([runId, candidate])
  const { busy, error, run } = useRunScopedAction(ownerKey)
  const command = useRef<BoundedCommand | null>(null)

  useEffect(
    () => () => {
      command.current?.cancel()
      command.current = null
    },
    [ownerKey],
  )

  const arm = useCallback(
    async (requestRunId: string, tuple: LocalExecutionArmTuple) => {
      if (candidate === null || requestRunId !== runId || !sameTuple(tuple, candidate)) {
        return false
      }

      let receipt: LocalExecutionArmReceipt | null = null
      return run(
        ownerKey,
        async () => {
          const ticket = reader.beginCommand()
          if (ticket === null) {
            throw new LocalExecutionCommandNotStarted()
          }

          const sent = startBoundedCommand((signal) => armLocalExecutionSequenceClient(signal).armLocalExecutionSequenceRaw(requestRunId, tuple))
          command.current = sent
          try {
            const raw = await sent.result
            receipt = toLocalExecutionArmReceipt(raw, requestRunId, tuple)
            if (receipt === null) {
              throw new LocalExecutionAnswerInvalid()
            }
          } finally {
            if (command.current === sent) {
              command.current = null
            }

            // Whatever the host did, only a status read that began after this transport settled can say; availability is a hint.
            ticket.settled()
            onSettled()
          }
        },
        {
          toMessage: localExecutionCommandMessage,
          onSuccess: () => {
            if (receipt !== null) {
              onReceipt?.(receipt)
            }
          },
        },
      )
    },
    [run, runId, candidate, ownerKey, reader, onSettled, onReceipt],
  )

  return { requesting: busy, error, arm }
}
