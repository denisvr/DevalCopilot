import { describe, expect, it } from 'vitest'
import type { GetGitHubRepositoryObservationResponse } from '../../api/clients'
import type { ObservationScope, ObservationSource } from './gitHubObservationTarget'
import { repositoryObservationScopeKey, toRepositoryObservation } from './gitHubRepositoryObservation'
import type { RepositoryObservationScope } from './gitHubRepositoryObservation'

const RUN = '11111111-1111-1111-1111-111111111111'
const OPERATION = '22222222-2222-2222-2222-222222222222'
const SHA = 'a'.repeat(40)
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
const scope: RepositoryObservationScope = { source, target }

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
const wireObservation = (overrides: Record<string, unknown> = {}) => ({
  repositoryNodeId: 'R_kgDOExample01',
  owner: 'octo-cat',
  repository: 'hello.world',
  observedAtUtc: OBSERVED,
  ...overrides,
})
const response = (overrides: Record<string, unknown>) => ({ runId: RUN, ...overrides }) as unknown as GetGitHubRepositoryObservationResponse
const observed = (overrides: Record<string, unknown> = {}) =>
  response({ status: 'Observed', source: wireSource(), target: wireTarget(), observation: wireObservation(), ...overrides })

describe('toRepositoryObservation', () => {
  it('accepts NotRecorded and Unavailable only without any scope or observation', () => {
    for (const status of ['NotRecorded', 'Unavailable'] as const) {
      expect(toRepositoryObservation(response({ status }), scope)).toEqual({ status })
      expect(toRepositoryObservation(response({ status, source: null, target: null, observation: null }), scope)).toEqual({ status })
      expect(toRepositoryObservation(response({ status, source: wireSource() }), scope)).toBeNull()
      expect(toRepositoryObservation(response({ status, target: wireTarget() }), scope)).toBeNull()
      expect(toRepositoryObservation(response({ status, observation: wireObservation() }), scope)).toBeNull()
    }
  })

  it('accepts CliUnavailable and Unknown only with exactly this source and target and no observation', () => {
    for (const status of ['CliUnavailable', 'Unknown'] as const) {
      expect(toRepositoryObservation(response({ status, source: wireSource(), target: wireTarget() }), scope)).toEqual({ status })
      expect(toRepositoryObservation(response({ status }), scope)).toBeNull()
      expect(toRepositoryObservation(response({ status, source: wireSource() }), scope)).toBeNull()
      expect(toRepositoryObservation(response({ status, target: wireTarget() }), scope)).toBeNull()
      expect(
        toRepositoryObservation(response({ status, source: wireSource(), target: wireTarget(), observation: wireObservation() }), scope),
      ).toBeNull()
    }
  })

  it('accepts Observed with the identity exactly as returned', () => {
    expect(toRepositoryObservation(observed(), scope)).toEqual({
      status: 'Observed',
      identity: { repositoryNodeId: 'R_kgDOExample01', owner: 'octo-cat', repository: 'hello.world', observedAtUtc: OBSERVED },
    })
  })

  it('accepts names that differ from the recorded ones only in ASCII case and reports them as returned', () => {
    expect(toRepositoryObservation(observed({ observation: wireObservation({ owner: 'OCTO-cat', repository: 'Hello.World' }) }), scope)).toEqual({
      status: 'Observed',
      identity: { repositoryNodeId: 'R_kgDOExample01', owner: 'OCTO-cat', repository: 'Hello.World', observedAtUtc: OBSERVED },
    })
  })

  it('refuses a foreign, renamed, suffixed or adopted name', () => {
    for (const observation of [
      wireObservation({ owner: 'someone-else' }),
      wireObservation({ repository: 'renamed.repo' }),
      wireObservation({ repository: 'hello.world.git' }),
      wireObservation({ owner: 'octo-cat2' }),
      wireObservation({ owner: 'octo-cat ' }),
      wireObservation({ repository: ' hello.world' }),
    ]) {
      expect(toRepositoryObservation(observed({ observation }), scope)).toBeNull()
    }
  })

  it('refuses an Observed answer without an observation or with an unusable one', () => {
    expect(toRepositoryObservation(observed({ observation: undefined }), scope)).toBeNull()
    expect(toRepositoryObservation(observed({ observation: null }), scope)).toBeNull()
    expect(toRepositoryObservation(observed({ observation: 'R_x' }), scope)).toBeNull()
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
      expect(toRepositoryObservation(observed({ observation: wireObservation(overrides) }), scope)).toBeNull()
    }
  })

  it('admits an id at the length bound and the conservative character set', () => {
    for (const repositoryNodeId of ['R', 'a'.repeat(256), 'abc-DEF_012/+=']) {
      expect(toRepositoryObservation(observed({ observation: wireObservation({ repositoryNodeId }) }), scope)).not.toBeNull()
    }
  })

  it('refuses another run, an unknown or missing status and a non-object answer', () => {
    expect(toRepositoryObservation(observed({ runId: '99999999-9999-9999-9999-999999999999' }), scope)).toBeNull()
    expect(toRepositoryObservation(observed({ runId: undefined }), scope)).toBeNull()
    expect(toRepositoryObservation(response({ status: 'observed' }), scope)).toBeNull()
    expect(toRepositoryObservation(response({ status: 'Pending' }), scope)).toBeNull()
    expect(toRepositoryObservation(response({ status: undefined }), scope)).toBeNull()
    expect(toRepositoryObservation(response({ status: 3 }), scope)).toBeNull()
    expect(toRepositoryObservation(null, scope)).toBeNull()
    expect(toRepositoryObservation(undefined, scope)).toBeNull()
    expect(toRepositoryObservation('Observed' as unknown as GetGitHubRepositoryObservationResponse, scope)).toBeNull()
  })

  it('refuses a source or target that is not exactly the observed one', () => {
    for (const overrides of [
      { source: wireSource({ operationId: '99999999-9999-9999-9999-999999999999' }) },
      { source: wireSource({ commitSha: 'b'.repeat(40) }) },
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
      expect(toRepositoryObservation(observed(overrides), scope)).toBeNull()
    }
  })

  it('matches identifiers ignoring case and the recorded instant by value', () => {
    expect(
      toRepositoryObservation(
        observed({ target: wireTarget({ id: target.id.toUpperCase(), recordedAtUtc: new Date(RECORDED.getTime()) }) }),
        scope,
      ),
    ).not.toBeNull()
  })
})

describe('repositoryObservationScopeKey', () => {
  it('changes with every identifying fact of the source and the target', () => {
    const base = repositoryObservationScopeKey(scope)
    expect(repositoryObservationScopeKey({ source: { ...source }, target: { ...target } })).toBe(base)
    for (const changed of [
      { source: { ...source, runId: 'other' }, target },
      { source: { ...source, operationId: 'other' }, target },
      { source: { ...source, commitSha: 'b'.repeat(40) }, target },
      { source: { ...source, branchName: 'other' }, target },
      { source, target: { ...target, id: 'other' } },
      { source, target: { ...target, eventId: 'other' } },
      { source, target: { ...target, owner: 'other' } },
      { source, target: { ...target, repository: 'other' } },
      { source, target: { ...target, recordedAtUtc: new Date('2026-10-12T20:00:01.000Z') } },
    ]) {
      expect(repositoryObservationScopeKey(changed)).not.toBe(base)
    }
  })
})
