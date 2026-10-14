import { describe, expect, it } from 'vitest'
import type { GetGitHubCiObservationResponse } from '../../api/clients'
import { ciObservationScopeKey, toCiObservation, CI_OBSERVATION_STATEMENTS, CI_ROLLUP_STATES } from './gitHubCiObservation'
import type { CiObservationScope } from './gitHubCiObservation'
import type { ObservationScope, ObservationSource } from './gitHubObservationTarget'

const RUN = '11111111-1111-1111-1111-111111111111'
const OPERATION = '22222222-2222-2222-2222-222222222222'
const SHA = 'a'.repeat(40)
const OTHER_SHA = 'b'.repeat(40)
const BRANCH = 'devalcopilot/workspace/abc/1'
const NODE_ID = 'R_kgDOExample01'
const RECORDED = new Date('2026-10-12T20:00:00.000Z')
const BOUND_OBSERVED = new Date('2026-10-12T20:30:00.000Z')
const BOUND_RECORDED = new Date('2026-10-12T20:31:00.000Z')
const OBSERVED = new Date('2026-10-12T21:00:00.000Z')

const source: ObservationSource = { runId: RUN, operationId: OPERATION, commitSha: SHA, branchName: BRANCH }
const target: ObservationScope = {
  id: '33333333-3333-3333-3333-333333333333',
  eventId: '44444444-4444-4444-4444-444444444444',
  host: 'github.com',
  owner: 'octo-cat',
  repository: 'hello.world',
  recordedAtUtc: RECORDED,
}
const scope: CiObservationScope = { source, target }

const wireSource = (overrides: Record<string, unknown> = {}) => ({ operationId: OPERATION, commitSha: SHA, branchName: BRANCH, ...overrides })
const wireTarget = (overrides: Record<string, unknown> = {}) => ({
  id: target.id,
  eventId: target.eventId,
  host: 'github.com',
  owner: 'octo-cat',
  repository: 'hello.world',
  recordedAtUtc: RECORDED,
  ...overrides,
})
const wireBinding = (overrides: Record<string, unknown> = {}) => ({
  id: '55555555-5555-5555-5555-555555555555',
  eventId: '66666666-6666-6666-6666-666666666666',
  repositoryNodeId: NODE_ID,
  owner: 'octo-cat',
  repository: 'hello.world',
  observedAtUtc: BOUND_OBSERVED,
  recordedAtUtc: BOUND_RECORDED,
  ...overrides,
})
const wireRef = (overrides: Record<string, unknown> = {}) => ({ branchName: BRANCH, commitSha: SHA, ...overrides })
const wireObservation = (overrides: Record<string, unknown> = {}) => ({
  repositoryNodeId: NODE_ID,
  owner: 'octo-cat',
  repository: 'hello.world',
  observedAtUtc: OBSERVED,
  ref: wireRef(),
  rollupState: null,
  ...overrides,
})
const response = (overrides: Record<string, unknown>) => ({ runId: RUN, ...overrides }) as unknown as GetGitHubCiObservationResponse
const full = (status: string, observation: unknown, binding: unknown = wireBinding()) =>
  response({ status, source: wireSource(), target: wireTarget(), binding, observation })
const reported = (state: unknown = 'SUCCESS', overrides: Record<string, unknown> = {}) =>
  full('StatusReported', wireObservation({ rollupState: state, ...overrides }))
const noStatus = (overrides: Record<string, unknown> = {}) => full('NoStatusReported', wireObservation(overrides))
const different = (rollupState: unknown = null, ref: unknown = wireRef({ commitSha: OTHER_SHA })) =>
  full('CommitDifferent', wireObservation({ ref, rollupState }))
const notReturned = (overrides: Record<string, unknown> = {}) => full('RefNotReturned', wireObservation({ ref: null, ...overrides }))

const FACTS = { repositoryNodeId: NODE_ID, owner: 'octo-cat', repository: 'hello.world', observedAtUtc: OBSERVED }
const BINDING = {
  id: '55555555-5555-5555-5555-555555555555',
  eventId: '66666666-6666-6666-6666-666666666666',
  repositoryNodeId: NODE_ID,
  owner: 'octo-cat',
  repository: 'hello.world',
  observedAtUtc: BOUND_OBSERVED,
  recordedAtUtc: BOUND_RECORDED,
}

describe('toCiObservation', () => {
  it('accepts NotRecorded and Unavailable only without any fact', () => {
    for (const status of ['NotRecorded', 'Unavailable'] as const) {
      expect(toCiObservation(response({ status }), scope)).toEqual({ status })
      expect(toCiObservation(response({ status, source: null, target: null, binding: null, observation: null }), scope)).toEqual({ status })
      expect(toCiObservation(response({ status, source: wireSource() }), scope)).toBeNull()
      expect(toCiObservation(response({ status, target: wireTarget() }), scope)).toBeNull()
      expect(toCiObservation(response({ status, binding: wireBinding() }), scope)).toBeNull()
      expect(toCiObservation(response({ status, observation: wireObservation() }), scope)).toBeNull()
    }
  })

  it('accepts BindingNotRecorded only with exactly this source and target and no binding or observation', () => {
    const base = { status: 'BindingNotRecorded', source: wireSource(), target: wireTarget() }
    expect(toCiObservation(response(base), scope)).toEqual({ status: 'BindingNotRecorded' })
    expect(toCiObservation(response({ ...base, binding: null, observation: null }), scope)).toEqual({ status: 'BindingNotRecorded' })
    expect(toCiObservation(response({ ...base, binding: wireBinding() }), scope)).toBeNull()
    expect(toCiObservation(response({ ...base, observation: wireObservation() }), scope)).toBeNull()
    expect(toCiObservation(response({ status: 'BindingNotRecorded' }), scope)).toBeNull()
    expect(toCiObservation(response({ ...base, source: undefined }), scope)).toBeNull()
    expect(toCiObservation(response({ ...base, target: undefined }), scope)).toBeNull()
  })

  it('accepts CliUnavailable, Unknown and IdentityChanged only with this source, target and binding and no observation', () => {
    for (const status of ['CliUnavailable', 'Unknown', 'IdentityChanged'] as const) {
      expect(toCiObservation(full(status, null), scope)).toEqual({ status, binding: BINDING })
      expect(toCiObservation(full(status, undefined), scope)).toEqual({ status, binding: BINDING })
      expect(toCiObservation(full(status, null, null), scope)).toBeNull()
      expect(toCiObservation(response({ status, source: wireSource(), target: wireTarget(), observation: null }), scope)).toBeNull()
      expect(toCiObservation(full(status, wireObservation()), scope)).toBeNull()
      expect(toCiObservation(response({ status, source: wireSource(), target: wireTarget() }), scope)).toBeNull()
    }
  })

  it('accepts StatusReported for each of the five literal states exactly as returned', () => {
    for (const state of CI_ROLLUP_STATES) {
      expect(toCiObservation(reported(state), scope)).toEqual({
        status: 'StatusReported',
        binding: BINDING,
        facts: FACTS,
        ref: { branchName: BRANCH, commitSha: SHA },
        rollupState: state,
      })
    }

    expect([...CI_ROLLUP_STATES]).toEqual(['ERROR', 'EXPECTED', 'FAILURE', 'PENDING', 'SUCCESS'])
  })

  it('refuses StatusReported with a missing, null or unsupported state', () => {
    for (const state of [
      null,
      undefined,
      '',
      ' SUCCESS',
      'SUCCESS ',
      'success',
      'Success',
      'PASSED',
      'GREEN',
      'APPROVED',
      'QUEUED',
      'COMPLETED',
      'NEUTRAL',
      7,
      true,
      ['SUCCESS'],
      { state: 'SUCCESS' },
    ]) {
      expect(toCiObservation(full('StatusReported', wireObservation({ rollupState: state })), scope)).toBeNull()
    }
  })

  it('accepts NoStatusReported only for the recorded commit with a null state', () => {
    expect(toCiObservation(noStatus(), scope)).toEqual({
      status: 'NoStatusReported',
      binding: BINDING,
      facts: FACTS,
      ref: { branchName: BRANCH, commitSha: SHA },
    })
    expect(toCiObservation(noStatus({ rollupState: undefined }), scope)).not.toBeNull()
    for (const state of ['SUCCESS', 'FAILURE', '', 'none']) {
      expect(toCiObservation(noStatus({ rollupState: state }), scope)).toBeNull()
    }

    expect(toCiObservation(noStatus({ ref: wireRef({ commitSha: OTHER_SHA }) }), scope)).toBeNull()
    expect(toCiObservation(noStatus({ ref: null }), scope)).toBeNull()
  })

  it('accepts CommitDifferent only for another well-formed commit and never with a state', () => {
    expect(toCiObservation(different(), scope)).toEqual({
      status: 'CommitDifferent',
      binding: BINDING,
      facts: FACTS,
      ref: { branchName: BRANCH, commitSha: OTHER_SHA },
    })
    expect(toCiObservation(different(undefined), scope)).not.toBeNull()
    for (const state of CI_ROLLUP_STATES) {
      expect(toCiObservation(different(state), scope)).toBeNull()
    }

    expect(toCiObservation(different(null, wireRef({ commitSha: SHA })), scope)).toBeNull()
    expect(toCiObservation(different(null, null), scope)).toBeNull()
  })

  it('accepts RefNotReturned only with an explicitly absent ref and no state', () => {
    expect(toCiObservation(notReturned(), scope)).toEqual({ status: 'RefNotReturned', binding: BINDING, facts: FACTS })
    expect(toCiObservation(notReturned({ ref: undefined }), scope)).toEqual({ status: 'RefNotReturned', binding: BINDING, facts: FACTS })
    expect(toCiObservation(notReturned({ rollupState: 'SUCCESS' }), scope)).toBeNull()
    expect(toCiObservation(full('RefNotReturned', wireObservation()), scope)).toBeNull()
    expect(toCiObservation(full('RefNotReturned', null), scope)).toBeNull()
  })

  it('refuses an observation whose repository ID is not exactly the bound one', () => {
    for (const repositoryNodeId of [NODE_ID.toLowerCase(), NODE_ID.toUpperCase(), NODE_ID + '0', 'R_kgDODifferent9', '']) {
      expect(toCiObservation(reported('SUCCESS', { repositoryNodeId }), scope)).toBeNull()
      expect(toCiObservation(notReturned({ repositoryNodeId }), scope)).toBeNull()
    }
  })

  it('refuses an observation older than the binding it was observed against', () => {
    expect(toCiObservation(reported('SUCCESS', { observedAtUtc: BOUND_RECORDED }), scope)).not.toBeNull()
    expect(toCiObservation(reported('SUCCESS', { observedAtUtc: new Date(BOUND_RECORDED.getTime() - 1) }), scope)).toBeNull()
    expect(toCiObservation(notReturned({ observedAtUtc: new Date(BOUND_RECORDED.getTime() - 1) }), scope)).toBeNull()
  })

  it('refuses a ref whose branch is not exactly the recorded one or whose commit is not forty lowercase hexadecimal characters', () => {
    for (const ref of [
      wireRef({ branchName: 'main' }),
      wireRef({ branchName: BRANCH + '0' }),
      wireRef({ branchName: BRANCH.toUpperCase() }),
      wireRef({ branchName: 'refs/heads/' + BRANCH }),
      wireRef({ branchName: BRANCH + ' ' }),
      wireRef({ branchName: undefined }),
      wireRef({ commitSha: 'A'.repeat(40) }),
      wireRef({ commitSha: 'a'.repeat(39) }),
      wireRef({ commitSha: 'a'.repeat(41) }),
      wireRef({ commitSha: 'g'.repeat(40) }),
      wireRef({ commitSha: ' ' + 'a'.repeat(39) }),
      wireRef({ commitSha: SHA + '\n' }),
      wireRef({ commitSha: undefined }),
      wireRef({ commitSha: 7 }),
      'refs/heads/main',
      [],
    ]) {
      expect(toCiObservation(reported('SUCCESS', { ref }), scope)).toBeNull()
      expect(toCiObservation(noStatus({ ref }), scope)).toBeNull()
      expect(toCiObservation(different(null, ref), scope)).toBeNull()
    }
  })

  it('accepts names that differ from the recorded ones only in ASCII case and reports them as returned', () => {
    expect(toCiObservation(reported('PENDING', { owner: 'OCTO-cat', repository: 'Hello.World' }), scope)).toEqual({
      status: 'StatusReported',
      binding: BINDING,
      facts: { ...FACTS, owner: 'OCTO-cat', repository: 'Hello.World' },
      ref: { branchName: BRANCH, commitSha: SHA },
      rollupState: 'PENDING',
    })
  })

  it('refuses a foreign, renamed, suffixed or adopted name in every observation-bearing status', () => {
    for (const overrides of [
      { owner: 'someone-else' },
      { repository: 'renamed.repo' },
      { repository: 'hello.world.git' },
      { owner: 'octo-cat2' },
      { owner: 'octo-cat ' },
      { repository: ' hello.world' },
    ]) {
      expect(toCiObservation(reported('SUCCESS', overrides), scope)).toBeNull()
      expect(toCiObservation(noStatus(overrides), scope)).toBeNull()
      expect(toCiObservation(notReturned(overrides), scope)).toBeNull()
    }
  })

  it('refuses an observation with an unusable identity or time', () => {
    for (const overrides of [
      { repositoryNodeId: 'has space' },
      { repositoryNodeId: 'R.x' },
      { repositoryNodeId: 'R_' + String.fromCharCode(0xe9) },
      { repositoryNodeId: 'a'.repeat(257) },
      { repositoryNodeId: 7 },
      { repositoryNodeId: undefined },
      { owner: undefined },
      { repository: undefined },
      { observedAtUtc: undefined },
      { observedAtUtc: null },
      { observedAtUtc: new Date(Number.NaN) },
      { observedAtUtc: '2026-10-12T21:00:00Z' },
    ]) {
      expect(toCiObservation(reported('SUCCESS', overrides), scope)).toBeNull()
      expect(toCiObservation(notReturned(overrides), scope)).toBeNull()
    }
  })

  it('refuses a binding that is incoherent with the target or itself', () => {
    for (const overrides of [
      { repositoryNodeId: 'has space' },
      { repositoryNodeId: undefined },
      { owner: 'someone-else' },
      { repository: 'renamed.repo' },
      { owner: undefined },
      { id: '' },
      { id: undefined },
      { eventId: ' ' },
      { eventId: undefined },
      { observedAtUtc: new Date(RECORDED.getTime() - 1) },
      { observedAtUtc: undefined },
      { recordedAtUtc: new Date(BOUND_OBSERVED.getTime() - 1) },
      { recordedAtUtc: undefined },
      { recordedAtUtc: new Date(Number.NaN) },
    ]) {
      expect(toCiObservation(full('CliUnavailable', null, wireBinding(overrides)), scope)).toBeNull()
      expect(toCiObservation(full('StatusReported', wireObservation({ rollupState: 'SUCCESS' }), wireBinding(overrides)), scope)).toBeNull()
    }

    expect(toCiObservation(full('CliUnavailable', null, 'x'), scope)).toBeNull()
  })

  it('refuses another run, an unknown or missing status and a non-object answer', () => {
    expect(toCiObservation(reported(), scope)).not.toBeNull()
    expect(toCiObservation(response({ ...reported(), runId: '99999999-9999-9999-9999-999999999999' }), scope)).toBeNull()
    expect(toCiObservation(response({ ...reported(), runId: undefined }), scope)).toBeNull()
    for (const status of ['statusreported', 'Passed', 'Approved', 'Ready', 'Green', 'Observed', 'CommitMatched', 'Pending', undefined, 3]) {
      expect(toCiObservation(response({ ...reported(), status }), scope)).toBeNull()
    }

    expect(toCiObservation(null, scope)).toBeNull()
    expect(toCiObservation(undefined, scope)).toBeNull()
    expect(toCiObservation('StatusReported' as unknown as GetGitHubCiObservationResponse, scope)).toBeNull()
  })

  it('refuses a source or target that is not exactly the observed one', () => {
    const base = { status: 'StatusReported', source: wireSource(), target: wireTarget(), binding: wireBinding(), observation: wireObservation({ rollupState: 'SUCCESS' }) }
    expect(toCiObservation(response(base), scope)).not.toBeNull()
    for (const overrides of [
      { source: wireSource({ operationId: '99999999-9999-9999-9999-999999999999' }) },
      { source: wireSource({ commitSha: OTHER_SHA }) },
      { source: wireSource({ branchName: 'elsewhere' }) },
      { source: undefined },
      { target: wireTarget({ id: '99999999-9999-9999-9999-999999999999' }) },
      { target: wireTarget({ eventId: '99999999-9999-9999-9999-999999999999' }) },
      { target: wireTarget({ owner: 'someone-else' }) },
      { target: wireTarget({ repository: 'renamed.repo' }) },
      { target: wireTarget({ host: 'example.com' }) },
      { target: wireTarget({ recordedAtUtc: new Date('2026-10-12T20:00:01.000Z') }) },
      { target: wireTarget({ recordedAtUtc: undefined }) },
      { target: undefined },
    ]) {
      expect(toCiObservation(response({ ...base, ...overrides }), scope)).toBeNull()
    }
  })

  it('matches identifiers ignoring case and the recorded instant by value', () => {
    expect(
      toCiObservation(
        response({
          status: 'StatusReported',
          source: wireSource(),
          target: wireTarget({ id: target.id.toUpperCase(), recordedAtUtc: new Date(RECORDED.getTime()) }),
          binding: wireBinding(),
          observation: wireObservation({ rollupState: 'FAILURE' }),
        }),
        scope,
      ),
    ).not.toBeNull()
  })
})

describe('ciObservationScopeKey', () => {
  it('changes with every identifying fact of the source and the target', () => {
    const base = ciObservationScopeKey(scope)
    expect(ciObservationScopeKey({ source: { ...source }, target: { ...target } })).toBe(base)
    for (const changed of [
      { source: { ...source, runId: 'other' }, target },
      { source: { ...source, operationId: 'other' }, target },
      { source: { ...source, commitSha: OTHER_SHA }, target },
      { source: { ...source, branchName: 'other' }, target },
      { source, target: { ...target, id: 'other' } },
      { source, target: { ...target, eventId: 'other' } },
      { source, target: { ...target, owner: 'other' } },
      { source, target: { ...target, repository: 'other' } },
      { source, target: { ...target, recordedAtUtc: new Date('2026-10-12T20:00:01.000Z') } },
    ]) {
      expect(ciObservationScopeKey(changed)).not.toBe(base)
    }
  })
})

describe('CI_OBSERVATION_STATEMENTS', () => {
  it('states the fixed disclosure and never calls a state passed, approved, ready or green', () => {
    const all = Object.values(CI_OBSERVATION_STATEMENTS).join('\n')
    expect(CI_OBSERVATION_STATEMENTS.Disclosure).toContain('point-in-time aggregate')
    expect(CI_OBSERVATION_STATEMENTS.Disclosure).toContain('complete check or workflow evidence')
    expect(CI_OBSERVATION_STATEMENTS.Disclosure).toContain('required-check approval')
    expect(CI_OBSERVATION_STATEMENTS.Disclosure).toContain('permission')
    expect(CI_OBSERVATION_STATEMENTS.Disclosure).toContain('publication authority')
    for (const word of ['Passed', 'Approved', 'Ready', 'Green']) {
      expect(all).not.toMatch(new RegExp(`\\b${word}\\b`, 'i'))
    }
  })
})
