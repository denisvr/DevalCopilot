import { agentAttemptVerificationWindowClient } from '../../../api/clients'
import { decideVerificationWindowAnswer } from '../agentAttemptVerification'
import type { VerificationSelection } from './useAgentAttemptVerificationEvidence'
import { useRecordedStreamWindow } from './useRecordedStreamWindow'
import type { UseRecordedStreamWindowResult } from './useRecordedStreamWindow'

export type { VerificationWindowFailure, VerificationWindowState } from './useRecordedStreamWindow'

/** The opening the selection belongs to, the selected attempt and the chosen member stream (null: nothing chosen). */
export interface VerificationWindowTarget {
  openingKey: string | null
  runId: string
  attemptId: string
  selection: VerificationSelection | null
}

export type UseAgentAttemptVerificationWindowResult = UseRecordedStreamWindowResult

/**
 * Manual, bounded windows of one recorded verification member's stdout or stderr (ADR-0052), scoped to exactly one
 * `(opening, member, stream, visit)` lifetime. Every committed change of the opening, the chosen member or stream or its visit,
 * including a return to an earlier one and a Hide and reselection of the same stream in one batch, is a new owned lifetime (see
 * `useRecordedStreamWindow`, which owns the windowing, the 256 KiB source-byte view budget and the whole-answer decision of each
 * window against its own request and the descriptor the selection was made from). This hook supplies only the attempt-scoped operation
 * call and its identity.
 */
export function useAgentAttemptVerificationWindow({
  openingKey,
  runId,
  attemptId,
  selection,
}: VerificationWindowTarget): UseAgentAttemptVerificationWindowResult {
  const executionId = selection?.executionId
  const stream = selection?.stream
  const visit = selection?.visit
  return useRecordedStreamWindow({
    ownerKey:
      openingKey === null || selection === null ? null : JSON.stringify([openingKey, runId, attemptId, executionId, stream, visit]),
    descriptor: selection?.descriptor ?? null,
    read: (fromOffset, maxBytes) =>
      agentAttemptVerificationWindowClient().readVerificationWindow(runId, attemptId, executionId ?? '', stream ?? 'stdout', fromOffset, maxBytes),
    decide: (answer, request) =>
      decideVerificationWindowAnswer(answer, {
        runId,
        attemptId,
        executionId: executionId ?? '',
        stream: stream ?? 'stdout',
        descriptor: request.descriptor,
        fromOffset: request.fromOffset,
        maxBytes: request.maxBytes,
      }),
  })
}
