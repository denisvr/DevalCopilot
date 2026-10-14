import { describe, expect, it } from 'vitest'
import type { GetGitHubCheckContextsObservationResponse } from '../../api/clients'
import {
  CHECK_CONTEXTS_STATEMENTS,
  MAXIMUM_CHECK_CONTEXTS,
  checkContextsScopeKey,
  toCheckContextsObservation,
} from './gitHubCheckContextsObservation'
import type { CheckContextsScope } from './gitHubCheckContextsObservation'
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

// Every special character is built from its code point, so the source stays ASCII and no tool can rewrite it.
const ch = (codePoint: number) => String.fromCodePoint(codePoint)
const lone = (unit: number) => String.fromCharCode(unit)

const source: ObservationSource = { runId: RUN, operationId: OPERATION, commitSha: SHA, branchName: BRANCH }
const target: ObservationScope = {
  id: '33333333-3333-3333-3333-333333333333',
  eventId: '44444444-4444-4444-4444-444444444444',
  host: 'github.com',
  owner: 'octo-cat',
  repository: 'hello.world',
  recordedAtUtc: RECORDED,
}
const scope: CheckContextsScope = { source, target }

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
const run = (overrides: Record<string, unknown> = {}) => ({
  kind: 'CheckRun',
  id: 'CR_1',
  name: 'build',
  status: 'COMPLETED',
  conclusion: 'SUCCESS',
  checkSuiteId: 'CS_1',
  ...overrides,
})
const statusContext = (overrides: Record<string, unknown> = {}) => ({
  kind: 'StatusContext',
  id: 'SC_1',
  name: 'ci/legacy',
  status: 'SUCCESS',
  conclusion: null,
  checkSuiteId: null,
  ...overrides,
})
const distinct = (count: number) => Array.from({ length: count }, (_, index) => run({ id: `CR_${index}`, name: `job ${index}` }))
const wireObservation = (overrides: Record<string, unknown> = {}) => ({
  repositoryNodeId: NODE_ID,
  owner: 'octo-cat',
  repository: 'hello.world',
  observedAtUtc: OBSERVED,
  ref: wireRef(),
  totalCount: null,
  contexts: null,
  ...overrides,
})
const response = (overrides: Record<string, unknown>) => ({ runId: RUN, ...overrides }) as unknown as GetGitHubCheckContextsObservationResponse
const full = (status: string, observation: unknown, binding: unknown = wireBinding()) =>
  response({ status, source: wireSource(), target: wireTarget(), binding, observation })
const reported = (contexts: unknown[], totalCount: unknown = contexts.length, overrides: Record<string, unknown> = {}) =>
  full('ContextsReported', wireObservation({ totalCount, contexts, ...overrides }))
const limit = (totalCount: unknown = 51, overrides: Record<string, unknown> = {}) =>
  full('LimitExceeded', wireObservation({ totalCount, contexts: null, ...overrides }))
const noContexts = (overrides: Record<string, unknown> = {}) => full('NoContextsReturned', wireObservation(overrides))
const different = (overrides: Record<string, unknown> = {}) =>
  full('CommitDifferent', wireObservation({ ref: wireRef({ commitSha: OTHER_SHA }), ...overrides }))
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
const REF = { branchName: BRANCH, commitSha: SHA }

describe('toCheckContextsObservation', () => {
  it('accepts NotRecorded and Unavailable only without any fact', () => {
    for (const status of ['NotRecorded', 'Unavailable'] as const) {
      expect(toCheckContextsObservation(response({ status }), scope)).toEqual({ status })
      expect(toCheckContextsObservation(response({ status, source: null, target: null, binding: null, observation: null }), scope)).toEqual({ status })
      expect(toCheckContextsObservation(response({ status, source: wireSource() }), scope)).toBeNull()
      expect(toCheckContextsObservation(response({ status, target: wireTarget() }), scope)).toBeNull()
      expect(toCheckContextsObservation(response({ status, binding: wireBinding() }), scope)).toBeNull()
      expect(toCheckContextsObservation(response({ status, observation: wireObservation() }), scope)).toBeNull()
    }
  })

  it('accepts BindingNotRecorded only with exactly this source and target and no binding or observation', () => {
    const base = { status: 'BindingNotRecorded', source: wireSource(), target: wireTarget() }
    expect(toCheckContextsObservation(response(base), scope)).toEqual({ status: 'BindingNotRecorded' })
    expect(toCheckContextsObservation(response({ ...base, binding: null, observation: null }), scope)).toEqual({ status: 'BindingNotRecorded' })
    expect(toCheckContextsObservation(response({ ...base, binding: wireBinding() }), scope)).toBeNull()
    expect(toCheckContextsObservation(response({ ...base, observation: wireObservation() }), scope)).toBeNull()
    expect(toCheckContextsObservation(response({ status: 'BindingNotRecorded' }), scope)).toBeNull()
  })

  it('accepts CliUnavailable, Unknown and IdentityChanged only with this source, target and binding and no observation', () => {
    for (const status of ['CliUnavailable', 'Unknown', 'IdentityChanged'] as const) {
      expect(toCheckContextsObservation(full(status, null), scope)).toEqual({ status, binding: BINDING })
      expect(toCheckContextsObservation(full(status, undefined), scope)).toEqual({ status, binding: BINDING })
      expect(toCheckContextsObservation(full(status, null, null), scope)).toBeNull()
      expect(toCheckContextsObservation(full(status, wireObservation()), scope)).toBeNull()
      expect(toCheckContextsObservation(response({ status, source: wireSource(), target: wireTarget() }), scope)).toBeNull()
    }
  })

  it('accepts RefNotReturned only with a null ref, count and collection', () => {
    expect(toCheckContextsObservation(notReturned(), scope)).toEqual({ status: 'RefNotReturned', binding: BINDING, facts: FACTS })
    expect(toCheckContextsObservation(notReturned({ ref: undefined, totalCount: undefined, contexts: undefined }), scope)).toEqual({
      status: 'RefNotReturned',
      binding: BINDING,
      facts: FACTS,
    })
    expect(toCheckContextsObservation(notReturned({ ref: wireRef() }), scope)).toBeNull()
    expect(toCheckContextsObservation(notReturned({ totalCount: 0 }), scope)).toBeNull()
    expect(toCheckContextsObservation(notReturned({ contexts: [] }), scope)).toBeNull()
    expect(toCheckContextsObservation(notReturned({ totalCount: 1, contexts: [run()] }), scope)).toBeNull()
  })

  it('accepts CommitDifferent only with another valid commit and no count or collection, even if the wire carried them', () => {
    expect(toCheckContextsObservation(different(), scope)).toEqual({
      status: 'CommitDifferent',
      binding: BINDING,
      facts: FACTS,
      ref: { branchName: BRANCH, commitSha: OTHER_SHA },
    })
    expect(toCheckContextsObservation(different({ totalCount: 0 }), scope)).toBeNull()
    expect(toCheckContextsObservation(different({ contexts: [] }), scope)).toBeNull()
    expect(toCheckContextsObservation(different({ totalCount: 1, contexts: [run()] }), scope)).toBeNull()
    expect(toCheckContextsObservation(different({ totalCount: 51 }), scope)).toBeNull()
    expect(toCheckContextsObservation(full('CommitDifferent', wireObservation()), scope)).toBeNull()
    expect(toCheckContextsObservation(different({ ref: wireRef({ commitSha: OTHER_SHA.toUpperCase() }) }), scope)).toBeNull()
    expect(toCheckContextsObservation(different({ ref: wireRef({ commitSha: 'b'.repeat(39) }) }), scope)).toBeNull()
    expect(toCheckContextsObservation(different({ ref: wireRef({ commitSha: OTHER_SHA, branchName: 'main' }) }), scope)).toBeNull()
    expect(toCheckContextsObservation(different({ ref: null }), scope)).toBeNull()
  })

  it('accepts NoContextsReturned only for the recorded commit with a null count and collection', () => {
    expect(toCheckContextsObservation(noContexts(), scope)).toEqual({ status: 'NoContextsReturned', binding: BINDING, facts: FACTS, ref: REF })
    expect(toCheckContextsObservation(noContexts({ totalCount: undefined, contexts: undefined }), scope)).toEqual({
      status: 'NoContextsReturned',
      binding: BINDING,
      facts: FACTS,
      ref: REF,
    })
    expect(toCheckContextsObservation(noContexts({ totalCount: 0 }), scope)).toBeNull()
    expect(toCheckContextsObservation(noContexts({ contexts: [] }), scope)).toBeNull()
    expect(toCheckContextsObservation(noContexts({ ref: wireRef({ commitSha: OTHER_SHA }) }), scope)).toBeNull()
    expect(toCheckContextsObservation(noContexts({ ref: null }), scope)).toBeNull()
  })

  it('accepts LimitExceeded only for the recorded commit with a total above fifty and no entry', () => {
    for (const total of [51, 52, 1000, 2147483647]) {
      expect(toCheckContextsObservation(limit(total), scope)).toEqual({
        status: 'LimitExceeded',
        binding: BINDING,
        facts: FACTS,
        ref: REF,
        totalCount: total,
      })
    }

    expect(toCheckContextsObservation(limit(MAXIMUM_CHECK_CONTEXTS), scope)).toBeNull()
    expect(toCheckContextsObservation(limit(0), scope)).toBeNull()
    expect(toCheckContextsObservation(limit(null), scope)).toBeNull()
    expect(toCheckContextsObservation(limit(51, { contexts: [] }), scope)).toBeNull()
    expect(toCheckContextsObservation(limit(51, { contexts: distinct(50) }), scope)).toBeNull()
    expect(toCheckContextsObservation(limit(51, { ref: wireRef({ commitSha: OTHER_SHA }) }), scope)).toBeNull()
    expect(toCheckContextsObservation(limit(51, { ref: null }), scope)).toBeNull()
    for (const bad of [-1, 51.5, 2147483648, Number.MAX_SAFE_INTEGER, Number.NaN, Number.POSITIVE_INFINITY, '51', true, [], {}]) {
      expect(toCheckContextsObservation(limit(bad), scope)).toBeNull()
    }
  })

  it('accepts ContextsReported for exactly the recorded commit with a count equal to the entries in provider order', () => {
    const entries = [run({ id: 'CR_b', name: 'build' }), statusContext({ id: 'SC_a', name: 'build' }), run({ id: 'CR_a', name: 'build', conclusion: null, status: 'QUEUED' })]
    expect(toCheckContextsObservation(reported(entries), scope)).toEqual({
      status: 'ContextsReported',
      binding: BINDING,
      facts: FACTS,
      ref: REF,
      totalCount: 3,
      contexts: entries,
    })
  })

  it('accepts zero entries as a zero count with an empty array and never treats it as a missing collection', () => {
    expect(toCheckContextsObservation(reported([]), scope)).toMatchObject({ status: 'ContextsReported', totalCount: 0, contexts: [] })
    expect(toCheckContextsObservation(reported([], 0, { contexts: null }), scope)).toBeNull()
    expect(toCheckContextsObservation(reported([], 0, { contexts: undefined }), scope)).toBeNull()
    expect(toCheckContextsObservation(reported([], 0, { totalCount: null }), scope)).toBeNull()
  })

  it('accepts exactly fifty entries and refuses a count or collection that is not the lesser of fifty and the total', () => {
    expect(toCheckContextsObservation(reported(distinct(50)), scope)).toMatchObject({ totalCount: 50, contexts: expect.any(Array) })
    expect(toCheckContextsObservation(reported(distinct(51)), scope)).toBeNull()
    expect(toCheckContextsObservation(reported(distinct(50), 51), scope)).toBeNull()
    expect(toCheckContextsObservation(reported(distinct(3), 4), scope)).toBeNull()
    expect(toCheckContextsObservation(reported(distinct(3), 2), scope)).toBeNull()
    expect(toCheckContextsObservation(reported(distinct(1), 0), scope)).toBeNull()
    expect(toCheckContextsObservation(reported([], 1), scope)).toBeNull()
    expect(toCheckContextsObservation(reported(distinct(1), -1), scope)).toBeNull()
    expect(toCheckContextsObservation(reported(distinct(2), 2.5), scope)).toBeNull()
    expect(toCheckContextsObservation(reported(distinct(2), '2'), scope)).toBeNull()
    expect(toCheckContextsObservation(reported(distinct(2), 2, { contexts: { length: 2 } }), scope)).toBeNull()
    expect(toCheckContextsObservation(reported(distinct(2), 2, { contexts: 'xx' }), scope)).toBeNull()
  })

  it('refuses ContextsReported for a commit that is not the recorded one or without a ref', () => {
    expect(toCheckContextsObservation(reported([run()], 1, { ref: wireRef({ commitSha: OTHER_SHA }) }), scope)).toBeNull()
    expect(toCheckContextsObservation(reported([run()], 1, { ref: null }), scope)).toBeNull()
    expect(toCheckContextsObservation(reported([run()], 1, { ref: wireRef({ branchName: BRANCH + '0' }) }), scope)).toBeNull()
    expect(toCheckContextsObservation(reported([run()], 1, { ref: wireRef({ commitSha: SHA.toUpperCase() }) }), scope)).toBeNull()
  })

  it('preserves repeated names, shared suites and the exact provider order without sorting or merging', () => {
    const entries = [
      run({ id: 'CR_z', name: 'same', checkSuiteId: 'CS_1' }),
      run({ id: 'CR_a', name: 'same', checkSuiteId: 'CS_1', conclusion: 'FAILURE' }),
      statusContext({ id: 'SC_m', name: 'same', status: 'ERROR' }),
    ]

    const reading = toCheckContextsObservation(reported(entries), scope)

    expect(reading).toMatchObject({ status: 'ContextsReported' })
    expect((reading as { contexts: { id: string }[] }).contexts.map((entry) => entry.id)).toEqual(['CR_z', 'CR_a', 'SC_m'])
  })

  it('refuses a duplicate entry identifier ordinally and keeps identifiers that differ only in case', () => {
    expect(toCheckContextsObservation(reported([run({ id: 'CR_1' }), run({ id: 'CR_1', name: 'other' })]), scope)).toBeNull()
    expect(toCheckContextsObservation(reported([run({ id: 'X_1' }), statusContext({ id: 'X_1' })]), scope)).toBeNull()
    expect(toCheckContextsObservation(reported([run({ id: 'CR_a' }), run({ id: 'CR_A' })]), scope)).toMatchObject({ totalCount: 2 })
  })

  it('accepts every literal check run status, conclusion (or null) and status context state independently', () => {
    const statuses = ['COMPLETED', 'IN_PROGRESS', 'PENDING', 'QUEUED', 'REQUESTED', 'WAITING']
    const conclusions = [null, 'ACTION_REQUIRED', 'CANCELLED', 'FAILURE', 'NEUTRAL', 'SKIPPED', 'STALE', 'STARTUP_FAILURE', 'SUCCESS', 'TIMED_OUT']
    const states = ['ERROR', 'EXPECTED', 'FAILURE', 'PENDING', 'SUCCESS']
    for (const status of statuses) {
      for (const conclusion of conclusions) {
        expect(toCheckContextsObservation(reported([run({ status, conclusion })]), scope)).toMatchObject({ status: 'ContextsReported' })
      }
    }

    for (const status of states) {
      expect(toCheckContextsObservation(reported([statusContext({ status })]), scope)).toMatchObject({ status: 'ContextsReported' })
    }
  })

  it('refuses an unsupported or translated status, conclusion or state, and a literal of the other kind', () => {
    for (const status of ['completed', 'SUCCESS', 'STARTUP_FAILURE', ' COMPLETED', 'RUNNING', '', null, 1]) {
      expect(toCheckContextsObservation(reported([run({ status })]), scope)).toBeNull()
    }

    for (const conclusion of ['success', 'COMPLETED', 'PENDING', 'ERROR', 'CANCELED', 'PASSED', 'GREEN', '', 1]) {
      expect(toCheckContextsObservation(reported([run({ conclusion })]), scope)).toBeNull()
    }

    for (const status of ['success', 'QUEUED', 'COMPLETED', 'NEUTRAL', 'PASSED', '', null]) {
      expect(toCheckContextsObservation(reported([statusContext({ status })]), scope)).toBeNull()
    }
  })

  it('refuses an entry that breaks its kind shape', () => {
    const bad = [
      run({ checkSuiteId: null }),
      run({ checkSuiteId: undefined }),
      run({ checkSuiteId: '' }),
      run({ checkSuiteId: 'has space' }),
      run({ conclusion: undefined }),
      statusContext({ conclusion: 'SUCCESS' }),
      statusContext({ checkSuiteId: 'CS_1' }),
      run({ kind: 'Other' }),
      run({ kind: 'checkrun' }),
      run({ kind: 'CheckRun ' }),
      run({ kind: null }),
      run({ id: '' }),
      run({ id: 'has space' }),
      run({ id: 'a'.repeat(257) }),
      run({ id: 'CR_' + ch(0xe9) }),
      run({ id: 5 }),
      run({ name: 5 }),
      null,
      'x',
      5,
      [],
    ]
    for (const entry of bad) {
      expect(toCheckContextsObservation(reported([entry]), scope)).toBeNull()
    }
  })

  it('bounds identifiers at 256 ASCII characters', () => {
    expect(toCheckContextsObservation(reported([run({ id: 'a'.repeat(256), checkSuiteId: 'b'.repeat(256) })]), scope)).toMatchObject({
      status: 'ContextsReported',
    })
    expect(toCheckContextsObservation(reported([run({ checkSuiteId: 'b'.repeat(257) })]), scope)).toBeNull()
  })

  it('admits well-formed labels exactly as returned and refuses blank, control, format, separator, replacement and malformed ones', () => {
    const good = [
      'build',
      ' padded ',
      'quote " and back\\slash',
      '<script>alert(1)</script> [x](https://evil.example) &amp;',
      ch(0x65e5) + ch(0x672c),
      'pair ' + ch(0x1f600),
      'a' + ch(0xa0) + 'b',
      'a'.repeat(256),
      ch(0xe9).repeat(128),
    ]
    for (const name of good) {
      expect(toCheckContextsObservation(reported([run({ name })]), scope)).toMatchObject({ contexts: [{ name }] })
      expect(toCheckContextsObservation(reported([statusContext({ name })]), scope)).toMatchObject({ contexts: [{ name }] })
    }

    const bad = [
      '',
      ' ',
      ch(0xa0),
      ch(0x3000),
      'a\tb',
      'a\nb',
      'a\rb',
      'a' + ch(0) + 'b',
      'a' + ch(0x7f) + 'b',
      'a' + ch(0x85) + 'b',
      'a' + ch(0xad) + 'b',
      'a' + ch(0x200b) + 'b',
      'a' + ch(0x200e) + 'b',
      'a' + ch(0x202e) + 'b',
      'a' + ch(0x2066) + 'b',
      'a' + ch(0xfeff) + 'b',
      'a' + ch(0x2028) + 'b',
      'a' + ch(0x2029) + 'b',
      'a' + ch(0xfffd) + 'b',
      'a' + lone(0xd800) + 'b',
      'a' + lone(0xdc00) + 'b',
      lone(0xdc00) + lone(0xd800),
      'a'.repeat(257),
      ch(0xe9).repeat(129),
      ch(0x65e5).repeat(86),
      ch(0x1f600).repeat(65),
      null,
      undefined,
      5,
    ]
    for (const name of bad) {
      expect(toCheckContextsObservation(reported([run({ name })]), scope)).toBeNull()
      expect(toCheckContextsObservation(reported([statusContext({ name })]), scope)).toBeNull()
    }
  })

  it('refuses the whole answer when one entry among valid siblings is malformed, never omitting it', () => {
    const entries = [run({ id: 'CR_1' }), run({ id: 'CR_2', name: 'bad' + ch(0) }), run({ id: 'CR_3' })]

    expect(toCheckContextsObservation(reported(entries), scope)).toBeNull()
    expect(toCheckContextsObservation(reported([entries[0], entries[2]]), scope)).toMatchObject({ totalCount: 2 })
  })

  it('does not copy members the contract does not name into an entry', () => {
    const reading = toCheckContextsObservation(reported([run({ extra: 'x', url: 'https://evil.example' })]), scope)

    expect(reading).toMatchObject({ status: 'ContextsReported' })
    expect(Object.keys((reading as { contexts: object[] }).contexts[0]).sort()).toEqual([
      'checkSuiteId', 'conclusion', 'id', 'kind', 'name', 'status',
    ])
  })

  it('refuses an observation with another run, source, target or an unknown, old or contradictory status', () => {
    expect(toCheckContextsObservation({ ...reported([run()]), runId: 'someone-else' } as GetGitHubCheckContextsObservationResponse, scope)).toBeNull()
    expect(toCheckContextsObservation(response({ status: 'ContextsReported', source: wireSource({ operationId: RUN }), target: wireTarget(), binding: wireBinding(), observation: wireObservation({ totalCount: 0, contexts: [] }) }), scope)).toBeNull()
    expect(toCheckContextsObservation(response({ status: 'ContextsReported', source: wireSource({ commitSha: OTHER_SHA }), target: wireTarget(), binding: wireBinding(), observation: wireObservation({ totalCount: 0, contexts: [] }) }), scope)).toBeNull()
    expect(toCheckContextsObservation(response({ status: 'ContextsReported', source: wireSource({ branchName: 'main' }), target: wireTarget(), binding: wireBinding(), observation: wireObservation({ totalCount: 0, contexts: [] }) }), scope)).toBeNull()
    expect(toCheckContextsObservation(response({ status: 'ContextsReported', source: wireSource(), target: wireTarget({ owner: 'someone' }), binding: wireBinding(), observation: wireObservation({ totalCount: 0, contexts: [] }) }), scope)).toBeNull()
    for (const status of ['StatusReported', 'NoStatusReported', 'Passed', 'Success', '', 'contextsreported', undefined, null, 5]) {
      expect(toCheckContextsObservation(full(status as string, wireObservation({ totalCount: 0, contexts: [] })), scope)).toBeNull()
    }

    expect(toCheckContextsObservation(null, scope)).toBeNull()
    expect(toCheckContextsObservation(undefined, scope)).toBeNull()
  })

  it('refuses a binding or observation that is not the bound identity or is out of time order', () => {
    const ok = (overrides: Record<string, unknown>) => reported([run()], 1, overrides)
    expect(toCheckContextsObservation(ok({ repositoryNodeId: 'R_other' }), scope)).toBeNull()
    expect(toCheckContextsObservation(ok({ repositoryNodeId: NODE_ID.toLowerCase() }), scope)).toBeNull()
    expect(toCheckContextsObservation(ok({ owner: 'someone' }), scope)).toBeNull()
    expect(toCheckContextsObservation(ok({ repository: 'renamed.repo' }), scope)).toBeNull()
    expect(toCheckContextsObservation(ok({ repository: 'hello.world.git' }), scope)).toBeNull()
    expect(toCheckContextsObservation(ok({ observedAtUtc: new Date(BOUND_RECORDED.getTime() - 1) }), scope)).toBeNull()
    expect(toCheckContextsObservation(ok({ observedAtUtc: new Date('x') }), scope)).toBeNull()
    expect(toCheckContextsObservation(ok({ observedAtUtc: BOUND_RECORDED }), scope)).toMatchObject({ status: 'ContextsReported' })
    expect(toCheckContextsObservation(ok({ owner: 'OCTO-CAT', repository: 'Hello.World' }), scope)).toMatchObject({
      facts: { owner: 'OCTO-CAT', repository: 'Hello.World' },
    })
    expect(toCheckContextsObservation(full('ContextsReported', wireObservation({ totalCount: 1, contexts: [run()] }), wireBinding({ repositoryNodeId: 'has space' })), scope)).toBeNull()
    expect(toCheckContextsObservation(full('ContextsReported', wireObservation({ totalCount: 1, contexts: [run()] }), wireBinding({ owner: 'someone' })), scope)).toBeNull()
    expect(toCheckContextsObservation(full('ContextsReported', wireObservation({ totalCount: 1, contexts: [run()] }), wireBinding({ observedAtUtc: new Date(RECORDED.getTime() - 1) })), scope)).toBeNull()
    expect(toCheckContextsObservation(full('ContextsReported', wireObservation({ totalCount: 1, contexts: [run()] }), wireBinding({ recordedAtUtc: new Date(BOUND_OBSERVED.getTime() - 1) })), scope)).toBeNull()
  })
})

describe('checkContextsScopeKey', () => {
  it('differs for every identifying fact of the source and the target', () => {
    const base = checkContextsScopeKey(scope)
    expect(checkContextsScopeKey({ source, target })).toBe(base)
    expect(checkContextsScopeKey({ source: { ...source, runId: 'x' }, target })).not.toBe(base)
    expect(checkContextsScopeKey({ source: { ...source, operationId: 'x' }, target })).not.toBe(base)
    expect(checkContextsScopeKey({ source: { ...source, commitSha: OTHER_SHA }, target })).not.toBe(base)
    expect(checkContextsScopeKey({ source: { ...source, branchName: 'x' }, target })).not.toBe(base)
    expect(checkContextsScopeKey({ source, target: { ...target, id: 'x' } })).not.toBe(base)
    expect(checkContextsScopeKey({ source, target: { ...target, eventId: 'x' } })).not.toBe(base)
    expect(checkContextsScopeKey({ source, target: { ...target, owner: 'x' } })).not.toBe(base)
    expect(checkContextsScopeKey({ source, target: { ...target, repository: 'x' } })).not.toBe(base)
    expect(checkContextsScopeKey({ source, target: { ...target, recordedAtUtc: new Date(RECORDED.getTime() + 1) } })).not.toBe(base)
  })
})

describe('CHECK_CONTEXTS_STATEMENTS', () => {
  it('has one fixed non-empty statement per status, the zero-entry statement and the disclosure', () => {
    for (const [key, text] of Object.entries(CHECK_CONTEXTS_STATEMENTS)) {
      expect(text.length, key).toBeGreaterThan(20)
    }
  })

  it('never says passed, approved, ready, green, complete or success', () => {
    const all = Object.values(CHECK_CONTEXTS_STATEMENTS).join(' ')
    for (const word of ['passed', 'approved', 'ready', 'green', 'succeeded', 'all checks']) {
      expect(all.toLowerCase()).not.toContain(word)
    }
  })

  it('states the disclosure, the fifty-entry bound and that zero returned entries is not a success', () => {
    expect(CHECK_CONTEXTS_STATEMENTS.Disclosure).toContain('bounded collection')
    expect(CHECK_CONTEXTS_STATEMENTS.Disclosure).toContain('required-check approval')
    expect(CHECK_CONTEXTS_STATEMENTS.Disclosure).toContain('workflow history')
    expect(CHECK_CONTEXTS_STATEMENTS.Disclosure).toContain('permission')
    expect(CHECK_CONTEXTS_STATEMENTS.Disclosure).toContain('publication authority')
    expect(CHECK_CONTEXTS_STATEMENTS.LabelRedaction).toContain('may have redacted')
    expect(CHECK_CONTEXTS_STATEMENTS.LabelRedaction).toContain('not necessarily the provider')
    expect(CHECK_CONTEXTS_STATEMENTS.LimitExceeded).toContain('50')
    expect(CHECK_CONTEXTS_STATEMENTS.ZeroReturned).toContain('zero')
    expect(CHECK_CONTEXTS_STATEMENTS.ZeroReturned).toContain('not')
    expect(CHECK_CONTEXTS_STATEMENTS.NoContextsReturned).toContain('does not mean that no checks exist')
  })
})
