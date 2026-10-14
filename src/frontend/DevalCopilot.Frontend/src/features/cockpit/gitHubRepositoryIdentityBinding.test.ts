import { describe, expect, it } from 'vitest'
import type { BindGitHubRepositoryIdentityResponse, GetGitHubRepositoryIdentityBindingResponse } from '../../api/clients'
import { toBindingReading, toBoundIdentity } from './gitHubRepositoryIdentityBinding'
import type { RepositoryObservationScope } from './gitHubRepositoryObservation'

const SHA = 'a'.repeat(40)
const TARGET_AT = new Date('2026-10-13T12:00:00.000Z')
const OBSERVED = new Date('2026-10-13T12:01:00.000Z')
const BOUND = new Date('2026-10-13T12:02:00.000Z')

const SCOPE: RepositoryObservationScope = {
  source: { runId: 'run-1', operationId: 'op-1', commitSha: SHA, branchName: 'devalcopilot/workspace/x/1' },
  target: { id: 'target-1', eventId: 'event-1', host: 'github.com', owner: 'octo-cat', repository: 'hello.world', recordedAtUtc: TARGET_AT },
}

const wireSource = () => ({ operationId: 'op-1', commitSha: SHA, branchName: 'devalcopilot/workspace/x/1' })
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
const get = (overrides: Record<string, unknown>) =>
  ({ runId: 'run-1', state: 'Available', source: wireSource(), target: wireTarget(), binding: wireBinding(), ...overrides }) as unknown as GetGitHubRepositoryIdentityBindingResponse
const post = (overrides: Record<string, unknown> = {}) =>
  ({ runId: 'run-1', source: wireSource(), target: wireTarget(), binding: wireBinding(), ...overrides }) as unknown as BindGitHubRepositoryIdentityResponse

describe('toBindingReading', () => {
  it('admits an Available answer whole, for exactly this scope', () => {
    expect(toBindingReading(get({}), SCOPE)).toEqual({
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
    })
  })

  it('admits names that differ from the target only in ASCII case and the longest node identifier', () => {
    expect(toBindingReading(get({ binding: wireBinding({ owner: 'OCTO-CAT', repository: 'Hello.World' }) }), SCOPE)?.kind).toBe('Available')
    expect(toBindingReading(get({ binding: wireBinding({ repositoryNodeId: 'a+/='.repeat(64) }) }), SCOPE)?.kind).toBe('Available')
  })

  it('admits NotRecorded with the exact pair as bindable, with neither as not bindable, and Unavailable with nothing', () => {
    expect(toBindingReading(get({ state: 'NotRecorded', binding: null }), SCOPE)).toEqual({ kind: 'NotRecorded', bindable: true })
    expect(toBindingReading(get({ state: 'NotRecorded', source: null, target: null, binding: null }), SCOPE)).toEqual({
      kind: 'NotRecorded',
      bindable: false,
    })
    expect(toBindingReading(get({ state: 'Unavailable', source: null, target: null, binding: null }), SCOPE)).toEqual({ kind: 'Unavailable' })
  })

  it.each([
    ['null response', null],
    ['another run', get({ runId: 'run-2' })],
    ['an unknown state', get({ state: 'Verified' })],
    ['a lowercase state', get({ state: 'available' })],
    ['Available without a binding', get({ binding: null })],
    ['Available without a source', get({ source: null })],
    ['Available without a target', get({ target: null })],
    ['Available with another operation', get({ source: { ...wireSource(), operationId: 'op-2' } })],
    ['Available with another commit', get({ source: { ...wireSource(), commitSha: 'b'.repeat(40) } })],
    ['Available with another branch', get({ source: { ...wireSource(), branchName: 'elsewhere' } })],
    ['Available with another target', get({ target: { ...wireTarget(), id: 'target-2' } })],
    ['Available with another target owner', get({ target: { ...wireTarget(), owner: 'someone-else' } })],
    ['Available with another target time', get({ target: { ...wireTarget(), recordedAtUtc: BOUND } })],
    ['a binding with another owner', get({ binding: wireBinding({ owner: 'someone-else' }) })],
    ['a binding with another repository', get({ binding: wireBinding({ repository: 'renamed.repo' }) })],
    ['a binding with a trimmed owner', get({ binding: wireBinding({ owner: ' octo-cat' }) })],
    ['a binding with an empty node id', get({ binding: wireBinding({ repositoryNodeId: '' }) })],
    ['a binding with a spaced node id', get({ binding: wireBinding({ repositoryNodeId: 'has space' }) })],
    ['a binding with an escaped node id', get({ binding: wireBinding({ repositoryNodeId: 'R_\\u0067' }) })],
    ['a binding with a 257 character node id', get({ binding: wireBinding({ repositoryNodeId: 'a'.repeat(257) }) })],
    ['a binding observed before the target was recorded', get({ binding: wireBinding({ observedAtUtc: new Date(TARGET_AT.getTime() - 1) }) })],
    ['a binding bound before it was observed', get({ binding: wireBinding({ recordedAtUtc: new Date(OBSERVED.getTime() - 1) }) })],
    ['a binding with an invalid instant', get({ binding: wireBinding({ observedAtUtc: new Date('x') }) })],
    ['a binding with a string instant', get({ binding: wireBinding({ observedAtUtc: '2026-10-13T12:01:00Z' }) })],
    ['a binding without an id', get({ binding: wireBinding({ id: '' }) })],
    ['NotRecorded with a binding', get({ state: 'NotRecorded' })],
    ['NotRecorded with only a source', get({ state: 'NotRecorded', target: null, binding: null })],
    ['NotRecorded with another target', get({ state: 'NotRecorded', target: { ...wireTarget(), id: 'target-2' }, binding: null })],
    ['Unavailable with a source', get({ state: 'Unavailable', target: null, binding: null })],
    ['Unavailable with a binding', get({ state: 'Unavailable', source: null, target: null })],
  ])('refuses the whole answer for %s', (_name, response) => {
    expect(toBindingReading(response as GetGitHubRepositoryIdentityBindingResponse | null, SCOPE)).toBeNull()
  })
})

describe('toBoundIdentity', () => {
  it('accepts exactly the answer to what was submitted', () => {
    expect(toBoundIdentity(post(), SCOPE, 'R_kgDOExample01')).toMatchObject({ id: 'binding-1', repositoryNodeId: 'R_kgDOExample01' })
  })

  it.each([
    ['another node id', post({ binding: wireBinding({ repositoryNodeId: 'R_other' }) })],
    ['another run', post({ runId: 'run-2' })],
    ['another source', post({ source: { ...wireSource(), commitSha: 'b'.repeat(40) } })],
    ['another target', post({ target: { ...wireTarget(), id: 'target-2' } })],
    ['no binding', post({ binding: null })],
    ['a malformed binding', post({ binding: wireBinding({ repositoryNodeId: 'has space' }) })],
    ['a foreign owner', post({ binding: wireBinding({ owner: 'someone-else' }) })],
  ])('treats %s as an unknown outcome', (_name, response) => {
    expect(toBoundIdentity(response, SCOPE, 'R_kgDOExample01')).toBeNull()
  })

  it('is null for a missing answer', () => {
    expect(toBoundIdentity(null, SCOPE, 'R_kgDOExample01')).toBeNull()
  })
})
