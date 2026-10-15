import { describe, expect, it } from 'vitest'
import type { GetGitHubBranchPublicationApprovalResponse, RecordGitHubBranchPublicationApprovalResponse } from '../../api/clients'
import {
  PUBLICATION_STATEMENTS,
  publicationApprovalScopeKey,
  toPublicationApprovalReading,
  toRecordedPublicationApproval,
} from './gitHubBranchPublicationApproval'
import type { PublicationApprovalScope } from './gitHubBranchPublicationApproval'

const SHA = 'a'.repeat(40)
const BRANCH = 'devalcopilot/workspace/x/1'
const TARGET_AT = new Date('2026-10-14T12:00:00.000Z')
const OBSERVED = new Date('2026-10-14T12:01:00.000Z')
const BOUND = new Date('2026-10-14T12:02:00.000Z')
const APPROVED = new Date('2026-10-14T12:03:00.000Z')

const SCOPE: PublicationApprovalScope = {
  projectId: 'project-1',
  source: { runId: 'run-1', operationId: 'op-1', commitSha: SHA, branchName: BRANCH },
  target: { id: 'target-1', eventId: 'event-1', host: 'github.com', owner: 'octo-cat', repository: 'hello.world', recordedAtUtc: TARGET_AT },
}

const wireSource = () => ({ operationId: 'op-1', commitSha: SHA, branchName: BRANCH })
const wireTarget = () => ({ id: 'target-1', eventId: 'event-1', host: 'github.com', owner: 'octo-cat', repository: 'hello.world', recordedAtUtc: TARGET_AT })
const wireBinding = (overrides: Record<string, unknown> = {}) => ({
  id: 'binding-1',
  eventId: 'binding-event-1',
  repositoryNodeId: 'R_kgDOExample01',
  owner: 'octo-cat',
  repository: 'hello.world',
  observedAtUtc: OBSERVED,
  recordedAtUtc: BOUND,
  ...overrides,
})
const wireApproval = (overrides: Record<string, unknown> = {}) => ({
  id: 'approval-1',
  eventId: 'approval-event-1',
  host: 'github.com',
  action: 'PushExactCommit',
  policyVersion: 1,
  localCommitOperationId: 'op-1',
  workspaceId: 'workspace-1',
  targetId: 'target-1',
  bindingId: 'binding-1',
  repositoryNodeId: 'R_kgDOExample01',
  owner: 'octo-cat',
  repository: 'hello.world',
  branchName: BRANCH,
  commitSha: SHA,
  approvedAtUtc: APPROVED,
  ...overrides,
})
const get = (overrides: Record<string, unknown>) =>
  ({
    runId: 'run-1',
    status: 'Available',
    source: wireSource(),
    target: wireTarget(),
    binding: wireBinding(),
    approval: wireApproval(),
    ...overrides,
  }) as unknown as GetGitHubBranchPublicationApprovalResponse
const post = (overrides: Record<string, unknown> = {}) =>
  ({
    runId: 'run-1',
    source: wireSource(),
    target: wireTarget(),
    binding: wireBinding(),
    approval: wireApproval(),
    ...overrides,
  }) as unknown as RecordGitHubBranchPublicationApprovalResponse
const SUBMITTED = { approvalId: 'approval-1', bindingId: 'binding-1', repositoryNodeId: 'R_kgDOExample01' }

describe('publicationApprovalScopeKey', () => {
  it('changes with the project, run, operation, commit, branch and every target fact', () => {
    const key = publicationApprovalScopeKey(SCOPE)

    expect(publicationApprovalScopeKey({ ...SCOPE })).toBe(key)
    for (const changed of [
      { ...SCOPE, projectId: 'project-2' },
      { ...SCOPE, source: { ...SCOPE.source, runId: 'run-2' } },
      { ...SCOPE, source: { ...SCOPE.source, operationId: 'op-2' } },
      { ...SCOPE, source: { ...SCOPE.source, commitSha: 'b'.repeat(40) } },
      { ...SCOPE, source: { ...SCOPE.source, branchName: 'elsewhere' } },
      { ...SCOPE, target: { ...SCOPE.target, id: 'target-2' } },
      { ...SCOPE, target: { ...SCOPE.target, eventId: 'event-2' } },
      { ...SCOPE, target: { ...SCOPE.target, owner: 'someone-else' } },
      { ...SCOPE, target: { ...SCOPE.target, repository: 'renamed' } },
      { ...SCOPE, target: { ...SCOPE.target, recordedAtUtc: BOUND } },
    ]) {
      expect(publicationApprovalScopeKey(changed)).not.toBe(key)
    }
  })
})

describe('toPublicationApprovalReading', () => {
  it('admits an Available answer whole, for exactly this scope', () => {
    expect(toPublicationApprovalReading(get({}), SCOPE)).toEqual({
      kind: 'Available',
      binding: {
        id: 'binding-1',
        eventId: 'binding-event-1',
        repositoryNodeId: 'R_kgDOExample01',
        owner: 'octo-cat',
        repository: 'hello.world',
        observedAtUtc: OBSERVED,
        recordedAtUtc: BOUND,
      },
      approval: { id: 'approval-1', eventId: 'approval-event-1', workspaceId: 'workspace-1', approvedAtUtc: APPROVED },
    })
  })

  it('admits ApprovalNotRecorded with the exact scope and no approval, and the two empty statuses with nothing', () => {
    expect(toPublicationApprovalReading(get({ status: 'ApprovalNotRecorded', approval: null }), SCOPE)).toEqual({
      kind: 'ApprovalNotRecorded',
      binding: expect.objectContaining({ id: 'binding-1', repositoryNodeId: 'R_kgDOExample01' }),
    })
    expect(toPublicationApprovalReading(get({ status: 'NotRecorded', source: null, target: null, binding: null, approval: null }), SCOPE)).toEqual({
      kind: 'NotRecorded',
    })
    expect(toPublicationApprovalReading(get({ status: 'Unavailable', source: null, target: null, binding: null, approval: null }), SCOPE)).toEqual({
      kind: 'Unavailable',
    })
  })

  it('admits binding names that differ from the target only in ASCII case, an approval at the binding time and the longest node identifier', () => {
    expect(toPublicationApprovalReading(get({ binding: wireBinding({ owner: 'OCTO-CAT', repository: 'Hello.World' }) }), SCOPE)?.kind).toBe('Available')
    expect(toPublicationApprovalReading(get({ approval: wireApproval({ approvedAtUtc: BOUND }) }), SCOPE)?.kind).toBe('Available')
    const node = 'a+/='.repeat(64)
    expect(
      toPublicationApprovalReading(get({ binding: wireBinding({ repositoryNodeId: node }), approval: wireApproval({ repositoryNodeId: node }) }), SCOPE)?.kind,
    ).toBe('Available')
  })

  it.each([
    ['null response', null],
    ['another run', get({ runId: 'run-2' })],
    ['an unknown status', get({ status: 'Approved' })],
    ['a lowercase status', get({ status: 'available' })],
    ['the retired state member name', get({ status: undefined, state: 'Available' })],
    ['Available without an approval', get({ approval: null })],
    ['Available without a binding', get({ binding: null })],
    ['Available without a source', get({ source: null })],
    ['Available without a target', get({ target: null })],
    ['Available with another operation', get({ source: { ...wireSource(), operationId: 'op-2' } })],
    ['Available with another commit', get({ source: { ...wireSource(), commitSha: 'b'.repeat(40) } })],
    ['Available with another branch', get({ source: { ...wireSource(), branchName: 'elsewhere' } })],
    ['Available with another target', get({ target: { ...wireTarget(), id: 'target-2' } })],
    ['Available with another target event', get({ target: { ...wireTarget(), eventId: 'event-2' } })],
    ['Available with another target owner', get({ target: { ...wireTarget(), owner: 'someone-else' } })],
    ['Available with another target time', get({ target: { ...wireTarget(), recordedAtUtc: BOUND } })],
    ['a binding with another owner', get({ binding: wireBinding({ owner: 'someone-else' }) })],
    ['a binding with another repository', get({ binding: wireBinding({ repository: 'renamed.repo' }) })],
    ['a binding with a trimmed owner', get({ binding: wireBinding({ owner: ' octo-cat' }) })],
    ['a binding with an empty node id', get({ binding: wireBinding({ repositoryNodeId: '' }) })],
    ['a binding with a spaced node id', get({ binding: wireBinding({ repositoryNodeId: 'has space' }) })],
    ['a binding with a 257 character node id', get({ binding: wireBinding({ repositoryNodeId: 'a'.repeat(257) }) })],
    ['a binding observed before the target was recorded', get({ binding: wireBinding({ observedAtUtc: new Date(TARGET_AT.getTime() - 1) }) })],
    ['a binding bound before it was observed', get({ binding: wireBinding({ recordedAtUtc: new Date(OBSERVED.getTime() - 1) }) })],
    ['a binding with a string instant', get({ binding: wireBinding({ observedAtUtc: '2026-10-14T12:01:00Z' }) })],
    ['a binding without an id', get({ binding: wireBinding({ id: '' }) })],
    ['an approval with another host', get({ approval: wireApproval({ host: 'example.com' }) })],
    ['an approval with another action', get({ approval: wireApproval({ action: 'ForcePush' }) })],
    ['an approval with a lowercase action', get({ approval: wireApproval({ action: 'pushexactcommit' }) })],
    ['an approval with another policy version', get({ approval: wireApproval({ policyVersion: 2 }) })],
    ['an approval with a string policy version', get({ approval: wireApproval({ policyVersion: '1' }) })],
    ['an approval for another operation', get({ approval: wireApproval({ localCommitOperationId: 'op-2' }) })],
    ['an approval for another target', get({ approval: wireApproval({ targetId: 'target-2' }) })],
    ['an approval for another binding', get({ approval: wireApproval({ bindingId: 'binding-2' }) })],
    ['an approval for another node identifier', get({ approval: wireApproval({ repositoryNodeId: 'R_other' }) })],
    ['an approval with another owner', get({ approval: wireApproval({ owner: 'Octo-Cat' }) })],
    ['an approval with another repository', get({ approval: wireApproval({ repository: 'Hello.World' }) })],
    ['an approval for another branch', get({ approval: wireApproval({ branchName: 'elsewhere' }) })],
    ['an approval for another commit', get({ approval: wireApproval({ commitSha: 'b'.repeat(40) }) })],
    ['an approval without an id', get({ approval: wireApproval({ id: '' }) })],
    ['an approval without an event', get({ approval: wireApproval({ eventId: ' ' }) })],
    ['an approval without a workspace', get({ approval: wireApproval({ workspaceId: undefined }) })],
    ['an approval before the binding was recorded', get({ approval: wireApproval({ approvedAtUtc: new Date(BOUND.getTime() - 1) }) })],
    ['an approval with an invalid instant', get({ approval: wireApproval({ approvedAtUtc: new Date('x') }) })],
    ['an approval with a string instant', get({ approval: wireApproval({ approvedAtUtc: '2026-10-14T12:03:00Z' }) })],
    ['ApprovalNotRecorded with an approval', get({ status: 'ApprovalNotRecorded' })],
    ['ApprovalNotRecorded without a binding', get({ status: 'ApprovalNotRecorded', binding: null, approval: null })],
    ['ApprovalNotRecorded with another target', get({ status: 'ApprovalNotRecorded', target: { ...wireTarget(), id: 'target-2' }, approval: null })],
    ['NotRecorded with a source', get({ status: 'NotRecorded', target: null, binding: null, approval: null })],
    ['NotRecorded with an approval', get({ status: 'NotRecorded', source: null, target: null, binding: null })],
    ['Unavailable with a source', get({ status: 'Unavailable', target: null, binding: null, approval: null })],
    ['Unavailable with an approval', get({ status: 'Unavailable', source: null, target: null, binding: null })],
  ])('refuses the whole answer for %s', (_name, response) => {
    expect(toPublicationApprovalReading(response as GetGitHubBranchPublicationApprovalResponse | null, SCOPE)).toBeNull()
  })
})

describe('toRecordedPublicationApproval', () => {
  it('accepts exactly the answer to what was submitted', () => {
    expect(toRecordedPublicationApproval(post(), SCOPE, SUBMITTED)).toEqual({
      binding: expect.objectContaining({ id: 'binding-1' }),
      approval: { id: 'approval-1', eventId: 'approval-event-1', workspaceId: 'workspace-1', approvedAtUtc: APPROVED },
    })
  })

  it.each([
    ['another approval identifier', post({ approval: wireApproval({ id: 'approval-2' }) })],
    ['another binding', post({ binding: wireBinding({ id: 'binding-2' }), approval: wireApproval({ bindingId: 'binding-2' }) })],
    ['another node identifier', post({ binding: wireBinding({ repositoryNodeId: 'R_other' }), approval: wireApproval({ repositoryNodeId: 'R_other' }) })],
    ['another run', post({ runId: 'run-2' })],
    ['another source', post({ source: { ...wireSource(), commitSha: 'b'.repeat(40) } })],
    ['another target', post({ target: { ...wireTarget(), id: 'target-2' } })],
    ['no approval', post({ approval: null })],
    ['no binding', post({ binding: null })],
    ['a malformed approval', post({ approval: wireApproval({ action: 'ForcePush' }) })],
    ['an approval for another commit', post({ approval: wireApproval({ commitSha: 'b'.repeat(40) }) })],
  ])('treats %s as an unknown outcome', (_name, response) => {
    expect(toRecordedPublicationApproval(response, SCOPE, SUBMITTED)).toBeNull()
  })

  it('is null for a missing answer', () => {
    expect(toRecordedPublicationApproval(null, SCOPE, SUBMITTED)).toBeNull()
  })
})

describe('PUBLICATION_STATEMENTS', () => {
  it('states the fixed boundary in plain text that never promises execution', () => {
    expect(PUBLICATION_STATEMENTS.Consent).toMatch(/one future normal push/i)
    expect(PUBLICATION_STATEMENTS.Consent).toMatch(/No push occurs now/)
    expect(PUBLICATION_STATEMENTS.Consent).toMatch(/pull request, force-push, merge, deletion, rerun and automatic execution are not authorized/)
    expect(PUBLICATION_STATEMENTS.Available).toMatch(/recorded consent/i)
    expect(PUBLICATION_STATEMENTS.Available).toMatch(/not (a|an) /i)
    for (const text of Object.values(PUBLICATION_STATEMENTS)) {
      expect(text).not.toMatch(/\bpushed to\b|\bwill be pushed\b|ready to push|eligible/i)
    }
  })
})
