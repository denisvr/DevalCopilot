import { describe, expect, it } from 'vitest'
import type { GetGitHubRepositoryPermissionObservationResponse } from '../../api/clients'
import { GetGitHubRepositoryPermissionObservationResponse as GeneratedPermissionResponse } from '../../api/generated/api-client'
import {
  PERMISSION_LITERALS,
  PERMISSION_OBSERVATION_STATEMENTS,
  permissionObservationScopeKey,
  toPermissionObservation,
} from './gitHubRepositoryPermissionObservation'
import type { PermissionObservationScope } from './gitHubRepositoryPermissionObservation'
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
const scope: PermissionObservationScope = { source, target }

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
const wireObservation = (overrides: Record<string, unknown> = {}) => ({
  repositoryNodeId: NODE_ID,
  owner: 'octo-cat',
  repository: 'hello.world',
  observedAtUtc: OBSERVED,
  viewerPermission: 'WRITE',
  ...overrides,
})
const response = (overrides: Record<string, unknown>) =>
  ({ runId: RUN, ...overrides }) as unknown as GetGitHubRepositoryPermissionObservationResponse
const full = (status: string, observation: unknown, binding: unknown = wireBinding()) =>
  response({ status, source: wireSource(), target: wireTarget(), binding, observation })
const reported = (permission: unknown = 'WRITE', overrides: Record<string, unknown> = {}) =>
  full('PermissionReported', wireObservation({ viewerPermission: permission, ...overrides }))
const notReturned = (overrides: Record<string, unknown> = {}) =>
  full('PermissionNotReturned', wireObservation({ viewerPermission: null, ...overrides }))

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

describe('toPermissionObservation', () => {
  it('accepts NotRecorded and Unavailable only without any fact', () => {
    for (const status of ['NotRecorded', 'Unavailable'] as const) {
      expect(toPermissionObservation(response({ status }), scope)).toEqual({ status })
      expect(toPermissionObservation(response({ status, source: null, target: null, binding: null, observation: null }), scope)).toEqual({ status })
      expect(toPermissionObservation(response({ status, source: wireSource() }), scope)).toBeNull()
      expect(toPermissionObservation(response({ status, target: wireTarget() }), scope)).toBeNull()
      expect(toPermissionObservation(response({ status, binding: wireBinding() }), scope)).toBeNull()
      expect(toPermissionObservation(response({ status, observation: wireObservation() }), scope)).toBeNull()
    }
  })

  it('accepts BindingNotRecorded only with exactly this source and target and no binding or observation', () => {
    const base = { status: 'BindingNotRecorded', source: wireSource(), target: wireTarget() }
    expect(toPermissionObservation(response(base), scope)).toEqual({ status: 'BindingNotRecorded' })
    expect(toPermissionObservation(response({ ...base, binding: null, observation: null }), scope)).toEqual({ status: 'BindingNotRecorded' })
    expect(toPermissionObservation(response({ ...base, binding: wireBinding() }), scope)).toBeNull()
    expect(toPermissionObservation(response({ ...base, observation: wireObservation() }), scope)).toBeNull()
    expect(toPermissionObservation(response({ status: 'BindingNotRecorded' }), scope)).toBeNull()
    expect(toPermissionObservation(response({ ...base, source: undefined }), scope)).toBeNull()
    expect(toPermissionObservation(response({ ...base, target: undefined }), scope)).toBeNull()
  })

  it('accepts CliUnavailable, Unknown and IdentityChanged only with this source, target and binding and no observation', () => {
    for (const status of ['CliUnavailable', 'Unknown', 'IdentityChanged'] as const) {
      expect(toPermissionObservation(full(status, null), scope)).toEqual({ status, binding: BINDING })
      expect(toPermissionObservation(full(status, undefined), scope)).toEqual({ status, binding: BINDING })
      expect(toPermissionObservation(full(status, null, null), scope)).toBeNull()
      expect(toPermissionObservation(response({ status, source: wireSource(), target: wireTarget(), observation: null }), scope)).toBeNull()
      expect(toPermissionObservation(full(status, wireObservation()), scope)).toBeNull()
      expect(toPermissionObservation(response({ status, source: wireSource(), target: wireTarget() }), scope)).toBeNull()
    }
  })

  it('accepts PermissionReported for each of the six literals exactly as returned', () => {
    for (const permission of PERMISSION_LITERALS) {
      expect(toPermissionObservation(reported(permission), scope)).toEqual({
        status: 'PermissionReported',
        binding: BINDING,
        facts: FACTS,
        viewerPermission: permission,
      })
    }

    expect([...PERMISSION_LITERALS]).toEqual(['READ', 'TRIAGE', 'TRIAGE_PLUS', 'WRITE', 'MAINTAIN', 'ADMIN'])
  })

  it('refuses PermissionReported with a missing, null or unsupported permission', () => {
    for (const permission of [
      null,
      undefined,
      '',
      ' ',
      ' WRITE',
      'WRITE ',
      'write',
      'Write',
      'admin',
      'triage_plus',
      'TRIAGE PLUS',
      'TRIAGE-PLUS',
      'OWNER',
      'NONE',
      'PUSH',
      'CAN_PUSH',
      'READ\n',
      7,
      true,
      ['WRITE'],
      { permission: 'WRITE' },
    ]) {
      expect(toPermissionObservation(full('PermissionReported', wireObservation({ viewerPermission: permission })), scope)).toBeNull()
    }
  })

  it('accepts PermissionNotReturned only with a literal null permission, never an undefined or omitted one', () => {
    expect(toPermissionObservation(notReturned(), scope)).toEqual({ status: 'PermissionNotReturned', binding: BINDING, facts: FACTS })
    // The case is retained, but undefined (which is also what an omitted member becomes) makes the whole answer incoherent.
    expect(toPermissionObservation(notReturned({ viewerPermission: undefined }), scope)).toBeNull()
    for (const permission of PERMISSION_LITERALS) {
      expect(toPermissionObservation(notReturned({ viewerPermission: permission }), scope)).toBeNull()
    }

    for (const permission of ['', 'none', 'null', 0, false]) {
      expect(toPermissionObservation(notReturned({ viewerPermission: permission }), scope)).toBeNull()
    }

    expect(toPermissionObservation(full('PermissionNotReturned', null), scope)).toBeNull()
  })

  // The wire is JSON passed through the real generated response class, exactly as the generated client builds it: JSON has no undefined,
  // so an omitted member stays undefined after fromJS while an explicit null stays null.
  describe('through the generated response class', () => {
    const generated = (status: string, observation: Record<string, unknown>) =>
      GeneratedPermissionResponse.fromJS(
        JSON.parse(
          JSON.stringify({ runId: RUN, status, source: wireSource(), target: wireTarget(), binding: wireBinding(), observation }),
        ),
      )
    const identity = { repositoryNodeId: NODE_ID, owner: 'octo-cat', repository: 'hello.world', observedAtUtc: OBSERVED }

    it('admits an explicit null permission as PermissionNotReturned', () => {
      const response = generated('PermissionNotReturned', { ...identity, viewerPermission: null })

      expect(response.observation?.viewerPermission).toBeNull()
      expect(toPermissionObservation(response, scope)).toEqual({ status: 'PermissionNotReturned', binding: BINDING, facts: FACTS })
    })

    it('refuses a response that omits the permission member, for either observation-bearing status', () => {
      for (const status of ['PermissionNotReturned', 'PermissionReported']) {
        const response = generated(status, { ...identity })

        expect(response.observation?.viewerPermission).toBeUndefined()
        expect(toPermissionObservation(response, scope)).toBeNull()
      }
    })

    it('refuses an explicit undefined permission, which JSON serialization drops', () => {
      const response = generated('PermissionNotReturned', { ...identity, viewerPermission: undefined })

      expect(response.observation?.viewerPermission).toBeUndefined()
      expect(toPermissionObservation(response, scope)).toBeNull()
    })

    it.each(PERMISSION_LITERALS)('still admits the literal %s as PermissionReported', (permission) => {
      const response = generated('PermissionReported', { ...identity, viewerPermission: permission })

      expect(toPermissionObservation(response, scope)).toEqual({
        status: 'PermissionReported',
        binding: BINDING,
        facts: FACTS,
        viewerPermission: permission,
      })
    })
  })

  it('refuses an observation-bearing status that carries no observation or a status that does not carry one', () => {
    expect(toPermissionObservation(full('PermissionReported', null), scope)).toBeNull()
    expect(toPermissionObservation(full('PermissionReported', undefined), scope)).toBeNull()
    expect(toPermissionObservation(full('PermissionReported', 'WRITE'), scope)).toBeNull()
    expect(toPermissionObservation(full('PermissionReported', []), scope)).toBeNull()
  })

  it('refuses an observation whose repository ID is not exactly the bound one', () => {
    for (const repositoryNodeId of [NODE_ID.toLowerCase(), NODE_ID.toUpperCase(), NODE_ID + '0', 'R_kgDODifferent9', '']) {
      expect(toPermissionObservation(reported('ADMIN', { repositoryNodeId }), scope)).toBeNull()
      expect(toPermissionObservation(notReturned({ repositoryNodeId }), scope)).toBeNull()
    }
  })

  it('refuses an observation older than the binding it was observed against', () => {
    expect(toPermissionObservation(reported('ADMIN', { observedAtUtc: BOUND_RECORDED }), scope)).not.toBeNull()
    expect(toPermissionObservation(reported('ADMIN', { observedAtUtc: new Date(BOUND_RECORDED.getTime() - 1) }), scope)).toBeNull()
    expect(toPermissionObservation(notReturned({ observedAtUtc: new Date(BOUND_RECORDED.getTime() - 1) }), scope)).toBeNull()
  })

  it('accepts names that differ from the recorded ones only in ASCII case and reports them as returned', () => {
    expect(toPermissionObservation(reported('MAINTAIN', { owner: 'OCTO-cat', repository: 'Hello.World' }), scope)).toEqual({
      status: 'PermissionReported',
      binding: BINDING,
      facts: { ...FACTS, owner: 'OCTO-cat', repository: 'Hello.World' },
      viewerPermission: 'MAINTAIN',
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
      expect(toPermissionObservation(reported('ADMIN', overrides), scope)).toBeNull()
      expect(toPermissionObservation(notReturned(overrides), scope)).toBeNull()
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
      expect(toPermissionObservation(reported('ADMIN', overrides), scope)).toBeNull()
      expect(toPermissionObservation(notReturned(overrides), scope)).toBeNull()
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
      expect(toPermissionObservation(full('CliUnavailable', null, wireBinding(overrides)), scope)).toBeNull()
      expect(toPermissionObservation(full('PermissionReported', wireObservation(), wireBinding(overrides)), scope)).toBeNull()
    }

    expect(toPermissionObservation(full('CliUnavailable', null, 'x'), scope)).toBeNull()
  })

  it('refuses another run, an unknown or missing status and a non-object answer', () => {
    expect(toPermissionObservation(reported(), scope)).not.toBeNull()
    expect(toPermissionObservation(response({ ...reported(), runId: '99999999-9999-9999-9999-999999999999' }), scope)).toBeNull()
    expect(toPermissionObservation(response({ ...reported(), runId: undefined }), scope)).toBeNull()
    for (const status of ['permissionreported', 'Allowed', 'Denied', 'CanPush', 'Ready', 'Observed', 'RefNotReturned', 'StatusReported', undefined, 3]) {
      expect(toPermissionObservation(response({ ...reported(), status }), scope)).toBeNull()
    }

    expect(toPermissionObservation(null, scope)).toBeNull()
    expect(toPermissionObservation(undefined, scope)).toBeNull()
    expect(toPermissionObservation('PermissionReported' as unknown as GetGitHubRepositoryPermissionObservationResponse, scope)).toBeNull()
  })

  it('refuses a source or target that is not exactly the observed one', () => {
    const base = { status: 'PermissionReported', source: wireSource(), target: wireTarget(), binding: wireBinding(), observation: wireObservation() }
    expect(toPermissionObservation(response(base), scope)).not.toBeNull()
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
      expect(toPermissionObservation(response({ ...base, ...overrides }), scope)).toBeNull()
    }
  })

  it('matches identifiers ignoring case and the recorded instant by value', () => {
    expect(
      toPermissionObservation(
        response({
          status: 'PermissionReported',
          source: wireSource(),
          target: wireTarget({ id: target.id.toUpperCase(), recordedAtUtc: new Date(RECORDED.getTime()) }),
          binding: wireBinding(),
          observation: wireObservation({ viewerPermission: 'READ' }),
        }),
        scope,
      ),
    ).not.toBeNull()
  })
})

describe('permissionObservationScopeKey', () => {
  it('changes with every identifying fact of the source and the target', () => {
    const base = permissionObservationScopeKey(scope)
    expect(permissionObservationScopeKey({ source: { ...source }, target: { ...target } })).toBe(base)
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
      expect(permissionObservationScopeKey(changed)).not.toBe(base)
    }
  })
})

describe('PERMISSION_OBSERVATION_STATEMENTS', () => {
  it('states the fixed disclosure and never calls a role allowed, denied, ready or able to push', () => {
    const all = Object.values(PERMISSION_OBSERVATION_STATEMENTS).join('\n')
    expect(PERMISSION_OBSERVATION_STATEMENTS.Disclosure).toBe('Repository access does not establish permission to push this branch.')
    for (const word of ['allowed', 'denied', 'ready', 'eligible', 'can push', 'granted']) {
      expect(all).not.toMatch(new RegExp(`\\b${word}\\b`, 'i'))
    }
  })
})
