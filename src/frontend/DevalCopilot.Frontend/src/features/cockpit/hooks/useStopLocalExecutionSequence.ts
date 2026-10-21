import { useCallback, useEffect, useRef } from 'react'
import { stopLocalExecutionSequenceClient } from '../../../api/clients'
import { toLocalExecutionStopAnswer } from '../localExecutionSequence'
import type { LocalExecutionStopAnswer } from '../localExecutionSequence'
import { LocalExecutionAnswerInvalid } from '../localExecutionSequenceCopy'
import { startBoundedCommand } from './boundedCommand'
import type { BoundedCommand } from './boundedCommand'
import { LocalExecutionCommandNotStarted, localExecutionCommandMessage } from './useArmLocalExecutionSequence'
import type { UseLocalExecutionSequenceResult } from './useLocalExecutionSequence'
import { useRunScopedAction } from './useRunScopedAction'

export interface UseStopLocalExecutionSequenceResult {
  requesting: boolean
  error: string | null
  /**
   * Stops exactly `requestSequenceId` of exactly `requestRunId` after its current operation. Resolves true only when the host's answer
   * was exactly the answer for that run and sequence AND this owner is still current; false for a refusal, an unknown outcome, a
   * duplicate of a request in flight, a run or sequence that is not the owner's, or an obsolete completion.
   */
  stop: (requestRunId: string, requestSequenceId: string) => Promise<boolean>
}

/**
 * Stop after current operation for one recorded sequence of one run (ADR-0060). It is not process cancellation, run Stop or Pause: a
 * claimed operation may still dispatch and finish, and only successors are prevented. The run and the sequence own the whole
 * interaction; the request is submitted once with a real ten-second AbortController bound and never resent. Like arming, it is wrapped
 * by the run's status change barrier: the status stays masked, and every action disabled, until a read that began after its transport
 * settled has settled. A late stop of an ended sequence is answered unchanged and shows what it already was.
 */
export function useStopLocalExecutionSequence(
  runId: string,
  sequenceId: string | null,
  reader: Pick<UseLocalExecutionSequenceResult, 'beginCommand'>,
  onAnswer?: (answer: LocalExecutionStopAnswer) => void,
): UseStopLocalExecutionSequenceResult {
  const ownerKey = JSON.stringify([runId, sequenceId])
  const { busy, error, run } = useRunScopedAction(ownerKey)
  const command = useRef<BoundedCommand | null>(null)

  useEffect(
    () => () => {
      command.current?.cancel()
      command.current = null
    },
    [ownerKey],
  )

  const stop = useCallback(
    async (requestRunId: string, requestSequenceId: string) => {
      if (sequenceId === null || requestRunId !== runId || requestSequenceId !== sequenceId) {
        return false
      }

      let answer: LocalExecutionStopAnswer | null = null
      return run(
        ownerKey,
        async () => {
          const ticket = reader.beginCommand()
          if (ticket === null) {
            throw new LocalExecutionCommandNotStarted()
          }

          const sent = startBoundedCommand((signal) => stopLocalExecutionSequenceClient(signal).stopLocalExecutionSequenceRaw(requestRunId, requestSequenceId))
          command.current = sent
          try {
            const raw = await sent.result
            answer = toLocalExecutionStopAnswer(raw, requestRunId, requestSequenceId)
            if (answer === null) {
              throw new LocalExecutionAnswerInvalid()
            }
          } finally {
            if (command.current === sent) {
              command.current = null
            }

            ticket.settled()
          }
        },
        {
          toMessage: localExecutionCommandMessage,
          onSuccess: () => {
            if (answer !== null) {
              onAnswer?.(answer)
            }
          },
        },
      )
    },
    [run, runId, sequenceId, ownerKey, reader, onAnswer],
  )

  return { requesting: busy, error, stop }
}
