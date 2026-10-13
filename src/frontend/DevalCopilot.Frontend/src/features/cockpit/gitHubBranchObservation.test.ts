import { describe, expect, it } from 'vitest'
import type { GetGitHubBranchObservationResponse } from '../../api/clients'
import { branchObservationScopeKey, toBranchObservation } from './gitHubBranchObservation'
import type { BranchObservationScope } from './gitHubBranchObservation'
import type { ObservationScope, ObservationSource } from './gitHubObservationTarget'

const RUN = '11111111-1111-1111-1111-111111111111'
const OPERATION = '22222222-2222-2222-2222-222222222222'
const SHA = 'a'.repeat(40)
const OTHER_SHA = 'b'.repeat(40)
const BRANCH = 'devalcopilot/workspace/abc/1'
const RECORDED = new Date('2026-10-12T20:00:00.000Z')
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
const scope: BranchObservationScope = { source, target }

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
const wireRef = (overrides: Record<string, unknown> = {}) => ({ branchName: BRANCH, commitSha: SHA, ...overrides })
const wireObservation = (overrides: Record<string, unknown> = {}) => ({
  repositoryNodeId: 'R_kgDOExample01',
  owner: 'octo-cat',
  repository: 'hello.world',
  observedAtUtc: OBSERVED,
  ref: wireRef(),
  ...overrides,
})
const response = (overrides: Record<string, unknown>) => ({ runId: RUN, ...overrides }) as unknown as GetGitHubBranchObservationResponse
const withScope = (status: string, observation: unknown) => response({ status, source: wireSource(), target: wireTarget(), observation })
const matched = (overrides: Record<string, unknown> = {}) => withScope('CommitMatched', wireObservation(overrides))
const different = (ref: unknown = wireRef({ commitSha: OTHER_SHA })) => withScope('CommitDifferent', wireObservation({ ref }))
const notReturned = (overrides: Record<string, unknown> = {}) => withScope('RefNotReturned', wireObservation({ ref: null, ...overrides }))

const FACTS = { repositoryNodeId: 'R_kgDOExample01', owner: 'octo-cat', repository: 'hello.world', observedAtUtc: OBSERVED }

describe('toBranchObservation', () => {
  it('accepts NotRecorded and Unavailable only without any scope or observation', () => {
    for (const status of ['NotRecorded', 'Unavailable'] as const) {
      expect(toBranchObservation(response({ status }), scope)).toEqual({ status })
      expect(toBranchObservation(response({ status, source: null, target: null, observation: null }), scope)).toEqual({ status })
      expect(toBranchObservation(response({ status, source: wireSource() }), scope)).toBeNull()
      expect(toBranchObservation(response({ status, target: wireTarget() }), scope)).toBeNull()
      expect(toBranchObservation(response({ status, observation: wireObservation() }), scope)).toBeNull()
    }
  })

  it('accepts CliUnavailable and Unknown only with exactly this source and target and no observation', () => {
    for (const status of ['CliUnavailable', 'Unknown'] as const) {
      expect(toBranchObservation(response({ status, source: wireSource(), target: wireTarget() }), scope)).toEqual({ status })
      expect(toBranchObservation(response({ status }), scope)).toBeNull()
      expect(toBranchObservation(response({ status, source: wireSource() }), scope)).toBeNull()
      expect(toBranchObservation(response({ status, target: wireTarget() }), scope)).toBeNull()
      expect(toBranchObservation(withScope(status, wireObservation()), scope)).toBeNull()
    }
  })

  it('accepts CommitMatched with the ref exactly as returned and an equal commit', () => {
    expect(toBranchObservation(matched(), scope)).toEqual({
      status: 'CommitMatched',
      facts: FACTS,
      ref: { branchName: BRANCH, commitSha: SHA },
    })
  })

  it('accepts CommitDifferent with the ref exactly as returned and a different commit', () => {
    expect(toBranchObservation(different(), scope)).toEqual({
      status: 'CommitDifferent',
      facts: FACTS,
      ref: { branchName: BRANCH, commitSha: OTHER_SHA },
    })
  })

  it('accepts RefNotReturned only with an observation whose ref is explicitly absent', () => {
    expect(toBranchObservation(notReturned(), scope)).toEqual({ status: 'RefNotReturned', facts: FACTS })
    expect(toBranchObservation(notReturned({ ref: undefined }), scope)).toEqual({ status: 'RefNotReturned', facts: FACTS })
    expect(toBranchObservation(withScope('RefNotReturned', wireObservation()), scope)).toBeNull()
    expect(toBranchObservation(withScope('RefNotReturned', wireObservation({ ref: wireRef({ commitSha: OTHER_SHA }) })), scope)).toBeNull()
    expect(toBranchObservation(withScope('RefNotReturned', null), scope)).toBeNull()
    expect(toBranchObservation(withScope('RefNotReturned', undefined), scope)).toBeNull()
  })

  it('refuses a ref status that contradicts the commit comparison', () => {
    expect(toBranchObservation(withScope('CommitMatched', wireObservation({ ref: wireRef({ commitSha: OTHER_SHA }) })), scope)).toBeNull()
    expect(toBranchObservation(withScope('CommitDifferent', wireObservation({ ref: wireRef({ commitSha: SHA }) })), scope)).toBeNull()
  })

  it('refuses CommitMatched and CommitDifferent without a ref', () => {
    for (const status of ['CommitMatched', 'CommitDifferent']) {
      expect(toBranchObservation(withScope(status, wireObservation({ ref: null })), scope)).toBeNull()
      expect(toBranchObservation(withScope(status, wireObservation({ ref: undefined })), scope)).toBeNull()
      expect(toBranchObservation(withScope(status, null), scope)).toBeNull()
      expect(toBranchObservation(withScope(status, 'R_x'), scope)).toBeNull()
    }
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
      expect(toBranchObservation(withScope('CommitMatched', wireObservation({ ref })), scope)).toBeNull()
      expect(toBranchObservation(withScope('CommitDifferent', wireObservation({ ref })), scope)).toBeNull()
    }
  })

  it('accepts names that differ from the recorded ones only in ASCII case and reports them as returned', () => {
    expect(toBranchObservation(matched({ owner: 'OCTO-cat', repository: 'Hello.World' }), scope)).toEqual({
      status: 'CommitMatched',
      facts: { ...FACTS, owner: 'OCTO-cat', repository: 'Hello.World' },
      ref: { branchName: BRANCH, commitSha: SHA },
    })
  })

  it('refuses a foreign, renamed, suffixed or adopted name in every ref-bearing status', () => {
    for (const overrides of [
      { owner: 'someone-else' },
      { repository: 'renamed.repo' },
      { repository: 'hello.world.git' },
      { owner: 'octo-cat2' },
      { owner: 'octo-cat ' },
      { repository: ' hello.world' },
    ]) {
      expect(toBranchObservation(matched(overrides), scope)).toBeNull()
      expect(toBranchObservation(notReturned(overrides), scope)).toBeNull()
    }
  })

  it('refuses an observation with an unusable repository identity', () => {
    for (const overrides of [
      { repositoryNodeId: '' },
      { repositoryNodeId: 'has space' },
      { repositoryNodeId: 'R.x' },
      { repositoryNodeId: 'R_x\n' },
      { repositoryNodeId: 'R_\u00e9' },
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
      expect(toBranchObservation(matched(overrides), scope)).toBeNull()
      expect(toBranchObservation(notReturned(overrides), scope)).toBeNull()
    }
  })

  it('admits an id at the length bound and the conservative character set', () => {
    for (const repositoryNodeId of ['R', 'a'.repeat(256), 'abc-DEF_012/+=']) {
      expect(toBranchObservation(matched({ repositoryNodeId }), scope)).not.toBeNull()
    }
  })

  it('refuses another run, an unknown or missing status and a non-object answer', () => {
    expect(toBranchObservation(matched({}), scope)).not.toBeNull()
    expect(toBranchObservation(response({ ...matched(), runId: '99999999-9999-9999-9999-999999999999' }), scope)).toBeNull()
    expect(toBranchObservation(response({ ...matched(), runId: undefined }), scope)).toBeNull()
    for (const status of ['commitmatched', 'Observed', 'Matched', 'Pending', undefined, 3]) {
      expect(toBranchObservation(response({ ...matched(), status }), scope)).toBeNull()
    }

    expect(toBranchObservation(null, scope)).toBeNull()
    expect(toBranchObservation(undefined, scope)).toBeNull()
    expect(toBranchObservation('CommitMatched' as unknown as GetGitHubBranchObservationResponse, scope)).toBeNull()
  })

  it('refuses a source or target that is not exactly the observed one', () => {
    const base = { status: 'CommitMatched', source: wireSource(), target: wireTarget(), observation: wireObservation() }
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
      expect(toBranchObservation(response({ ...base, ...overrides }), scope)).toBeNull()
    }
  })

  it('matches identifiers ignoring case and the recorded instant by value', () => {
    expect(
      toBranchObservation(
        response({
          status: 'CommitMatched',
          source: wireSource(),
          target: wireTarget({ id: target.id.toUpperCase(), recordedAtUtc: new Date(RECORDED.getTime()) }),
          observation: wireObservation(),
        }),
        scope,
      ),
    ).not.toBeNull()
  })
})

describe('branchObservationScopeKey', () => {
  it('changes with every identifying fact of the source and the target', () => {
    const base = branchObservationScopeKey(scope)
    expect(branchObservationScopeKey({ source: { ...source }, target: { ...target } })).toBe(base)
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
      expect(branchObservationScopeKey(changed)).not.toBe(base)
    }
  })
})
