import { describe, expect, it, vi } from 'vitest'
import { describeGlobalAgentClaimBlock } from './deriveGlobalAgentClaimBlock'
import {
  WORKSPACE_EXECUTION_GUIDANCE,
  WORKSPACE_EXECUTION_STATUSES,
  WORKSPACE_EXECUTION_WITHHELD_POINTER,
  deriveWorkspaceExecutionStartGate,
  toWorkspaceExecutionReading,
} from './workspaceExecution'
import type { WorkspaceExecutionReadFailure, WorkspaceExecutionStatus } from './workspaceExecution'
import { OTHER_PROJECT_ID, PROJECT_ID, WORKSPACE_ID, readingOf, stateFor, wireFor } from './workspaceExecutionFixture'
import type { WireBody } from './workspaceExecutionFixture'

const decode = (body: unknown) => toWorkspaceExecutionReading(body, PROJECT_ID)

describe('toWorkspaceExecutionReading: the closed, whole-answer decoder', () => {
  it.each(WORKSPACE_EXECUTION_STATUSES)('accepts the exact %s tuple as the host wrote it', (status) => {
    const body = wireFor(status)

    expect(decode(body)).toEqual(body)
  })

  it('accepts Unavailable with no known workspace, and with the latest workspace', () => {
    expect(decode(wireFor('Unavailable', PROJECT_ID, { workspaceId: null }))).toMatchObject({ status: 'Unavailable', workspaceId: null })
    expect(decode(wireFor('Unavailable'))).toMatchObject({ status: 'Unavailable', workspaceId: WORKSPACE_ID })
  })

  it.each([
    ['nothing', undefined],
    ['null', null],
    ['an array', []],
    ['an array of the answer', [wireFor('Idle')]],
    ['a string', 'Idle'],
    ['a number', 1],
    ['an empty object', {}],
  ])('refuses %s', (_, body) => {
    expect(decode(body)).toBeNull()
  })

  it('refuses an unknown member', () => {
    expect(decode({ ...wireFor('Idle'), extra: 1 })).toBeNull()
    expect(decode({ ...wireFor('Idle'), path: 'C:\\somewhere' })).toBeNull()
  })

  it.each(['projectId', 'workspaceId', 'status', 'reasonCode'])('refuses an answer whose %s key is missing, which is not an explicit null', (key) => {
    for (const status of WORKSPACE_EXECUTION_STATUSES) {
      const { [key]: _removed, ...without } = wireFor(status)
      expect(decode(without), `${status} without ${key}`).toBeNull()
    }
  })

  it("refuses another project's answer, and an answer asked for under another id", () => {
    expect(decode(wireFor('Idle', OTHER_PROJECT_ID))).toBeNull()
    expect(toWorkspaceExecutionReading(wireFor('Idle'), OTHER_PROJECT_ID)).toBeNull()
    expect(toWorkspaceExecutionReading(wireFor('Idle'), 'project-1')).toBeNull()
  })

  it.each([
    ['uppercase', WORKSPACE_ID.toUpperCase()],
    ['braced', `{${WORKSPACE_ID}}`],
    ['padded', ` ${WORKSPACE_ID} `],
    ['trailing space', `${WORKSPACE_ID} `],
    ['without hyphens', WORKSPACE_ID.replaceAll('-', '')],
    ['the nil identity', '00000000-0000-0000-0000-000000000000'],
    ['too short', WORKSPACE_ID.slice(0, -1)],
    ['too long', `${WORKSPACE_ID}0`],
    ['not hexadecimal', 'zzzzzzzz-3333-4333-8333-cccccccccccc'],
    ['an empty string', ''],
    ['a number', 7],
    ['an object', {}],
    ['false', false],
  ])('refuses a workspace identity that is %s', (_, workspaceId) => {
    expect(decode(wireFor('Idle', PROJECT_ID, { workspaceId }))).toBeNull()
    expect(decode(wireFor('Unavailable', PROJECT_ID, { workspaceId }))).toBeNull()
  })

  it.each([
    ['uppercase', PROJECT_ID.toUpperCase()],
    ['braced', `{${PROJECT_ID}}`],
    ['padded', ` ${PROJECT_ID}`],
    ['without hyphens', PROJECT_ID.replaceAll('-', '')],
    ['null', null],
    ['a number', 7],
  ])('refuses a project identity that is %s', (_, projectId) => {
    expect(decode(wireFor('Idle', PROJECT_ID, { projectId }))).toBeNull()
  })

  it.each([
    ['an unknown status', { status: 'Busy' }],
    ['a lowercase status', { status: 'idle' }],
    ['a padded status', { status: 'Idle ' }],
    ['a null status', { status: null }],
    ['a numeric status', { status: 0 }],
    ['an object status', { status: { name: 'Idle' } }],
  ])('refuses %s', (_, overrides) => {
    expect(decode(wireFor('Idle', PROJECT_ID, overrides))).toBeNull()
  })

  it.each<[string, WorkspaceExecutionStatus, WireBody]>([
    ['Idle with a reason', 'Idle', { reasonCode: 'agent_attempt_active' }],
    ['Idle with an empty reason', 'Idle', { reasonCode: '' }],
    ['Idle with no workspace', 'Idle', { workspaceId: null }],
    ['NoWorkspace with a workspace', 'NoWorkspace', { workspaceId: WORKSPACE_ID }],
    ['NoWorkspace with no reason', 'NoWorkspace', { reasonCode: null }],
    ['NoWorkspace with another reason', 'NoWorkspace', { reasonCode: 'workspace_execution_unavailable' }],
    ['AgentAttemptActive with no workspace', 'AgentAttemptActive', { workspaceId: null }],
    ['AgentAttemptActive with no reason', 'AgentAttemptActive', { reasonCode: null }],
    ['AgentAttemptActive with the verification reason', 'AgentAttemptActive', { reasonCode: 'verification_active' }],
    ['VerificationActive with no workspace', 'VerificationActive', { workspaceId: null }],
    ['VerificationActive with the agent reason', 'VerificationActive', { reasonCode: 'agent_attempt_active' }],
    ['CommitReserved with no workspace', 'CommitReserved', { workspaceId: null }],
    ['CommitReserved with the conflict reason', 'CommitReserved', { reasonCode: 'conflicting_execution_owners' }],
    ['Conflicted with no workspace', 'Conflicted', { workspaceId: null }],
    ['Conflicted with the commit reason', 'Conflicted', { reasonCode: 'local_commit_reserved' }],
    ['Unavailable with no reason', 'Unavailable', { reasonCode: null }],
    ['Unavailable with another reason', 'Unavailable', { reasonCode: 'workspace_not_prepared' }],
    ['a reason in another case', 'AgentAttemptActive', { reasonCode: 'Agent_Attempt_Active' }],
  ])('refuses a wrong tuple: %s', (_, status, overrides) => {
    expect(decode(wireFor(status, PROJECT_ID, overrides))).toBeNull()
  })

  it('never repairs, trims or partially accepts: the answer returned carries exactly the four members', () => {
    const reading = decode(wireFor('AgentAttemptActive'))

    expect(Object.keys(reading ?? {}).sort()).toEqual(['projectId', 'reasonCode', 'status', 'workspaceId'])
  })
})

describe('deriveWorkspaceExecutionStartGate: only a validated current Idle for the selected project allows', () => {
  it('allows only a validated Idle of exactly the selected project, with no guidance', () => {
    expect(deriveWorkspaceExecutionStartGate(stateFor('Idle'), PROJECT_ID)).toEqual({ allowsExecutionStart: true, guidance: null })
  })

  it.each(WORKSPACE_EXECUTION_STATUSES.filter((status) => status !== 'Idle'))('withholds %s with its own fixed guidance', (status) => {
    const gate = deriveWorkspaceExecutionStartGate(stateFor(status), PROJECT_ID)

    expect(gate.allowsExecutionStart).toBe(false)
    expect(gate.guidance).toBe(WORKSPACE_EXECUTION_GUIDANCE[status])
  })

  it('words an active Agent attempt as an inspect-and-wait statement that cancels and releases nothing', () => {
    expect(deriveWorkspaceExecutionStartGate(stateFor('AgentAttemptActive'), PROJECT_ID).guidance).toContain(
      'An Agent attempt is active in this workspace. Inspect it and wait; nothing is cancelled or released here.',
    )
  })

  it('withholds the first read while it is pending, with its own guidance', () => {
    const gate = deriveWorkspaceExecutionStartGate(stateFor(null, { loading: true }), PROJECT_ID)

    expect(gate).toEqual({ allowsExecutionStart: false, guidance: WORKSPACE_EXECUTION_GUIDANCE.pending })
  })

  it.each<WorkspaceExecutionReadFailure>(['read', 'timeout', 'not-found', 'invalid'])('withholds a %s failure, even beside an Idle reading', (failure) => {
    const state = stateFor('Idle', { failure })

    expect(deriveWorkspaceExecutionStartGate(state, PROJECT_ID)).toEqual({ allowsExecutionStart: false, guidance: WORKSPACE_EXECUTION_GUIDANCE[failure] })
    expect(deriveWorkspaceExecutionStartGate(stateFor(null, { failure }), PROJECT_ID).allowsExecutionStart).toBe(false)
  })

  it("withholds another project's Idle reading as replaced", () => {
    const gate = deriveWorkspaceExecutionStartGate(stateFor('Idle', {}, OTHER_PROJECT_ID), PROJECT_ID)

    expect(gate).toEqual({ allowsExecutionStart: false, guidance: WORKSPACE_EXECUTION_GUIDANCE.replaced })
  })

  it('withholds when there is no state at all or no selected project', () => {
    expect(deriveWorkspaceExecutionStartGate(null, PROJECT_ID).allowsExecutionStart).toBe(false)
    expect(deriveWorkspaceExecutionStartGate(stateFor('Idle'), null).allowsExecutionStart).toBe(false)
  })

  it('has one distinct fixed sentence per status and failure, free of any identifier or path', () => {
    const texts = Object.values(WORKSPACE_EXECUTION_GUIDANCE)

    expect(new Set(texts).size).toBe(texts.length)
    for (const text of texts) {
      expect(text).not.toMatch(/[0-9a-f]{8}-[0-9a-f]{4}/)
      expect(text).not.toMatch(/[A-Za-z]:\\|\/Users\//)
    }
  })

  it('never calls the state refresh itself: it is a pure derivation', () => {
    const refresh = vi.fn()

    deriveWorkspaceExecutionStartGate(stateFor('Conflicted', { refresh }), PROJECT_ID)

    expect(refresh).not.toHaveBeenCalled()
    expect(readingOf('Idle').status).toBe('Idle')
  })
})

describe('the workspace execution veto as a global claim block', () => {
  it('is worded by the one short pointer, never by the per-status guidance, so a control does not repeat the region guidance', () => {
    expect(describeGlobalAgentClaimBlock({ reason: 'WorkspaceExecutionWithheld' })).toBe(WORKSPACE_EXECUTION_WITHHELD_POINTER)
    expect(Object.values(WORKSPACE_EXECUTION_GUIDANCE)).not.toContain(WORKSPACE_EXECUTION_WITHHELD_POINTER)
  })
})
