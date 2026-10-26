import { describe, expect, it } from 'vitest'
import {
  decideProjectVerificationWindowAnswer,
  recordedDescriptorOf,
  toProjectVerificationHistory,
  toProjectVerificationInspection,
} from './projectVerificationInspection'
import type { ProjectVerificationWindowRequestFacts } from './projectVerificationInspection'
import {
  CHECKPOINT,
  FINGERPRINT,
  OTHER_FINGERPRINT,
  PROJECT,
  artifactIdFor,
  emptyPageFor,
  executionFor,
  executionIdFor,
  guidOf,
  indexedItemFor,
  inspectionFor,
  instantAt,
  itemFor,
  notRecordedStream,
  pageFor,
  recordedStreamFor,
  refusedWindowFor,
  unavailableInspectionFor,
  unavailablePageFor,
  unavailableStream,
  windowFor,
} from './projectVerificationInspectionFixture'

const history = (raw: unknown, before: number | null = null) => toProjectVerificationHistory(raw, { projectId: PROJECT, beforeExecutionNumber: before })
const inspect = (raw: unknown, number = 7) =>
  toProjectVerificationInspection(raw, { projectId: PROJECT, item: indexedItemFor(number) })

describe('toProjectVerificationHistory', () => {
  it('accepts an empty page, a partial page and a full page with its cursor', () => {
    expect(history(emptyPageFor())).toEqual({ status: 'Recorded', items: [], hasMore: false, nextBeforeExecutionNumber: null })
    expect(history(pageFor(7, 7))).toMatchObject({ status: 'Recorded', hasMore: false, nextBeforeExecutionNumber: null })

    const full = history(pageFor(45, 20))
    expect(full).toMatchObject({ status: 'Recorded', hasMore: true, nextBeforeExecutionNumber: 26 })
    expect(full?.status === 'Recorded' ? full.items.map((item) => item.executionNumber) : []).toEqual(
      Array.from({ length: 20 }, (_, index) => 45 - index),
    )
  })

  it('accepts the exact end of history: a full page ending at number 1 has no older page', () => {
    expect(history(pageFor(20, 20))).toMatchObject({ status: 'Recorded', hasMore: false, nextBeforeExecutionNumber: null })
  })

  it('accepts a page strictly below the requested cursor and refuses one that reaches it', () => {
    expect(history(pageFor(25, 5), 26)).toMatchObject({ status: 'Recorded' })
    expect(history(pageFor(26, 5), 26)).toBeNull()
    expect(history(pageFor(30, 5), 26)).toBeNull()
  })

  it('accepts Unavailable only with no item, no further page and no cursor', () => {
    expect(history(unavailablePageFor())).toEqual({ status: 'Unavailable' })
    expect(history({ ...unavailablePageFor(), items: [itemFor(1)] })).toBeNull()
    expect(history({ ...unavailablePageFor(), hasMore: true })).toBeNull()
    expect(history({ ...unavailablePageFor(), nextBeforeExecutionNumber: 4 })).toBeNull()
  })

  it('refuses a wrong or missing project, an unknown status, extra or missing keys and a non-object answer', () => {
    expect(history(pageFor(5, 2, {}, guidOf('p', 2)))).toBeNull()
    expect(history({ ...pageFor(5, 2), projectId: undefined })).toBeNull()
    expect(history({ ...pageFor(5, 2), status: 'Other' })).toBeNull()
    expect(history({ ...pageFor(5, 2), extra: 1 })).toBeNull()
    const { items: _items, ...withoutItems } = pageFor(5, 2)
    expect(history(withoutItems)).toBeNull()
    for (const raw of [null, undefined, 'x', 4, [], []]) {
      expect(history(raw)).toBeNull()
    }
  })

  it('refuses more than 20 items, unordered, repeated or non-descending numbers and duplicate identities', () => {
    expect(history(pageFor(40, 21))).toBeNull()
    expect(history({ ...pageFor(5, 3), items: [itemFor(3), itemFor(4), itemFor(5)] })).toBeNull()
    expect(history({ ...pageFor(5, 3), items: [itemFor(5), itemFor(5, { executionId: guidOf('x', 1) }), itemFor(3)] })).toBeNull()
    expect(history({ ...pageFor(5, 3), items: [itemFor(5), itemFor(4), itemFor(3, { executionId: executionIdFor(5) })] })).toBeNull()
  })

  it('refuses a damaged member wholly, never filtering it away', () => {
    const base = pageFor(6, 3)
    const damaged = (patch: Record<string, unknown>) => ({ ...base, items: [base.items[0], itemFor(5, patch), base.items[2]] })
    expect(history(base)).not.toBeNull()
    for (const patch of [
      { executionId: executionIdFor(5).toUpperCase() },
      { executionId: '00000000-0000-0000-0000-000000000000' },
      { executionId: 'not-a-guid' },
      { executionNumber: 0 },
      { executionNumber: 5.5 },
      { executionNumber: '5' },
      { executionNumber: 2 ** 31 },
      { commandName: '   ' },
      { commandName: 'x'.repeat(201) },
      { commandName: 5 },
      { claimedAtUtc: '2026-10-20T09:00:00Z' },
      { claimedAtUtc: '2026-10-20T09:00:00.0000000+00:00' },
      { claimedAtUtc: '2026-13-20T09:00:00.0000000Z' },
      { claimedAtUtc: null },
      { extra: true },
    ]) {
      expect(history(damaged(patch)), JSON.stringify(patch)).toBeNull()
    }
  })

  it('accepts a command name of exactly 200 units and keeps literal text untouched', () => {
    const name = `<b>${'é'.repeat(197)}`
    const accepted = history({ ...pageFor(3, 1), items: [itemFor(3, { commandName: name })] })
    expect(accepted?.status === 'Recorded' ? accepted.items[0].commandName : null).toBe(name)
  })

  it('refuses an inconsistent cursor', () => {
    expect(history({ ...pageFor(45, 20), nextBeforeExecutionNumber: 27 })).toBeNull()
    expect(history({ ...pageFor(45, 20), nextBeforeExecutionNumber: null })).toBeNull()
    expect(history({ ...pageFor(45, 20), hasMore: false })).toBeNull()
    expect(history({ ...pageFor(45, 20), hasMore: 'true' })).toBeNull()
    expect(history({ ...pageFor(7, 7), hasMore: true, nextBeforeExecutionNumber: 1 })).toBeNull()
    expect(history({ ...pageFor(7, 7), nextBeforeExecutionNumber: 1 })).toBeNull()
    expect(history({ ...pageFor(20, 20), hasMore: true, nextBeforeExecutionNumber: 1 })).toBeNull()
    expect(history({ ...emptyPageFor(), hasMore: true, nextBeforeExecutionNumber: 1 })).toBeNull()
    expect(history({ ...pageFor(7, 7), nextBeforeExecutionNumber: undefined })).toBeNull()
  })
})

describe('toProjectVerificationInspection', () => {
  it('accepts the supported tuple of every recorded status exactly as the host proves them', () => {
    const dispatched = instantAt(30, 1)
    const completed = instantAt(30, 9)
    const accepted: Record<string, unknown>[] = [
      // Running, with and without a dispatch marker.
      { status: 'Running', outcome: null, exitCode: null, completionFingerprintSha256: null, dispatchedAtUtc: null, completedAtUtc: null },
      { status: 'Running', outcome: null, exitCode: null, completionFingerprintSha256: null, dispatchedAtUtc: dispatched, completedAtUtc: null },
      { status: 'Passed' },
      { status: 'Failed', exitCode: 2147483647 },
      { status: 'Failed', exitCode: -2147483648 },
      { status: 'Failed', exitCode: -2147483647 },
      { status: 'Failed', outcome: 'Failed', exitCode: null },
      { status: 'TimedOut', outcome: 'TimedOut', exitCode: null },
      { status: 'Cancelled', outcome: 'Cancelled', exitCode: null },
      // Interrupted, before and after a dispatch.
      { status: 'Interrupted', outcome: null, exitCode: null, completionFingerprintSha256: null, dispatchedAtUtc: null },
      { status: 'Interrupted', outcome: null, exitCode: null, completionFingerprintSha256: null },
      // SourceChanged before dispatch, and after a dispatch for every outcome.
      { status: 'SourceChanged', outcome: null, exitCode: null, completionFingerprintSha256: OTHER_FINGERPRINT, dispatchedAtUtc: null },
      { status: 'SourceChanged', outcome: 'Exited', exitCode: 0, completionFingerprintSha256: OTHER_FINGERPRINT },
      { status: 'SourceChanged', outcome: 'Exited', exitCode: 4, completionFingerprintSha256: OTHER_FINGERPRINT },
      { status: 'SourceChanged', outcome: 'TimedOut', exitCode: null, completionFingerprintSha256: OTHER_FINGERPRINT },
      { status: 'SourceChanged', outcome: 'Cancelled', exitCode: null, completionFingerprintSha256: OTHER_FINGERPRINT },
      { status: 'SourceChanged', outcome: 'Failed', exitCode: null, completionFingerprintSha256: OTHER_FINGERPRINT },
      {
        status: 'LocalExecutionSequenceDispatchRefused',
        outcome: null,
        exitCode: null,
        completionFingerprintSha256: null,
        dispatchedAtUtc: null,
        standardOutput: notRecordedStream(),
        standardError: unavailableStream(),
      },
    ]
    for (const patch of accepted) {
      const result = inspect(inspectionFor(7, {}, { completedAtUtc: completed, ...patch }))
      expect(result?.status, JSON.stringify(patch)).toBe('Recorded')
    }

    const all = inspect(inspectionFor(7))
    expect(all).toMatchObject({ status: 'Recorded', execution: { status: 'Passed', exitCode: 0, outcome: 'Exited', checkpointNumber: 3 } })
    expect(all?.status === 'Recorded' ? all.execution.claimFingerprintSha256 : null).toBe(FINGERPRINT)
  })

  it('refuses every contradictory tuple', () => {
    const completed = instantAt(30, 9)
    const refused: Record<string, unknown>[] = [
      { status: 'Running', completedAtUtc: completed },
      { status: 'Running', outcome: 'Exited', exitCode: null, completionFingerprintSha256: null, completedAtUtc: null },
      { status: 'Running', outcome: null, exitCode: 0, completionFingerprintSha256: null, completedAtUtc: null },
      { status: 'Running', outcome: null, exitCode: null, completionFingerprintSha256: FINGERPRINT, completedAtUtc: null },
      { status: 'Passed', exitCode: 1 },
      { status: 'Passed', outcome: 'TimedOut', exitCode: null },
      { status: 'Passed', outcome: null, exitCode: null },
      { status: 'Passed', completionFingerprintSha256: OTHER_FINGERPRINT },
      { status: 'Passed', completionFingerprintSha256: null },
      { status: 'Passed', dispatchedAtUtc: null },
      { status: 'Passed', completedAtUtc: null },
      { status: 'Failed', exitCode: 0 },
      { status: 'Failed', outcome: 'Failed', exitCode: 3 },
      { status: 'Failed', outcome: 'Exited', exitCode: null },
      { status: 'Failed', exitCode: 3, completionFingerprintSha256: OTHER_FINGERPRINT },
      { status: 'TimedOut', outcome: 'Exited', exitCode: 0 },
      { status: 'TimedOut', outcome: 'TimedOut', exitCode: 1 },
      { status: 'Cancelled', outcome: 'TimedOut', exitCode: null },
      { status: 'Interrupted', outcome: 'Exited', exitCode: null, completionFingerprintSha256: null },
      { status: 'Interrupted', outcome: null, exitCode: null, completionFingerprintSha256: FINGERPRINT },
      { status: 'Interrupted', outcome: null, exitCode: null, completionFingerprintSha256: null, completedAtUtc: null },
      { status: 'SourceChanged', outcome: 'Exited', exitCode: 0, completionFingerprintSha256: FINGERPRINT },
      { status: 'SourceChanged', outcome: 'Exited', exitCode: 0, completionFingerprintSha256: null },
      { status: 'SourceChanged', outcome: 'Exited', exitCode: null, completionFingerprintSha256: OTHER_FINGERPRINT },
      { status: 'SourceChanged', outcome: 'TimedOut', exitCode: 1, completionFingerprintSha256: OTHER_FINGERPRINT },
      { status: 'SourceChanged', outcome: null, exitCode: null, completionFingerprintSha256: OTHER_FINGERPRINT },
      { status: 'SourceChanged', outcome: 'Exited', exitCode: 0, completionFingerprintSha256: OTHER_FINGERPRINT, dispatchedAtUtc: null },
      { status: 'SourceChanged', outcome: null, exitCode: null, completionFingerprintSha256: OTHER_FINGERPRINT, dispatchedAtUtc: null, completedAtUtc: null },
      { status: 'LocalExecutionSequenceDispatchRefused', outcome: null, exitCode: null, completionFingerprintSha256: null },
      { status: 'LocalExecutionSequenceDispatchRefused', outcome: 'Failed', exitCode: null, completionFingerprintSha256: null, dispatchedAtUtc: null },
      { status: 'LocalExecutionSequenceDispatchRefused', outcome: null, exitCode: null, completionFingerprintSha256: FINGERPRINT, dispatchedAtUtc: null },
      { status: 'Unknown' },
      { status: 'passed' },
      { outcome: 'Aborted' },
    ]
    for (const patch of refused) {
      expect(inspect(inspectionFor(7, {}, { completedAtUtc: completed, ...patch })), JSON.stringify(patch)).toBeNull()
    }
  })

  it('refuses a chronology that runs backwards, compared at tick precision', () => {
    const claimed = itemFor(7).claimedAtUtc
    expect(claimed).toBe('2026-10-20T09:00:07.0000000Z')
    expect(inspect(inspectionFor(7, {}, { dispatchedAtUtc: instantAt(0, 6) }))).toBeNull()
    expect(inspect(inspectionFor(7, {}, { dispatchedAtUtc: '2026-10-20T09:00:07.0000000Z', completedAtUtc: '2026-10-20T09:00:06.9999999Z' }))).toBeNull()
    expect(inspect(inspectionFor(7, {}, { dispatchedAtUtc: '2026-10-20T09:00:07.0000000Z', completedAtUtc: '2026-10-20T09:00:07.0000001Z' }))).not.toBeNull()
    expect(inspect(inspectionFor(7, {}, { dispatchedAtUtc: claimed, completedAtUtc: claimed }))).not.toBeNull()
    expect(
      inspect(inspectionFor(7, {}, { status: 'Interrupted', outcome: null, exitCode: null, completionFingerprintSha256: null, dispatchedAtUtc: null, completedAtUtc: instantAt(0, 6) })),
    ).toBeNull()
    expect(
      inspect(inspectionFor(7, {}, { status: 'Interrupted', outcome: null, exitCode: null, completionFingerprintSha256: null, dispatchedAtUtc: null, completedAtUtc: claimed })),
    ).not.toBeNull()
  })

  it('accepts Unavailable only with no execution', () => {
    expect(inspect(unavailableInspectionFor(7))).toEqual({ status: 'Unavailable' })
    expect(inspect({ ...unavailableInspectionFor(7), execution: executionFor(7) })).toBeNull()
    expect(inspect({ ...unavailableInspectionFor(7), execution: undefined })).toBeNull()
  })

  it('refuses another project or execution, unknown statuses, missing or extra keys and non-objects', () => {
    expect(inspect(inspectionFor(7, {}, {}, guidOf('p', 2)))).toBeNull()
    expect(inspect(inspectionFor(7, { executionId: executionIdFor(8) }))).toBeNull()
    expect(inspect(inspectionFor(7, { status: 'Other' }))).toBeNull()
    expect(inspect(inspectionFor(7, { extra: 1 }))).toBeNull()
    expect(inspect(inspectionFor(7, {}, { extra: 1 }))).toBeNull()
    const { execution: _execution, ...without } = inspectionFor(7)
    expect(inspect(without)).toBeNull()
    expect(inspect(inspectionFor(7, { execution: null }))).toBeNull()
    for (const raw of [null, undefined, 'x', 3, []]) {
      expect(inspect(raw)).toBeNull()
    }
  })

  it('binds the answer to the indexed number, snapshot name and claim time: a changed immutable fact is not a silent replacement', () => {
    expect(inspect(inspectionFor(7, {}, { executionNumber: 8 }))).toBeNull()
    expect(inspect(inspectionFor(7, {}, { commandName: 'Renamed' }))).toBeNull()
    expect(inspect(inspectionFor(7, {}, { claimedAtUtc: instantAt(0, 8) }))).toBeNull()
    expect(inspect(inspectionFor(7, {}, { commandName: 'Check 7 ' }))).toBeNull()
    expect(inspect(inspectionFor(7), 8)).toBeNull()
  })

  it('refuses malformed identities, numbers, fingerprints and exit codes', () => {
    for (const patch of [
      { commandId: 'x' },
      { commandId: guidOf('m', 1).toUpperCase() },
      { workspaceId: '00000000-0000-0000-0000-000000000000' },
      { checkpointId: null },
      { checkpointNumber: 0 },
      { checkpointNumber: 1.5 },
      { checkpointNumber: 2 ** 31 },
      { claimFingerprintSha256: 'A'.repeat(64) },
      { claimFingerprintSha256: 'a'.repeat(63) },
      { claimFingerprintSha256: `${'a'.repeat(63)}g` },
      { claimFingerprintSha256: null },
      { completionFingerprintSha256: 'a'.repeat(65) },
      { completionFingerprintSha256: undefined },
      { exitCode: 0.5 },
      { exitCode: '0' },
      { exitCode: 2 ** 31 },
      { dispatchedAtUtc: '2026-10-20 09:30:01' },
      { completedAtUtc: instantAt(30, 9).replace('Z', '+00:00') },
    ]) {
      expect(inspect(inspectionFor(7, {}, patch)), JSON.stringify(patch)).toBeNull()
    }
  })

  it('accepts a SourceChanged completion fingerprint different from the claim and legitimate Int32 extreme exit codes', () => {
    const changed = inspect(inspectionFor(7, {}, { status: 'SourceChanged', completionFingerprintSha256: OTHER_FINGERPRINT }))
    expect(changed).toMatchObject({
      status: 'Recorded',
      execution: { claimFingerprintSha256: FINGERPRINT, completionFingerprintSha256: OTHER_FINGERPRINT, checkpointId: CHECKPOINT },
    })
    for (const exitCode of [2147483647, -2147483648, -2147483647, 1, -1]) {
      expect(inspect(inspectionFor(7, {}, { status: 'Failed', exitCode }))?.status, String(exitCode)).toBe('Recorded')
    }
  })

  it('accepts every stream descriptor tuple the Domain records and refuses the rest', () => {
    const tuple = (standardOutput: unknown, standardError: unknown) => inspect(inspectionFor(7, {}, { standardOutput, standardError }))
    expect(tuple(notRecordedStream(), unavailableStream())?.status).toBe('Recorded')
    expect(tuple(recordedStreamFor(7, 'stdout', { byteLength: 0 }), notRecordedStream())?.status).toBe('Recorded')
    expect(
      tuple(
        recordedStreamFor(7, 'stdout', { captureOutcome: 'RecoveredAfterHostInterruption', truncated: null }),
        recordedStreamFor(7, 'stderr', { truncated: false }),
      )?.status,
    ).toBe('Recorded')
    expect(tuple(recordedStreamFor(7, 'stdout', { byteLength: 2 ** 53 - 1 }), notRecordedStream())?.status).toBe('Recorded')

    expect(tuple(recordedStreamFor(7, 'stdout', { truncated: null }), notRecordedStream())).toBeNull()
    expect(tuple(recordedStreamFor(7, 'stdout', { captureOutcome: 'RecoveredAfterHostInterruption', truncated: true }), notRecordedStream())).toBeNull()
    expect(tuple(recordedStreamFor(7, 'stdout', { captureOutcome: 'Other' }), notRecordedStream())).toBeNull()
    expect(tuple(recordedStreamFor(7, 'stdout', { byteLength: -1 }), notRecordedStream())).toBeNull()
    expect(tuple(recordedStreamFor(7, 'stdout', { byteLength: 2 ** 53 }), notRecordedStream())).toBeNull()
    expect(tuple(recordedStreamFor(7, 'stdout', { artifactId: 'not-a-guid' }), notRecordedStream())).toBeNull()
    expect(tuple({ ...notRecordedStream(), artifactId: artifactIdFor(7, 'stdout') }, notRecordedStream())).toBeNull()
    expect(tuple({ ...recordedStreamFor(7, 'stdout'), extra: 1 }, notRecordedStream())).toBeNull()
    expect(tuple(recordedStreamFor(7, 'stdout'), recordedStreamFor(7, 'stdout'))).toBeNull()
    expect(tuple(recordedStreamFor(7, 'stdout'), undefined)).toBeNull()
  })

  it('exposes the Recorded descriptor of a stream and nothing for any other state', () => {
    const accepted = inspect(inspectionFor(7, {}, { standardError: notRecordedStream() }))
    expect(recordedDescriptorOf(accepted, 'stdout')).toMatchObject({ status: 'Recorded', byteLength: 12 })
    expect(recordedDescriptorOf(accepted, 'stderr')).toBeNull()
    expect(recordedDescriptorOf(inspect(unavailableInspectionFor(7)), 'stdout')).toBeNull()
    expect(recordedDescriptorOf(null, 'stdout')).toBeNull()
  })
})

describe('decideProjectVerificationWindowAnswer', () => {
  const descriptor = (number = 7, stream: 'stdout' | 'stderr' = 'stdout') => {
    const raw = inspect(inspectionFor(number, {}, {}), number)
    const found = recordedDescriptorOf(raw, stream)
    if (found === null) {
      throw new Error('fixture descriptor')
    }

    return found
  }
  const request = (patch: Partial<ProjectVerificationWindowRequestFacts> = {}): ProjectVerificationWindowRequestFacts => ({
    projectId: PROJECT,
    executionId: executionIdFor(7),
    stream: 'stdout',
    descriptor: descriptor(),
    fromOffset: 0,
    maxBytes: 16384,
    ...patch,
  })
  const decide = (raw: unknown, patch: Partial<ProjectVerificationWindowRequestFacts> = {}) =>
    decideProjectVerificationWindowAnswer(raw, request(patch))

  it('accepts an exact whole-output window and a window that ends the output', () => {
    expect(decide(windowFor(7, 'stdout'))).toEqual({ kind: 'ok', text: 'hello output', nextOffset: 12, totalLength: 12 })
    expect(
      decide({ ...windowFor(7, 'stdout'), text: 'hello', nextOffset: 5 }, { maxBytes: 5 }),
    ).toEqual({ kind: 'ok', text: 'hello', nextOffset: 5, totalLength: 12 })
    expect(decide({ ...windowFor(7, 'stdout'), text: ' output', nextOffset: 12 }, { fromOffset: 5 })).toMatchObject({ kind: 'ok' })
  })

  it('accepts an empty output read whole and an exhausted cursor', () => {
    const empty = inspect(inspectionFor(7, {}, { standardOutput: recordedStreamFor(7, 'stdout', { byteLength: 0 }) }))
    const zero = recordedDescriptorOf(empty, 'stdout')!
    const answer = { ...windowFor(7, 'stdout'), artifact: recordedStreamFor(7, 'stdout', { byteLength: 0 }), text: '', nextOffset: 0, totalLength: 0 }
    expect(decide(answer, { descriptor: zero })).toEqual({ kind: 'ok', text: '', nextOffset: 0, totalLength: 0 })
    expect(decide({ ...windowFor(7, 'stdout'), text: '', nextOffset: 12 }, { fromOffset: 12 })).toMatchObject({ kind: 'ok', text: '' })
  })

  it('maps each non-Ok status to its own closed answer with no artifact, no text, the offset unchanged and a zero length', () => {
    expect(decide(refusedWindowFor(7, 'stdout', 'Unavailable'))).toEqual({ kind: 'unavailable' })
    expect(decide(refusedWindowFor(7, 'stdout', 'NotRecorded'))).toEqual({ kind: 'not-recorded' })
    expect(decide(refusedWindowFor(7, 'stdout', 'Missing', 5), { fromOffset: 5 })).toEqual({ kind: 'missing' })
    expect(decide(refusedWindowFor(7, 'stdout', 'IntegrityMismatch'))).toEqual({ kind: 'integrity-mismatch' })
  })

  it('refuses a refusal that carries anything', () => {
    const refused = refusedWindowFor(7, 'stdout', 'Missing')
    expect(decide({ ...refused, artifact: recordedStreamFor(7, 'stdout') })).toEqual({ kind: 'incoherent' })
    expect(decide({ ...refused, text: 'x' })).toEqual({ kind: 'incoherent' })
    expect(decide({ ...refused, nextOffset: 3 })).toEqual({ kind: 'incoherent' })
    expect(decide({ ...refused, totalLength: 12 })).toEqual({ kind: 'incoherent' })
    expect(decide({ ...refused, status: 'Other' })).toEqual({ kind: 'incoherent' })
    expect(decide({ ...refused, status: 'Ok' })).toEqual({ kind: 'incoherent' })
  })

  it('refuses another project, execution or stream, extra or missing keys and non-objects', () => {
    expect(decide(windowFor(7, 'stdout', {}, guidOf('p', 2)))).toEqual({ kind: 'incoherent' })
    expect(decide(windowFor(8, 'stdout'))).toEqual({ kind: 'incoherent' })
    expect(decide(windowFor(7, 'stderr'))).toEqual({ kind: 'incoherent' })
    expect(decide({ ...windowFor(7, 'stdout'), stream: 'STDOUT' })).toEqual({ kind: 'incoherent' })
    expect(decide({ ...windowFor(7, 'stdout'), extra: 1 })).toEqual({ kind: 'incoherent' })
    const { text: _text, ...without } = windowFor(7, 'stdout')
    expect(decide(without)).toEqual({ kind: 'incoherent' })
    for (const raw of [null, undefined, 'x', 1, []]) {
      expect(decide(raw)).toEqual({ kind: 'incoherent' })
    }
  })

  it('refuses an Ok window whose descriptor, length or cursor breaks the byte-cursor invariants', () => {
    const ok = windowFor(7, 'stdout')
    expect(decide({ ...ok, artifact: recordedStreamFor(7, 'stdout', { byteLength: 13 }) })).toEqual({ kind: 'incoherent' })
    expect(decide({ ...ok, artifact: recordedStreamFor(7, 'stdout', { artifactId: artifactIdFor(9, 'stdout') }) })).toEqual({ kind: 'incoherent' })
    expect(decide({ ...ok, artifact: recordedStreamFor(7, 'stdout', { truncated: true }) })).toEqual({ kind: 'incoherent' })
    expect(decide({ ...ok, artifact: recordedStreamFor(7, 'stdout', { captureOutcome: 'RecoveredAfterHostInterruption', truncated: null }) })).toEqual({ kind: 'incoherent' })
    expect(decide({ ...ok, artifact: null })).toEqual({ kind: 'incoherent' })
    expect(decide({ ...ok, totalLength: 11 })).toEqual({ kind: 'incoherent' })
    expect(decide({ ...ok, nextOffset: 13 })).toEqual({ kind: 'incoherent' })
    expect(decide({ ...ok, nextOffset: -1, text: '' })).toEqual({ kind: 'incoherent' })
    expect(decide({ ...ok, nextOffset: 1.5 })).toEqual({ kind: 'incoherent' })
    expect(decide({ ...ok, text: 5 })).toEqual({ kind: 'incoherent' })
    // Advances beyond the requested cap, stalls with bytes left, advances with no text, or yields more units than bytes consumed.
    expect(decide(ok, { maxBytes: 11 })).toEqual({ kind: 'incoherent' })
    expect(decide({ ...ok, text: '', nextOffset: 0 })).toEqual({ kind: 'incoherent' })
    expect(decide({ ...ok, text: '' })).toEqual({ kind: 'incoherent' })
    expect(decide({ ...ok, text: 'hello', nextOffset: 3 }, { maxBytes: 16384 })).toEqual({ kind: 'incoherent' })
    expect(decide({ ...ok, text: 'x' }, { fromOffset: 12 })).toEqual({ kind: 'incoherent' })
    expect(decide({ ...ok, nextOffset: 4, text: 'hello output' }, { fromOffset: 6 })).toEqual({ kind: 'incoherent' })
  })

  it('does not re-encode the text: a replacement character and a split code point stay a valid byte-cursor advance', () => {
    expect(decide({ ...windowFor(7, 'stdout'), text: '�', nextOffset: 3 }, { maxBytes: 3 })).toMatchObject({ kind: 'ok', nextOffset: 3 })
    expect(decide({ ...windowFor(7, 'stdout'), text: 'é', nextOffset: 2 }, { maxBytes: 2 })).toMatchObject({ kind: 'ok', nextOffset: 2 })
  })
})
