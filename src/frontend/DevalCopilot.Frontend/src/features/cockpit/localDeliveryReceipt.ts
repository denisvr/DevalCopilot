import type { GetLocalDeliveryReceiptResponse, LocalCommitOperationResponse } from '../../api/clients'

/** The host never records more than this many verification members for one local delivery. */
export const MAXIMUM_RECEIPT_MEMBERS = 32

/** The host never admits a local commit of more changed paths than this. */
export const MAXIMUM_RECEIPT_CHANGED_PATHS = 128

/** Every identifying fact of the completed operation a receipt is read for. A response must repeat all of them exactly. */
export interface ReceiptSource {
  runId: string
  operationId: string
  commitSha: string
  checkpointId: string
  checkpointNumber: number
}

export interface LocalDeliveryMember {
  order: number
  commandId: string
  executionId: string
  executionNumber: number
  commandName: string
  completedAtUtc: Date
}

/** A receipt already proven consistent with its source: the recorded values only, nothing derived or defaulted. */
export interface LocalDeliveryReceipt {
  runId: string
  operationId: string
  objective: string
  commitSha: string
  parentCommitSha: string
  treeSha: string
  branchName: string
  completedAtUtc: Date
  checkpoint: { id: string; number: number; fingerprintSha256: string; changedPathCount: number }
  executionReportMessageId: string
  codeReview: { attemptId: string; attemptNumber: number; approvalMessageId: string }
  humanReview: { reviewId: string; decision: 'Approved' }
  verification: readonly LocalDeliveryMember[]
}

export type LocalDeliveryReading = { kind: 'Available'; receipt: LocalDeliveryReceipt } | { kind: 'Unavailable' }

const OBJECT_ID = /^[0-9a-f]{40}$/
const SHA256 = /^[0-9a-f]{64}$/

const isText = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
// A number that is not a safe integer cannot name an exact checkpoint, attempt, execution or count.
const isPositiveInteger = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value > 0
const isInstant = (value: unknown): value is Date => value instanceof Date && !Number.isNaN(value.getTime())

/** The source of a receipt read: only a Completed operation that carries every identifying fact. Anything else has no receipt to read. */
export function toReceiptSource(runId: string, operation: LocalCommitOperationResponse | null | undefined): ReceiptSource | null {
  if (
    !isText(runId)
    || !operation
    || operation.status !== 'Completed'
    || !isText(operation.operationId)
    || typeof operation.commitSha !== 'string'
    || !OBJECT_ID.test(operation.commitSha)
    || !isText(operation.checkpointId)
    || !isPositiveInteger(operation.checkpointNumber)
  ) {
    return null
  }

  return {
    runId,
    operationId: operation.operationId,
    commitSha: operation.commitSha,
    checkpointId: operation.checkpointId,
    checkpointNumber: operation.checkpointNumber,
  }
}

/** The lifetime key of a source: every identifying fact, so a change of any one is a different owner. */
export const receiptSourceKey = (source: ReceiptSource): string =>
  JSON.stringify([source.runId, source.operationId, source.commitSha, source.checkpointId, source.checkpointNumber])

function toMembers(verification: unknown): LocalDeliveryMember[] | null {
  if (!Array.isArray(verification) || verification.length < 1 || verification.length > MAXIMUM_RECEIPT_MEMBERS) {
    return null
  }

  const members: LocalDeliveryMember[] = []
  for (const [index, member] of verification.entries()) {
    if (
      !member
      || typeof member !== 'object'
      || member.order !== index
      || !isText(member.commandId)
      || !isText(member.executionId)
      || !isPositiveInteger(member.executionNumber)
      || !isText(member.commandName)
      || member.status !== 'Passed'
      || member.exitCode !== 0
      || !isInstant(member.completedAtUtc)
    ) {
      return null
    }

    members.push({
      order: index,
      commandId: member.commandId,
      executionId: member.executionId,
      executionNumber: member.executionNumber,
      commandName: member.commandName,
      completedAtUtc: member.completedAtUtc,
    })
  }

  return new Set(members.map((member) => member.executionId)).size === members.length
    && new Set(members.map((member) => member.commandId)).size === members.length
    ? members
    : null
}

/**
 * Accepts a host response for a Completed operation only when it is coherent and for exactly `source`: the Available state with a
 * version-1 receipt that repeats the run, operation, commit and checkpoint, carries every approval identity and 1 to 32 ordered,
 * unique, Passed, clean-exit members, or the Unavailable state with no receipt. A contradictory state (NotRecorded or NotCompleted for
 * a completed operation, an unknown state, a receipt on a state that carries none) or any missing, mismatched or malformed fact is
 * null and is never repaired, trimmed or substituted.
 */
export function toLocalDeliveryReading(
  response: GetLocalDeliveryReceiptResponse | null | undefined,
  source: ReceiptSource,
): LocalDeliveryReading | null {
  if (!response || typeof response !== 'object') {
    return null
  }

  if (response.state === 'Unavailable') {
    // Only the generated client's own absent-receipt representation (null or undefined) is absent; any other value, however falsy, is a payload.
    return response.receipt === null || response.receipt === undefined ? { kind: 'Unavailable' } : null
  }

  const receipt = response.receipt
  if (response.state !== 'Available' || !receipt || typeof receipt !== 'object') {
    return null
  }

  const { checkpoint, codeReview, humanReview } = receipt
  const verification = toMembers(receipt.verification)
  if (
    receipt.version !== 1
    || receipt.runId !== source.runId
    || receipt.operationId !== source.operationId
    || receipt.commitSha !== source.commitSha
    || !isText(receipt.objective)
    || !isText(receipt.branchName)
    || typeof receipt.parentCommitSha !== 'string'
    || !OBJECT_ID.test(receipt.parentCommitSha)
    || typeof receipt.treeSha !== 'string'
    || !OBJECT_ID.test(receipt.treeSha)
    || !isInstant(receipt.completedAtUtc)
    || !checkpoint
    || checkpoint.id !== source.checkpointId
    || checkpoint.number !== source.checkpointNumber
    || typeof checkpoint.fingerprintSha256 !== 'string'
    || !SHA256.test(checkpoint.fingerprintSha256)
    || !isPositiveInteger(checkpoint.changedPathCount)
    || checkpoint.changedPathCount > MAXIMUM_RECEIPT_CHANGED_PATHS
    || !isText(receipt.executionReportMessageId)
    || !codeReview
    || !isText(codeReview.attemptId)
    || !isPositiveInteger(codeReview.attemptNumber)
    || !isText(codeReview.approvalMessageId)
    || !humanReview
    || !isText(humanReview.reviewId)
    || humanReview.decision !== 'Approved'
    || !verification
  ) {
    return null
  }

  return {
    kind: 'Available',
    receipt: {
      runId: receipt.runId,
      operationId: receipt.operationId,
      objective: receipt.objective,
      commitSha: receipt.commitSha,
      parentCommitSha: receipt.parentCommitSha,
      treeSha: receipt.treeSha,
      branchName: receipt.branchName,
      completedAtUtc: receipt.completedAtUtc,
      checkpoint: {
        id: checkpoint.id,
        number: checkpoint.number,
        fingerprintSha256: checkpoint.fingerprintSha256,
        changedPathCount: checkpoint.changedPathCount,
      },
      executionReportMessageId: receipt.executionReportMessageId,
      codeReview: {
        attemptId: codeReview.attemptId,
        attemptNumber: codeReview.attemptNumber,
        approvalMessageId: codeReview.approvalMessageId,
      },
      humanReview: { reviewId: humanReview.reviewId, decision: 'Approved' },
      verification,
    },
  }
}
