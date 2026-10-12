import { describe, expect, it } from 'vitest'
import type {
  GetGitHubObservationTargetResponse,
  LocalCommitOperationResponse,
  RecordGitHubObservationTargetResponse,
} from '../../api/clients'
import { observationSourceKey, toObservationReading, toObservationSource, toRecordedObservation } from './gitHubObservationTarget'
import type { ObservationSource } from './gitHubObservationTarget'
import { isGitHubOwner, isGitHubRepository } from './gitHubRepositoryNames'

const RUN = '11111111-1111-1111-1111-111111111111'
const OPERATION = '22222222-2222-2222-2222-222222222222'
const SHA = 'a'.repeat(40)
const BRANCH = 'devalcopilot/workspace/abc/1'
const AT = new Date('2026-10-12T20:00:00.000Z')

const source: ObservationSource = { runId: RUN, operationId: OPERATION, commitSha: SHA, branchName: BRANCH }
const wireSource = (overrides: Record<string, unknown> = {}) => ({ operationId: OPERATION, commitSha: SHA, branchName: BRANCH, ...overrides })
const wireTarget = (overrides: Record<string, unknown> = {}) => ({
  id: '33333333-3333-3333-3333-333333333333',
  eventId: '44444444-4444-4444-4444-444444444444',
  host: 'github.com',
  owner: 'octo-cat',
  repository: 'hello.world',
  recordedAtUtc: AT,
  ...overrides,
})
const read = (overrides: Record<string, unknown>) => ({ runId: RUN, ...overrides }) as unknown as GetGitHubObservationTargetResponse
const wrote = (overrides: Record<string, unknown> = {}) =>
  ({ runId: RUN, source: wireSource(), target: wireTarget(), ...overrides }) as unknown as RecordGitHubObservationTargetResponse

describe('the GitHub repository name subset', () => {
  it('admits exactly the conservative ASCII subset and never trims', () => {
    expect(isGitHubOwner('a')).toBe(true)
    expect(isGitHubOwner('a'.repeat(39))).toBe(true)
    expect(isGitHubOwner('a'.repeat(40))).toBe(false)
    for (const bad of ['', '-a', 'a-', 'a_b', 'a.b', ' a', 'a ', 'a\n', 'é', 'Ａ']) {
      expect(isGitHubOwner(bad)).toBe(false)
    }
    expect(isGitHubRepository('r')).toBe(true)
    expect(isGitHubRepository('repo.name_1-x')).toBe(true)
    expect(isGitHubRepository('r'.repeat(100))).toBe(true)
    expect(isGitHubRepository('r'.repeat(101))).toBe(false)
    for (const bad of ['', '.', '..', '.hidden', '-r', '_r', 'r r', 'r/r', 'r\n', 'é']) {
      expect(isGitHubRepository(bad)).toBe(false)
    }
    expect(isGitHubOwner(undefined)).toBe(false)
    expect(isGitHubRepository(7)).toBe(false)
  })
})

describe('toObservationSource', () => {
  const operation = (overrides: Record<string, unknown> = {}) =>
    ({ status: 'Completed', operationId: OPERATION, commitSha: SHA, branchName: BRANCH, ...overrides }) as unknown as LocalCommitOperationResponse

  it('is the exact completed delivery and nothing else', () => {
    expect(toObservationSource(RUN, operation())).toEqual(source)
    expect(toObservationSource(RUN, undefined)).toBeNull()
    expect(toObservationSource(RUN, null)).toBeNull()
    expect(toObservationSource('', operation())).toBeNull()
    for (const overrides of [
      { status: 'Executing' },
      { status: undefined },
      { operationId: undefined },
      { operationId: ' ' },
      { commitSha: undefined },
      { commitSha: 'A'.repeat(40) },
      { commitSha: 'a'.repeat(39) },
      { branchName: undefined },
      { branchName: '  ' },
      { branchName: 'b'.repeat(513) },
    ]) {
      expect(toObservationSource(RUN, operation(overrides))).toBeNull()
    }
    expect(toObservationSource(RUN, operation({ branchName: 'b'.repeat(512) }))).not.toBeNull()
  })

  it('gives every identifying fact its own lifetime key', () => {
    const keys = new Set([
      observationSourceKey(source),
      observationSourceKey({ ...source, runId: 'x' }),
      observationSourceKey({ ...source, operationId: 'x' }),
      observationSourceKey({ ...source, commitSha: 'b'.repeat(40) }),
      observationSourceKey({ ...source, branchName: 'x' }),
    ])
    expect(keys.size).toBe(5)
  })
})

describe('toObservationReading', () => {
  it('accepts NotRecorded with the exact source as recordable and without a source as not recordable', () => {
    expect(toObservationReading(read({ state: 'NotRecorded', source: wireSource(), target: null }), source)).toEqual({
      kind: 'NotRecorded',
      recordable: true,
    })
    expect(toObservationReading(read({ state: 'NotRecorded' }), source)).toEqual({ kind: 'NotRecorded', recordable: false })
    expect(toObservationReading(read({ state: 'NotRecorded', source: null, target: null }), source)).toEqual({
      kind: 'NotRecorded',
      recordable: false,
    })
  })

  it('accepts Unavailable only with neither source nor target', () => {
    expect(toObservationReading(read({ state: 'Unavailable' }), source)).toEqual({ kind: 'Unavailable' })
    expect(toObservationReading(read({ state: 'Unavailable', source: wireSource() }), source)).toBeNull()
    expect(toObservationReading(read({ state: 'Unavailable', target: wireTarget() }), source)).toBeNull()
  })

  it('accepts Available only for the exact source with a coherent target, ids compared without case', () => {
    expect(toObservationReading(read({ state: 'Available', source: wireSource(), target: wireTarget() }), source)).toEqual({
      kind: 'Available',
      target: {
        id: '33333333-3333-3333-3333-333333333333',
        eventId: '44444444-4444-4444-4444-444444444444',
        host: 'github.com',
        owner: 'octo-cat',
        repository: 'hello.world',
        recordedAtUtc: AT,
      },
    })
    expect(
      toObservationReading(
        read({ runId: RUN.toUpperCase(), state: 'Available', source: wireSource({ operationId: OPERATION.toUpperCase() }), target: wireTarget() }),
        source,
      ),
    ).not.toBeNull()
  })

  it('rejects every contradiction, mismatch and malformed fact without repairing it', () => {
    const available = (overrides: Record<string, unknown> = {}, targetOverrides: Record<string, unknown> = {}) =>
      read({ state: 'Available', source: wireSource(), target: wireTarget(targetOverrides), ...overrides })
    const bad: [string, GetGitHubObservationTargetResponse | null | undefined][] = [
      ['null', null],
      ['undefined', undefined],
      ['a string', 'x' as unknown as GetGitHubObservationTargetResponse],
      ['another run', read({ runId: 'other', state: 'Unavailable' })],
      ['no run', { state: 'Unavailable' } as unknown as GetGitHubObservationTargetResponse],
      ['no state', read({})],
      ['an unknown state', read({ state: 'Pending' })],
      ['a lowercase state', read({ state: 'available', source: wireSource(), target: wireTarget() })],
      ['NotRecorded with a target', read({ state: 'NotRecorded', source: wireSource(), target: wireTarget() })],
      ['NotRecorded with another operation', read({ state: 'NotRecorded', source: wireSource({ operationId: 'x' }) })],
      ['NotRecorded with another commit', read({ state: 'NotRecorded', source: wireSource({ commitSha: 'b'.repeat(40) }) })],
      ['NotRecorded with another branch', read({ state: 'NotRecorded', source: wireSource({ branchName: 'other' }) })],
      ['NotRecorded with a non-object source', read({ state: 'NotRecorded', source: 'x' })],
      ['Available without a source', available({ source: undefined })],
      ['Available without a target', available({ target: undefined })],
      ['Available with another operation', available({ source: wireSource({ operationId: 'x' }) })],
      ['Available with another commit', available({ source: wireSource({ commitSha: 'b'.repeat(40) }) })],
      ['Available with another branch', available({ source: wireSource({ branchName: 'other' }) })],
      ['another host', available({}, { host: 'example.org' })],
      ['no host', available({}, { host: undefined })],
      ['an unsupported owner', available({}, { owner: '-bad' })],
      ['an unsupported repository', available({}, { repository: '.bad' })],
      ['no id', available({}, { id: undefined })],
      ['a blank event id', available({}, { eventId: '  ' })],
      ['an invalid time', available({}, { recordedAtUtc: new Date('nope') })],
      ['a text time', available({}, { recordedAtUtc: '2026-10-12T20:00:00Z' })],
      ['no time', available({}, { recordedAtUtc: undefined })],
    ]
    for (const [name, response] of bad) {
      expect(toObservationReading(response, source), name).toBeNull()
    }
  })
})

describe('toRecordedObservation', () => {
  const submitted = { owner: 'octo-cat', repository: 'hello.world' }

  it('accepts exactly what was asked', () => {
    expect(toRecordedObservation(wrote(), source, submitted)).toMatchObject({ owner: 'octo-cat', repository: 'hello.world', host: 'github.com' })
  })

  it('rejects another run, source, names or a malformed target', () => {
    const bad: [string, RecordGitHubObservationTargetResponse | null | undefined][] = [
      ['null', null],
      ['another run', wrote({ runId: 'other' })],
      ['another operation', wrote({ source: wireSource({ operationId: 'x' }) })],
      ['another commit', wrote({ source: wireSource({ commitSha: 'b'.repeat(40) }) })],
      ['another branch', wrote({ source: wireSource({ branchName: 'x' }) })],
      ['no source', wrote({ source: undefined })],
      ['no target', wrote({ target: undefined })],
      ['another owner', wrote({ target: wireTarget({ owner: 'other' }) })],
      ['another repository', wrote({ target: wireTarget({ repository: 'other' }) })],
      ['a different case', wrote({ target: wireTarget({ owner: 'Octo-Cat' }) })],
      ['another host', wrote({ target: wireTarget({ host: 'example.org' }) })],
      ['an invalid time', wrote({ target: wireTarget({ recordedAtUtc: new Date('nope') }) })],
    ]
    for (const [name, response] of bad) {
      expect(toRecordedObservation(response, source, submitted), name).toBeNull()
    }
  })
})
