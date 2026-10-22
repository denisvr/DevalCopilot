import { localExecutionInspectionWindowClient } from '../../../api/clients'
import { decideLocalExecutionWindowAnswer } from '../localExecutionInspection'
import type { LocalExecutionStreamSelection } from './useLocalExecutionInspection'
import { useRecordedStreamWindow } from './useRecordedStreamWindow'
import type { UseRecordedStreamWindowResult } from './useRecordedStreamWindow'

/** The chosen step visit, the proved execution of its verification and the chosen stream (null: nothing chosen). */
export interface LocalExecutionInspectionWindowTarget {
  /** The lifetime of the chosen step visit; null while none is chosen. */
  stepKey: string | null
  runId: string
  sequenceId: string
  stepId: string
  executionId: string
  stream: LocalExecutionStreamSelection | null
}

/**
 * Manual, bounded windows of one recorded step's verification stdout or stderr (ADR-0061), scoped to exactly one
 * `(step visit, stream, stream visit)` lifetime. `useRecordedStreamWindow` owns the windowing, the 256 KiB source-byte view budget and
 * the whole-answer decision against the descriptor the choice was made from; this hook supplies only the step-scoped operation call
 * and its identity. Nothing loads until the owner asks, and nothing outlives its visit.
 */
export function useLocalExecutionInspectionWindow({
  stepKey,
  runId,
  sequenceId,
  stepId,
  executionId,
  stream,
}: LocalExecutionInspectionWindowTarget): UseRecordedStreamWindowResult {
  const name = stream?.stream ?? 'stdout'
  return useRecordedStreamWindow({
    ownerKey:
      stepKey === null || stream === null ? null : JSON.stringify([stepKey, runId, sequenceId, stepId, executionId, name, stream.visit]),
    descriptor: stream?.descriptor ?? null,
    read: (fromOffset, maxBytes) => localExecutionInspectionWindowClient().readWindow(runId, stepId, name, fromOffset, maxBytes),
    decide: (answer, request) =>
      decideLocalExecutionWindowAnswer(answer, {
        runId,
        sequenceId,
        stepId,
        executionId,
        stream: name,
        descriptor: request.descriptor,
        fromOffset: request.fromOffset,
        maxBytes: request.maxBytes,
      }),
  })
}
