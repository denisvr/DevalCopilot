import {
  GetProjectRunHistoryResponse,
  ProjectRunHistoryEntryResponse,
  ProjectRunHistoryReceiptSourceResponse,
} from '../../api/generated/api-client'
import type { IProjectRunHistoryEntryResponse, IProjectRunHistoryReceiptSourceResponse } from '../../api/generated/api-client'

/** Test-only builders of a coherent run-history response; every field can be replaced to build a contradiction. */
export const HISTORY_PROJECT = 'project-1'

export const runIdFor = (executionNumber: number) => `run-${executionNumber}`

export const sourceResponseFor = (executionNumber: number, patch: Partial<IProjectRunHistoryReceiptSourceResponse> = {}) =>
  new ProjectRunHistoryReceiptSourceResponse({
    runId: runIdFor(executionNumber),
    operationId: `operation-${executionNumber}`,
    commitSha: 'c'.repeat(40),
    checkpointId: `checkpoint-${executionNumber}`,
    checkpointNumber: 3,
    ...patch,
  })

export const entryFor = (executionNumber: number, patch: Partial<IProjectRunHistoryEntryResponse> = {}) =>
  new ProjectRunHistoryEntryResponse({
    projectId: HISTORY_PROJECT,
    runId: runIdFor(executionNumber),
    executionNumber,
    objective: `Objective ${executionNumber}`,
    lifecycle: 'Created',
    stage: 'Intake',
    executionMode: 'ManualAgent',
    createdAtUtc: new Date(Date.UTC(2026, 9, 9, 10, executionNumber % 60, 0)),
    lastAdvancedAtUtc: new Date(Date.UTC(2026, 9, 9, 11, executionNumber % 60, 0)),
    receiptSource: undefined,
    ...patch,
  })

/** A Completed run whose recorded operation locates a receipt. */
export const deliveredEntryFor = (executionNumber: number, patch: Partial<IProjectRunHistoryEntryResponse> = {}) =>
  entryFor(executionNumber, {
    lifecycle: 'Completed',
    stage: 'Completed',
    receiptSource: sourceResponseFor(executionNumber),
    ...patch,
  })

/** Descending entries for the given execution numbers, with `hasMore` and the next cursor derived exactly as the host derives them. */
export const pageOf = (
  executionNumbers: readonly number[],
  options: { projectId?: string; hasMore?: boolean; next?: number | undefined; entries?: ProjectRunHistoryEntryResponse[] } = {},
) => {
  const hasMore = options.hasMore ?? false
  return new GetProjectRunHistoryResponse({
    projectId: options.projectId ?? HISTORY_PROJECT,
    entries: options.entries ?? executionNumbers.map((number) => entryFor(number)),
    hasMore,
    nextBeforeExecutionNumber: 'next' in options ? options.next : hasMore ? executionNumbers[executionNumbers.length - 1] : undefined,
  })
}

/** Execution numbers from `from` down to `to`, inclusive. */
export const descending = (from: number, to: number) => Array.from({ length: from - to + 1 }, (_, index) => from - index)
