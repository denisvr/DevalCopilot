import { describe, expect, it } from 'vitest'
import { ApiException, CheckpointApprovalEvidenceMemberResponse, CheckpointApprovalEvidenceResponse } from '../../../api/generated/api-client'
import { approvalSourceKey, classifyBundleRefusal, describeApprovalFailure, MAXIMUM_APPROVAL_MEMBERS, toApprovalBundle, toApprovalSource } from './checkpointApprovalBundle'

const source = { projectId: 'project-1', workspaceId: 'workspace-1', checkpointId: 'checkpoint-1', checkpointNumber: 3, fingerprintSha256: 'f'.repeat(64) }

const member = (number: number, overrides: Partial<CheckpointApprovalEvidenceMemberResponse> = {}) =>
  new CheckpointApprovalEvidenceMemberResponse({
    verificationCommandId: `command-${number}`,
    commandNumber: number,
    recipeLabel: `Recipe ${number}`,
    verificationExecutionId: `execution-${number}`,
    executionNumber: number * 10,
    ...overrides,
  })

const response = (members: CheckpointApprovalEvidenceMemberResponse[], overrides: Partial<CheckpointApprovalEvidenceResponse> = {}) =>
  new CheckpointApprovalEvidenceResponse({ ...source, members, ...overrides })

const problem = (code: string) => new ApiException('raw', 409, JSON.stringify({ errors: [{ code, detail: 'host text' }] }), {}, null)

describe('toApprovalBundle', () => {
  it('accepts a coherent ordered bundle for exactly the requested source and names every member in its key', () => {
    const bundle = toApprovalBundle(response([member(1), member(2)]), source)

    expect(bundle).not.toBeNull()
    expect(bundle!.members.map(item => item.executionId)).toEqual(['execution-1', 'execution-2'])
    expect(bundle!.checkpointNumber).toBe(3)
    expect(bundle!.workspaceId).toBe('workspace-1')
    expect(JSON.parse(bundle!.key)).toEqual([
      approvalSourceKey(source),
      [['command-1', 'execution-1'], ['command-2', 'execution-2']],
    ])
    expect(JSON.parse(approvalSourceKey(source))).toEqual(['project-1', 'workspace-1', 'checkpoint-1', 3, 'f'.repeat(64)])
  })

  it('gives two bundles of different members different keys', () => {
    const first = toApprovalBundle(response([member(1), member(2)]), source)!
    const second = toApprovalBundle(response([member(1), member(2, { verificationExecutionId: 'execution-2b' })]), source)!

    expect(first.key).not.toBe(second.key)
  })

  it.each([
    ['project', { projectId: 'project-2' }],
    ['workspace', { workspaceId: 'workspace-2' }],
    ['absent workspace', { workspaceId: undefined }],
    ['blank workspace', { workspaceId: '  ' }],
    ['checkpoint', { checkpointId: 'checkpoint-2' }],
    ['fingerprint', { fingerprintSha256: 'e'.repeat(64) }],
    ['different checkpoint number', { checkpointNumber: 999 }],
    ['absent checkpoint number', { checkpointNumber: undefined }],
    ['zero checkpoint number', { checkpointNumber: 0 }],
    ['non-integer checkpoint number', { checkpointNumber: 1.5 }],
  ])('refuses a response whose %s is not the source being read', (_name, overrides) => {
    expect(toApprovalBundle(response([member(1)], overrides), source)).toBeNull()
  })

  it.each([
    ['null', null],
    ['undefined', undefined],
    ['a string', 'bundle' as unknown],
  ])('refuses %s', (_name, value) => {
    expect(toApprovalBundle(value as CheckpointApprovalEvidenceResponse | null | undefined, source)).toBeNull()
  })

  it('refuses a missing, empty or non-array member list', () => {
    expect(toApprovalBundle(response([]), source)).toBeNull()
    expect(toApprovalBundle(new CheckpointApprovalEvidenceResponse({ ...source }), source)).toBeNull()
    expect(toApprovalBundle({ ...source, members: 'x' } as unknown as CheckpointApprovalEvidenceResponse, source)).toBeNull()
  })

  it('accepts exactly 32 members and refuses 33 instead of truncating', () => {
    const many = (count: number) => Array.from({ length: count }, (_, index) => member(index + 1))

    expect(toApprovalBundle(response(many(MAXIMUM_APPROVAL_MEMBERS)), source)?.members).toHaveLength(32)
    expect(toApprovalBundle(response(many(MAXIMUM_APPROVAL_MEMBERS + 1)), source)).toBeNull()
  })

  it.each([
    ['a missing command id', { verificationCommandId: undefined }],
    ['a blank execution id', { verificationExecutionId: '  ' }],
    ['a missing recipe label', { recipeLabel: undefined }],
    ['a zero command number', { commandNumber: 0 }],
    ['a fractional execution number', { executionNumber: 2.5 }],
    ['a negative execution number', { executionNumber: -1 }],
  ])('refuses a member with %s', (_name, overrides) => {
    expect(toApprovalBundle(response([member(1), member(2, overrides)]), source)).toBeNull()
  })

  it('refuses members that are out of command order, repeated or share an identity', () => {
    expect(toApprovalBundle(response([member(2), member(1)]), source)).toBeNull()
    expect(toApprovalBundle(response([member(1), member(1, { verificationCommandId: 'command-x', verificationExecutionId: 'execution-x' })]), source)).toBeNull()
    expect(toApprovalBundle(response([member(1), member(2, { verificationCommandId: 'command-1' })]), source)).toBeNull()
    expect(toApprovalBundle(response([member(1), member(2, { verificationExecutionId: 'execution-1' })]), source)).toBeNull()
  })

  it('refuses a null member', () => {
    expect(toApprovalBundle({ ...source, members: [null] } as unknown as CheckpointApprovalEvidenceResponse, source)).toBeNull()
  })
})

describe('toApprovalSource', () => {
  const evidence = { gitWorkspaceId: 'workspace-1', checkpointId: 'checkpoint-1', checkpointNumber: 3, fingerprintSha256: 'f'.repeat(64) }

  it('carries every observed identity fact of a complete evidence record', () => {
    expect(toApprovalSource('project-1', evidence)).toEqual(source)
  })

  it.each([
    ['no evidence', null],
    ['a missing workspace', { ...evidence, gitWorkspaceId: undefined }],
    ['a blank workspace', { ...evidence, gitWorkspaceId: '   ' }],
    ['a missing checkpoint', { ...evidence, checkpointId: undefined }],
    ['a missing checkpoint number', { ...evidence, checkpointNumber: undefined }],
    ['a zero checkpoint number', { ...evidence, checkpointNumber: 0 }],
    ['a fractional checkpoint number', { ...evidence, checkpointNumber: 2.5 }],
    ['a missing fingerprint', { ...evidence, fingerprintSha256: undefined }],
  ])('withholds the source for %s instead of repairing it', (_name, value) => {
    expect(toApprovalSource('project-1', value)).toBeNull()
  })

  it('gives a different source key for a change of any one admitted fact', () => {
    const keys = new Set([
      approvalSourceKey(source),
      approvalSourceKey({ ...source, projectId: 'project-2' }),
      approvalSourceKey({ ...source, workspaceId: 'workspace-2' }),
      approvalSourceKey({ ...source, checkpointId: 'checkpoint-2' }),
      approvalSourceKey({ ...source, checkpointNumber: 4 }),
      approvalSourceKey({ ...source, fingerprintSha256: 'e'.repeat(64) }),
    ])

    expect(keys.size).toBe(6)
  })
})

describe('classifyBundleRefusal and describeApprovalFailure', () => {
  it('classifies only the host fixed approval-evidence codes', () => {
    expect(classifyBundleRefusal(problem('approval_evidence.no_enabled_recipes'))).toBe('none')
    expect(classifyBundleRefusal(problem('approval_evidence.too_many_recipes'))).toBe('tooMany')
    expect(classifyBundleRefusal(problem('approval_evidence.verification_incomplete'))).toBe('incomplete')
    expect(classifyBundleRefusal(problem('reviews.checkpoint_not_current'))).toBe('unavailable')
    expect(classifyBundleRefusal(new Error('network'))).toBe('unavailable')
    expect(classifyBundleRefusal(new ApiException('raw', 500, 'not json', {}, null))).toBe('unavailable')
  })

  it('shows fixed text and never the host detail', () => {
    for (const code of ['reviews.approval_requires_complete_verification_set', 'reviews.checkpoint_not_current', 'something.else']) {
      expect(describeApprovalFailure(problem(code))).not.toContain('host text')
    }

    expect(describeApprovalFailure(problem('reviews.approval_requires_complete_verification_set'))).toContain('Refresh evidence')
    expect(describeApprovalFailure(new Error('SENTINEL'))).toBe('The complete-set approval could not be recorded.')
  })
})
