import {
  GetLocalDeliveryReceiptResponse,
  LocalCommitOperationResponse,
  LocalDeliveryCheckpointResponse,
  LocalDeliveryCodeReviewResponse,
  LocalDeliveryHumanReviewResponse,
  LocalDeliveryReceiptResponse,
  LocalDeliveryVerificationResponse,
} from '../../api/generated/api-client'
import type { ILocalDeliveryReceiptResponse, ILocalDeliveryVerificationResponse } from '../../api/generated/api-client'
import type { ReceiptSource } from './localDeliveryReceipt'

/** Test-only builders of a coherent receipt response for one source; every field can be replaced to build a contradiction. */
export const sourceFor = (marker = 'a'): ReceiptSource => ({
  runId: `run-${marker}`,
  operationId: `operation-${marker}`,
  commitSha: 'c'.repeat(40),
  checkpointId: `checkpoint-${marker}`,
  checkpointNumber: 3,
})

export const completedOperation = (marker = 'a', patch: Partial<LocalCommitOperationResponse> = {}) =>
  new LocalCommitOperationResponse({
    operationId: `operation-${marker}`,
    status: 'Completed',
    checkpointId: `checkpoint-${marker}`,
    checkpointNumber: 3,
    codeReviewAttemptId: `review-${marker}`,
    humanCheckpointReviewId: `human-${marker}`,
    branchName: 'devalcopilot/run-1',
    parentCommitSha: 'a'.repeat(40),
    treeSha: 'b'.repeat(40),
    commitSha: 'c'.repeat(40),
    changedPathCount: 2,
    ...patch,
  })

export const verificationMember = (order: number, patch: Partial<ILocalDeliveryVerificationResponse> = {}) =>
  new LocalDeliveryVerificationResponse({
    order,
    commandId: `command-${order}`,
    executionId: `execution-${order}`,
    executionNumber: order + 4,
    commandName: `Backend tests ${order + 1}`,
    status: 'Passed',
    exitCode: 0,
    completedAtUtc: new Date(Date.UTC(2026, 9, 9, 12, order, 0)),
    ...patch,
  })

export const receiptFor = (source: ReceiptSource, patch: Partial<ILocalDeliveryReceiptResponse> = {}) =>
  new LocalDeliveryReceiptResponse({
    version: 1,
    runId: source.runId,
    operationId: source.operationId,
    objective: 'Implement the ledger',
    commitSha: source.commitSha,
    parentCommitSha: 'a'.repeat(40),
    treeSha: 'b'.repeat(40),
    branchName: 'devalcopilot/run-1',
    completedAtUtc: new Date(Date.UTC(2026, 9, 9, 13, 0, 0)),
    checkpoint: new LocalDeliveryCheckpointResponse({
      id: source.checkpointId,
      number: source.checkpointNumber,
      fingerprintSha256: 'f'.repeat(64),
      changedPathCount: 2,
    }),
    executionReportMessageId: 'report-1',
    codeReview: new LocalDeliveryCodeReviewResponse({ attemptId: 'review-attempt-1', attemptNumber: 5, approvalMessageId: 'approval-1' }),
    humanReview: new LocalDeliveryHumanReviewResponse({ reviewId: 'human-review-1', decision: 'Approved' }),
    verification: [verificationMember(0), verificationMember(1)],
    ...patch,
  })

export const availableFor = (source: ReceiptSource, patch: Partial<ILocalDeliveryReceiptResponse> = {}) =>
  new GetLocalDeliveryReceiptResponse({ state: 'Available', receipt: receiptFor(source, patch) })

export const stateOnly = (state: string) => new GetLocalDeliveryReceiptResponse({ state, receipt: undefined })
