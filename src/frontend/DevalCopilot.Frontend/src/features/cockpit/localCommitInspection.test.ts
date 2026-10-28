import { describe, expect, it } from 'vitest'
import { LOCAL_COMMIT_INSPECTION_KNOWN_REASON_CODES, toLocalCommitInspection } from './localCommitInspection'
import {
  COMPLETED,
  CREATED,
  LOCAL_COMMIT_OTHER_RUN,
  LOCAL_COMMIT_RESULT,
  LOCAL_COMMIT_RUN,
  STARTED,
  localCommitInspectionOperationFor,
  notRecordedLocalCommitInspectionFor,
  recordedLocalCommitInspectionFor,
  recordedLocalCommitInspectionOf,
  unavailableLocalCommitInspectionFor,
} from './localCommitInspectionFixture'

const TARGET = { runId: LOCAL_COMMIT_RUN }
const decode = (raw: unknown) => toLocalCommitInspection(raw, TARGET)
const withOperation = (patch: Record<string, unknown>, status = 'Completed') => recordedLocalCommitInspectionOf(status, {}, patch)

describe('toLocalCommitInspection (ADR-0070 whole-answer decoder)', () => {
  it('accepts exactly the three closed statuses and no operation for the negative ones', () => {
    expect(decode(notRecordedLocalCommitInspectionFor())).toEqual({ status: 'NotRecorded', runId: LOCAL_COMMIT_RUN })
    expect(decode(unavailableLocalCommitInspectionFor())).toEqual({ status: 'Unavailable', runId: LOCAL_COMMIT_RUN })
    expect(decode(notRecordedLocalCommitInspectionFor({ status: 'Pending' }))).toBeNull()
    expect(decode(notRecordedLocalCommitInspectionFor({ operation: localCommitInspectionOperationFor() }))).toBeNull()
    expect(decode(unavailableLocalCommitInspectionFor({ operation: localCommitInspectionOperationFor() }))).toBeNull()
    expect(decode(recordedLocalCommitInspectionFor({ operation: null }))).toBeNull()
  })

  it.each([
    ['Prepared', { executionStartedAtUtc: null, completedAtUtc: null }],
    ['Executing', { completedAtUtc: null }],
    ['Completed', {}],
    ['Failed', {}],
    ['Interrupted', {}],
    ['NeedsAttention', { completedAtUtc: null }],
  ])('accepts a supported %s operation as exactly its recorded facts', (status, expectedTimes) => {
    const answer = decode(recordedLocalCommitInspectionOf(status))

    expect(answer?.status).toBe('Recorded')
    const operation = answer?.status === 'Recorded' ? answer.operation : null
    expect(operation).toMatchObject({ status, ...expectedTimes })
    expect(operation?.commitSha).toBe(status === 'Completed' ? LOCAL_COMMIT_RESULT : null)
  })

  it('admits Failed, Interrupted and NeedsAttention without an execution marker and invents no time', () => {
    for (const status of ['Failed', 'Interrupted', 'NeedsAttention']) {
      const answer = decode(recordedLocalCommitInspectionOf(status, {}, { executionStartedAtUtc: null }))
      expect(answer?.status, status).toBe('Recorded')
      expect(answer?.status === 'Recorded' ? answer.operation.executionStartedAtUtc : 'x', status).toBeNull()
    }

    expect(decode(withOperation({ executionStartedAtUtc: null }, 'Completed'))).toBeNull()
    expect(decode(withOperation({ executionStartedAtUtc: null }, 'Executing'))).toBeNull()
  })

  it('enforces the complete time tuple of every status', () => {
    expect(decode(withOperation({ executionStartedAtUtc: STARTED }, 'Prepared'))).toBeNull()
    expect(decode(withOperation({ completedAtUtc: COMPLETED }, 'Prepared'))).toBeNull()
    expect(decode(withOperation({ completedAtUtc: COMPLETED }, 'Executing'))).toBeNull()
    expect(decode(withOperation({ executionStartedAtUtc: '2026-10-19T09:00:00.0000000Z' }, 'Executing'))).toBeNull()
    expect(decode(withOperation({ completedAtUtc: null }, 'Completed'))).toBeNull()
    expect(decode(withOperation({ completedAtUtc: '2026-10-20T09:00:05.0000000Z' }, 'Completed'))).toBeNull()
    expect(decode(withOperation({ completedAtUtc: null }, 'Failed'))).toBeNull()
    expect(decode(withOperation({ completedAtUtc: null }, 'Interrupted'))).toBeNull()
    expect(decode(withOperation({ completedAtUtc: '2026-10-19T09:00:00.0000000Z', executionStartedAtUtc: null }, 'Failed'))).toBeNull()
    expect(decode(withOperation({ executionStartedAtUtc: '2026-10-20T09:00:30.0000000Z' }, 'Failed'))).toBeNull()
    expect(decode(withOperation({ executionStartedAtUtc: '2026-10-19T09:00:00.0000000Z' }, 'Failed'))).toBeNull()
    expect(decode(withOperation({ completedAtUtc: COMPLETED }, 'NeedsAttention'))).toBeNull()
    expect(decode(withOperation({ executionStartedAtUtc: '2026-10-19T09:00:00.0000000Z' }, 'NeedsAttention'))).toBeNull()
    expect(decode(withOperation({ executionStartedAtUtc: CREATED, completedAtUtc: CREATED }, 'Completed'))?.status).toBe('Recorded')
  })

  it('compares times at full tick precision', () => {
    expect(decode(withOperation({ createdAtUtc: '2026-10-20T09:00:10.1234568Z', executionStartedAtUtc: '2026-10-20T09:00:10.1234567Z' }))).toBeNull()
    expect(decode(withOperation({ createdAtUtc: '2026-10-20T09:00:10.1234567Z', executionStartedAtUtc: '2026-10-20T09:00:10.1234567Z' }))?.status).toBe(
      'Recorded',
    )
  })

  it('discloses a commit identity only for Completed and requires a valid one there', () => {
    for (const status of ['Prepared', 'Executing', 'Failed', 'Interrupted', 'NeedsAttention']) {
      expect(decode(recordedLocalCommitInspectionOf(status, {}, { commitSha: LOCAL_COMMIT_RESULT })), status).toBeNull()
    }

    expect(decode(withOperation({ commitSha: null }))).toBeNull()
    expect(decode(withOperation({ commitSha: 'A'.repeat(40) }))).toBeNull()
    expect(decode(withOperation({ commitSha: 'a'.repeat(39) }))).toBeNull()
    expect(decode(withOperation({ commitSha: '' }))).toBeNull()
  })

  it('admits reasons only in their supported shapes: None for the open states, Known or Unrecognized for the ambiguous ones', () => {
    for (const status of ['Prepared', 'Executing', 'Completed']) {
      expect(decode(recordedLocalCommitInspectionOf(status, {}, { reason: { kind: 'Unrecognized', code: null } })), status).toBeNull()
      expect(decode(recordedLocalCommitInspectionOf(status, {}, { reason: { kind: 'Known', code: 'local_commit.git_unprovable' } })), status).toBeNull()
    }

    for (const status of ['Failed', 'Interrupted', 'NeedsAttention']) {
      expect(decode(recordedLocalCommitInspectionOf(status, {}, { reason: { kind: 'None', code: null } })), status).toBeNull()
      expect(decode(recordedLocalCommitInspectionOf(status, {}, { reason: { kind: 'Unrecognized', code: null } }))?.status, status).toBe('Recorded')
    }
  })

  it('accepts every allowlisted known code exactly and refuses prefix lookalikes, other cases and raw codes', () => {
    for (const code of LOCAL_COMMIT_INSPECTION_KNOWN_REASON_CODES) {
      const answer = decode(recordedLocalCommitInspectionOf('NeedsAttention', {}, { reason: { kind: 'Known', code } }))
      expect(answer?.status === 'Recorded' ? answer.operation.reason : null).toEqual({ kind: 'Known', code })
    }

    for (const code of ['local_commit.git_unprovable2', 'LOCAL_COMMIT.GIT_UNPROVABLE', 'local_commit.', 'local_commit.arbitrary', '', null]) {
      expect(decode(recordedLocalCommitInspectionOf('NeedsAttention', {}, { reason: { kind: 'Known', code } })), String(code)).toBeNull()
    }

    expect(decode(recordedLocalCommitInspectionOf('Failed', {}, { reason: { kind: 'Unrecognized', code: 'local_commit.arbitrary' } }))).toBeNull()
    expect(decode(recordedLocalCommitInspectionOf('Failed', {}, { reason: { kind: 'None', code: 'local_commit.git_unprovable' } }))).toBeNull()
    expect(decode(recordedLocalCommitInspectionOf('Failed', {}, { reason: { kind: 'Other', code: null } }))).toBeNull()
    expect(decode(recordedLocalCommitInspectionOf('Failed', {}, { reason: { kind: 'Unrecognized' } }))).toBeNull()
    expect(decode(recordedLocalCommitInspectionOf('Failed', {}, { reason: { kind: 'Unrecognized', code: null, extra: 1 } }))).toBeNull()
    expect(decode(recordedLocalCommitInspectionOf('Failed', {}, { reason: null }))).toBeNull()
  })

  it('refuses an unknown or non-string operation status', () => {
    for (const status of ['Pending', 'prepared', 'Cancelled', '', null, 1]) {
      expect(decode(withOperation({ status })), String(status)).toBeNull()
    }
  })

  it('applies the exact integer bounds, including both extrema and no clamping', () => {
    expect(decode(withOperation({ checkpointNumber: 1 }))?.status).toBe('Recorded')
    expect(decode(withOperation({ checkpointNumber: 2147483647 }))?.status).toBe('Recorded')
    for (const checkpointNumber of [0, -1, 2147483648, 1.5, '2', null, Number.NaN]) {
      expect(decode(withOperation({ checkpointNumber })), String(checkpointNumber)).toBeNull()
    }

    expect(decode(withOperation({ changedPathCount: 1 }))?.status).toBe('Recorded')
    expect(decode(withOperation({ changedPathCount: 128 }))?.status).toBe('Recorded')
    for (const changedPathCount of [0, 129, -1, 1.5, '3', null]) {
      expect(decode(withOperation({ changedPathCount })), String(changedPathCount)).toBeNull()
    }

    expect(decode(withOperation({ totalBytes: 0 }))?.status).toBe('Recorded')
    expect(decode(withOperation({ totalBytes: 4194304 }))?.status).toBe('Recorded')
    for (const totalBytes of [-1, 4194305, 0.5, '10', null, 2 ** 53]) {
      expect(decode(withOperation({ totalBytes })), String(totalBytes)).toBeNull()
    }
  })

  it('admits only canonical lowercase identifiers, lowercase object ids and fingerprints', () => {
    for (const key of ['operationId', 'projectId', 'workspaceId', 'checkpointId']) {
      const valid = localCommitInspectionOperationFor()[key] as string
      for (const bad of [valid.toUpperCase(), '00000000-0000-0000-0000-000000000000', valid.replaceAll('-', ''), `{${valid}}`, `${valid} `, '', null, 7]) {
        expect(decode(withOperation({ [key]: bad })), `${key} ${String(bad)}`).toBeNull()
      }
    }

    for (const key of ['parentCommitSha', 'treeSha']) {
      for (const bad of ['A'.repeat(40), 'a'.repeat(39), 'a'.repeat(41), 'g'.repeat(40), '', null]) {
        expect(decode(withOperation({ [key]: bad })), `${key} ${String(bad)}`).toBeNull()
      }
    }

    for (const bad of ['F'.repeat(64), 'f'.repeat(63), 'f'.repeat(65), '', null]) {
      expect(decode(withOperation({ checkpointFingerprintSha256: bad })), String(bad)).toBeNull()
    }
  })

  it('admits a branch of one to 512 characters without a control character and refuses blank, oversized and controlled text', () => {
    expect(decode(withOperation({ branchName: 'b' }))?.status).toBe('Recorded')
    expect(decode(withOperation({ branchName: 'b'.repeat(512) }))?.status).toBe('Recorded')
    for (const branchName of ['', '   ', 'b'.repeat(513), 'a\u0000b', 'a\nb', 'a\u007fb', 'a\u0085b', null, 5]) {
      expect(decode(withOperation({ branchName })), JSON.stringify(branchName)).toBeNull()
    }
  })

  it('admits only the canonical seven-digit UTC round-trip time', () => {
    for (const bad of [
      '2026-10-20T09:00:00Z',
      '2026-10-20T09:00:00.123Z',
      '2026-10-20T09:00:00.1234567+00:00',
      '2026-10-20 09:00:00.1234567Z',
      '2026-13-20T09:00:00.1234567Z',
      '',
      null,
      1,
    ]) {
      expect(decode(withOperation({ createdAtUtc: bad })), String(bad)).toBeNull()
    }

    expect(decode(withOperation({ executionStartedAtUtc: '2026-10-20T09:00:10Z' }))).toBeNull()
    expect(decode(withOperation({ completedAtUtc: 'later' }))).toBeNull()
  })

  // LCMI-R1 (ADR-0070): every supplied time is nondefault, while a legitimate year-0001 instant stays valid at full tick precision.
  describe('the default UTC instant', () => {
    const DEFAULT = '0001-01-01T00:00:00.0000000Z'
    const ONE_TICK = '0001-01-01T00:00:00.0000001Z'

    it('refuses a default preparation instant on a Prepared operation', () => {
      expect(decode(recordedLocalCommitInspectionOf('Prepared', {}, { createdAtUtc: DEFAULT }))).toBeNull()
    })

    it('refuses a default preparation instant on a Completed operation with ordinary later times', () => {
      expect(decode(withOperation({ createdAtUtc: DEFAULT }))).toBeNull()
    })

    it('refuses a default execution marker or completion even where the order would otherwise hold', () => {
      expect(decode(withOperation({ createdAtUtc: DEFAULT, executionStartedAtUtc: DEFAULT, completedAtUtc: DEFAULT }))).toBeNull()
      expect(decode(withOperation({ createdAtUtc: DEFAULT, executionStartedAtUtc: null, completedAtUtc: COMPLETED }, 'Failed'))).toBeNull()
      expect(decode(withOperation({ createdAtUtc: DEFAULT, executionStartedAtUtc: null, completedAtUtc: null }, 'NeedsAttention'))).toBeNull()
    })

    it('keeps a legitimate instant one tick after the default, with full tick ordering', () => {
      expect(decode(recordedLocalCommitInspectionOf('Prepared', {}, { createdAtUtc: ONE_TICK }))?.status).toBe('Recorded')
      expect(
        decode(withOperation({ createdAtUtc: ONE_TICK, executionStartedAtUtc: ONE_TICK, completedAtUtc: '0001-01-01T00:00:00.0000002Z' }))?.status,
      ).toBe('Recorded')
      expect(
        decode(withOperation({ createdAtUtc: '0001-01-01T00:00:00.0000002Z', executionStartedAtUtc: ONE_TICK, completedAtUtc: COMPLETED })),
      ).toBeNull()
    })
  })

  it('refuses an answer for another run, a missing member, an extra member or the wrong shape', () => {
    expect(decode(recordedLocalCommitInspectionFor({ runId: LOCAL_COMMIT_OTHER_RUN }))).toBeNull()
    expect(decode(recordedLocalCommitInspectionFor({ runId: LOCAL_COMMIT_RUN.toUpperCase() }))).toBeNull()
    expect(decode({ ...recordedLocalCommitInspectionFor(), extra: 1 })).toBeNull()
    expect(decode(withOperation({ extra: 1 }))).toBeNull()

    const { operation: _dropped, ...withoutOperation } = notRecordedLocalCommitInspectionFor()
    expect(decode(withoutOperation)).toBeNull()
    const { status: _status, ...withoutStatus } = notRecordedLocalCommitInspectionFor()
    expect(decode(withoutStatus)).toBeNull()

    for (const key of Object.keys(localCommitInspectionOperationFor())) {
      const { [key]: _removed, ...partial } = localCommitInspectionOperationFor()
      expect(decode(recordedLocalCommitInspectionFor({ operation: partial })), key).toBeNull()
    }

    for (const raw of [undefined, null, 'text', 7, [], [recordedLocalCommitInspectionFor()]]) {
      expect(decode(raw)).toBeNull()
    }
  })

  it('never partially accepts: any single violated field refuses the whole answer', () => {
    const accepted = decode(recordedLocalCommitInspectionFor())
    expect(accepted?.status).toBe('Recorded')

    expect(decode(withOperation({ totalBytes: 4194305 }))).toBeNull()
    expect(decode(withOperation({ workspaceId: 'w' }))).toBeNull()
  })
})
