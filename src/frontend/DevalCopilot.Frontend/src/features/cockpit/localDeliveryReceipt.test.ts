import { describe, expect, it } from 'vitest'
import { GetLocalDeliveryReceiptResponse, LocalCommitOperationResponse } from '../../api/generated/api-client'
import { availableFor, completedOperation, receiptFor, sourceFor, stateOnly, verificationMember } from './localDeliveryReceiptFixture'
import { receiptSourceKey, toLocalDeliveryReading, toReceiptSource } from './localDeliveryReceipt'

const source = sourceFor()
const checkpoint = { id: 'checkpoint-a', number: 3, fingerprintSha256: 'f'.repeat(64), changedPathCount: 2 }

describe('toReceiptSource', () => {
  it('names the source only for a completed operation that carries every identifying fact', () => {
    expect(toReceiptSource('run-a', completedOperation())).toEqual(source)
  })

  it.each([
    ['not completed', { status: 'Executing' }],
    ['a missing status', { status: undefined }],
    ['no operation id', { operationId: undefined }],
    ['an empty operation id', { operationId: ' ' }],
    ['no commit sha', { commitSha: undefined }],
    ['a malformed commit sha', { commitSha: 'zz' }],
    ['no checkpoint id', { checkpointId: undefined }],
    ['no checkpoint number', { checkpointNumber: undefined }],
    ['a non-positive checkpoint number', { checkpointNumber: 0 }],
    ['a checkpoint number beyond the safe integers', { checkpointNumber: 2 ** 53 }],
    ['a fractional checkpoint number', { checkpointNumber: 1.5 }],
  ])('is null for %s', (_name, patch) => {
    expect(toReceiptSource('run-a', completedOperation('a', patch as Partial<LocalCommitOperationResponse>))).toBeNull()
  })

  it('is null without an operation or a run', () => {
    expect(toReceiptSource('run-a', null)).toBeNull()
    expect(toReceiptSource('', completedOperation())).toBeNull()
  })

  it('has a key that changes with every identifying fact', () => {
    const keys = new Set([
      receiptSourceKey(source),
      receiptSourceKey({ ...source, runId: 'run-b' }),
      receiptSourceKey({ ...source, operationId: 'operation-b' }),
      receiptSourceKey({ ...source, commitSha: 'd'.repeat(40) }),
      receiptSourceKey({ ...source, checkpointId: 'checkpoint-b' }),
      receiptSourceKey({ ...source, checkpointNumber: 4 }),
    ])
    expect(keys.size).toBe(6)
  })
})

describe('toLocalDeliveryReading', () => {
  it('accepts a coherent version-1 receipt and keeps the recorded order and values', () => {
    const reading = toLocalDeliveryReading(availableFor(source), source)

    expect(reading?.kind).toBe('Available')
    if (reading?.kind !== 'Available') {
      throw new Error('expected an available receipt')
    }
    const { receipt } = reading
    expect(receipt.commitSha).toBe(source.commitSha)
    expect(receipt.checkpoint).toEqual(checkpoint)
    expect(receipt.codeReview).toEqual({ attemptId: 'review-attempt-1', attemptNumber: 5, approvalMessageId: 'approval-1' })
    expect(receipt.humanReview).toEqual({ reviewId: 'human-review-1', decision: 'Approved' })
    expect(receipt.verification.map((member) => [member.order, member.commandName, member.executionNumber])).toEqual([
      [0, 'Backend tests 1', 4],
      [1, 'Backend tests 2', 5],
    ])
  })

  it('accepts the Unavailable state only without a receipt', () => {
    expect(toLocalDeliveryReading(stateOnly('Unavailable'), source)).toEqual({ kind: 'Unavailable' })
    expect(
      toLocalDeliveryReading(new GetLocalDeliveryReceiptResponse({ state: 'Unavailable', receipt: receiptFor(source) }), source),
    ).toBeNull()
  })

  it('rejects the states that contradict a completed operation, an unknown state and a missing receipt', () => {
    expect(toLocalDeliveryReading(stateOnly('NotRecorded'), source)).toBeNull()
    expect(toLocalDeliveryReading(stateOnly('NotCompleted'), source)).toBeNull()
    expect(toLocalDeliveryReading(stateOnly('Bogus'), source)).toBeNull()
    expect(toLocalDeliveryReading(stateOnly('Available'), source)).toBeNull()
    expect(toLocalDeliveryReading(new GetLocalDeliveryReceiptResponse({}), source)).toBeNull()
    expect(toLocalDeliveryReading(null, source)).toBeNull()
    expect(toLocalDeliveryReading(undefined, source)).toBeNull()
  })

  it.each([
    ['another run', { runId: 'run-b' }],
    ['another operation', { operationId: 'operation-b' }],
    ['another commit', { commitSha: 'd'.repeat(40) }],
    ['another checkpoint', { checkpoint: { ...checkpoint, id: 'checkpoint-b' } }],
    ['another checkpoint number', { checkpoint: { ...checkpoint, number: 4 } }],
    ['an unsupported version', { version: 2 }],
    ['no version', { version: undefined }],
    ['a malformed parent', { parentCommitSha: 'nope' }],
    ['a malformed tree', { treeSha: 'T'.repeat(40) }],
    ['a malformed fingerprint', { checkpoint: { ...checkpoint, fingerprintSha256: 'f'.repeat(63) } }],
    ['no changed paths', { checkpoint: { ...checkpoint, changedPathCount: 0 } }],
    ['a changed-path count beyond the host maximum', { checkpoint: { ...checkpoint, changedPathCount: 129 } }],
    ['a changed-path count beyond the safe integers', { checkpoint: { ...checkpoint, changedPathCount: 2 ** 53 } }],
    ['a negative changed-path count', { checkpoint: { ...checkpoint, changedPathCount: -1 } }],
    ['a fractional changed-path count', { checkpoint: { ...checkpoint, changedPathCount: 1.5 } }],
    ['a reviewer attempt number beyond the safe integers', { codeReview: { attemptId: 'a', attemptNumber: 2 ** 53, approvalMessageId: 'b' } }],
    ['a fractional reviewer attempt number', { codeReview: { attemptId: 'a', attemptNumber: 1.5, approvalMessageId: 'b' } }],
    ['a verification execution number beyond the safe integers', { verification: [verificationMember(0), verificationMember(1, { executionNumber: 2 ** 53 })] }],
    ['a fractional verification execution number', { verification: [verificationMember(0), verificationMember(1, { executionNumber: 1.5 })] }],
    ['no branch', { branchName: '' }],
    ['no objective', { objective: undefined }],
    ['an invalid completion time', { completedAtUtc: new Date('nope') }],
    ['no completion time', { completedAtUtc: undefined }],
    ['no execution report', { executionReportMessageId: undefined }],
    ['no code review', { codeReview: undefined }],
    ['a code review without attempt number', { codeReview: { attemptId: 'a', approvalMessageId: 'b' } }],
    ['a code review without approval', { codeReview: { attemptId: 'a', attemptNumber: 1 } }],
    ['no human review', { humanReview: undefined }],
    ['a declined human review', { humanReview: { reviewId: 'h', decision: 'ChangesRequested' } }],
    ['a human review without id', { humanReview: { decision: 'Approved' } }],
    ['no checkpoint', { checkpoint: undefined }],
    ['no verification list', { verification: undefined }],
    ['an empty verification list', { verification: [] }],
    ['thirty-three members', { verification: Array.from({ length: 33 }, (_value, order) => verificationMember(order)) }],
    ['a member out of order', { verification: [verificationMember(1), verificationMember(0)] }],
    ['a member order gap', { verification: [verificationMember(0), verificationMember(2)] }],
    ['a repeated execution', { verification: [verificationMember(0), verificationMember(1, { executionId: 'execution-0' })] }],
    ['a repeated command', { verification: [verificationMember(0), verificationMember(1, { commandId: 'command-0' })] }],
    ['a failed member', { verification: [verificationMember(0), verificationMember(1, { status: 'Failed' })] }],
    ['a member with a nonzero exit', { verification: [verificationMember(0), verificationMember(1, { exitCode: 1 })] }],
    ['a member without exit code', { verification: [verificationMember(0), verificationMember(1, { exitCode: undefined })] }],
    ['a nameless member', { verification: [verificationMember(0), verificationMember(1, { commandName: '' })] }],
    ['a member without execution number', { verification: [verificationMember(0), verificationMember(1, { executionNumber: 0 })] }],
    ['a member without completion', { verification: [verificationMember(0), verificationMember(1, { completedAtUtc: undefined })] }],
  ])('rejects a receipt with %s instead of repairing it', (_name, patch) => {
    expect(toLocalDeliveryReading(availableFor(source, patch as never), source)).toBeNull()
  })

  it('accepts a changed-path count at the host maximum and the largest safe checkpoint identity', () => {
    const maximum = { ...checkpoint, changedPathCount: 128, number: Number.MAX_SAFE_INTEGER }
    const safeSource = { ...source, checkpointNumber: Number.MAX_SAFE_INTEGER }

    expect(toLocalDeliveryReading(availableFor(safeSource, { checkpoint: maximum } as never), safeSource)?.kind).toBe('Available')
    expect(toReceiptSource('run-a', completedOperation('a', { checkpointNumber: 2 ** 53 }))).toBeNull()
    expect(toReceiptSource('run-a', completedOperation('a', { checkpointNumber: Number.MAX_SAFE_INTEGER }))).not.toBeNull()
  })

  it.each([[0], [''], [false], [Number.NaN]])('rejects an Unavailable state that carries the falsy non-null receipt %j', (falsy) => {
    const response = new GetLocalDeliveryReceiptResponse({ state: 'Unavailable' })
    ;(response as unknown as { receipt: unknown }).receipt = falsy

    expect(toLocalDeliveryReading(response, source)).toBeNull()
  })

  it('treats a null or undefined receipt as the generated client absent receipt for the Unavailable state', () => {
    for (const absent of [null, undefined]) {
      const response = new GetLocalDeliveryReceiptResponse({ state: 'Unavailable' })
      ;(response as unknown as { receipt: unknown }).receipt = absent

      expect(toLocalDeliveryReading(response, source)).toEqual({ kind: 'Unavailable' })
    }
  })

  it('accepts exactly thirty-two members', () => {
    const members = Array.from({ length: 32 }, (_value, order) => verificationMember(order))
    const reading = toLocalDeliveryReading(availableFor(source, { verification: members }), source)

    expect(reading?.kind === 'Available' && reading.receipt.verification.length).toBe(32)
  })
})
