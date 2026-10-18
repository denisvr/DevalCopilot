import { describe, expect, it } from 'vitest'
import { decideVerificationWindowAnswer, supportsVerificationEvidence, toAgentAttemptVerificationEvidence } from './agentAttemptVerification'
import type { AgentAttemptVerificationExpectation } from './agentAttemptVerification'
import {
  artifactIdFor,
  notRecordedStream,
  recordedStream,
  recordedVerificationFor,
  refusedVerificationWindow,
  unavailableStream,
  unavailableVerificationFor,
  verificationExpectationFor,
  verificationMemberFor,
  verificationWindowAnswer,
  verificationWindowRequest,
} from './agentAttemptVerificationFixture'

const RUN = 'run-1'
const expected = verificationExpectationFor(RUN, 2)
const diagnosisExpected = verificationExpectationFor(RUN, 2, 'VerificationDiagnosis')
const decode = (raw: unknown, expectation: AgentAttemptVerificationExpectation = expected) => toAgentAttemptVerificationEvidence(raw, expectation)

/** An answer whose first member is replaced by a patched copy. */
const withMember = (patch: Record<string, unknown>, statuses: readonly ('Passed' | 'Failed')[] = ['Passed']) => {
  const answer = recordedVerificationFor(RUN, 2, statuses)
  return { ...answer, members: [{ ...answer.members[0], ...patch }, ...answer.members.slice(1)] }
}

describe('toAgentAttemptVerificationEvidence', () => {
  it('accepts an ordinary review with one passed member and keeps every raw fact', () => {
    const accepted = decode(recordedVerificationFor(RUN, 2))

    expect(accepted).toEqual({
      status: 'Recorded',
      checkpoint: { id: 'run-1-checkpoint', checkpointNumber: 3 },
      members: [
        {
          sequence: 0,
          commandId: 'run-1-attempt-2-command-0',
          executionId: 'run-1-attempt-2-execution-0',
          executionNumber: 1,
          commandName: 'Command 1',
          status: 'Passed',
          exitCode: 0,
          claimedAtUtc: '2026-10-09T10:00:00.1234567Z',
          dispatchedAtUtc: '2026-10-09T10:00:01.1234567Z',
          completedAtUtc: '2026-10-09T10:00:02.1234567Z',
          standardOutput: {
            status: 'Recorded',
            artifactId: 'run-1-attempt-2-artifact-0-stdout',
            byteLength: 120,
            captureOutcome: 'CapturedWithKnownTruncation',
            truncated: false,
          },
          standardError: { status: 'NotRecorded' },
        },
      ],
    })
  })

  it('accepts a diagnosis with failed and passed members in recorded order', () => {
    const accepted = decode(recordedVerificationFor(RUN, 2, ['Passed', 'Failed', 'Failed']), diagnosisExpected)

    expect(accepted?.status === 'Recorded' && accepted.members.map((member) => [member.status, member.exitCode])).toEqual([
      ['Passed', 0],
      ['Failed', 1],
      ['Failed', 1],
    ])
  })

  it('accepts thirty-two members and refuses a count of thirty-three, zero or one that does not equal the members', () => {
    expect(decode(recordedVerificationFor(RUN, 2, Array(32).fill('Passed')))).not.toBeNull()
    expect(decode(recordedVerificationFor(RUN, 2, Array(33).fill('Passed')))).toBeNull()
    expect(decode(recordedVerificationFor(RUN, 2, [], 'ImplementationReview'))).toBeNull()
    for (const memberTotalCount of [2, 0, -1, 1.5, '1', null, undefined]) {
      expect(decode(recordedVerificationFor(RUN, 2, ['Passed'], 'ImplementationReview', { memberTotalCount }))).toBeNull()
    }
  })

  it('accepts Unavailable only with no attempt, checkpoint, count or member', () => {
    expect(decode(unavailableVerificationFor(RUN, 2))).toEqual({ status: 'Unavailable' })
    for (const patch of [
      { attempt: verificationExpectationFor(RUN, 2) },
      { checkpoint: { id: 'c', checkpointNumber: 1 } },
      { memberTotalCount: 0 },
      { members: [verificationMemberFor(RUN, 2, 0)] },
      { attempt: undefined },
    ]) {
      expect(decode(unavailableVerificationFor(RUN, 2, patch))).toBeNull()
    }
  })

  it.each([
    ['another run', { runId: 'run-2' }],
    ['another attempt', { attemptId: 'run-1-attempt-3' }],
    ['no status', { status: undefined }],
    ['an unknown status', { status: 'Empty' }],
    ['members that are not a list', { members: {} }],
    ['an extra root field', { extra: true }],
  ])('refuses %s', (_, patch) => {
    expect(decode({ ...recordedVerificationFor(RUN, 2), ...patch })).toBeNull()
    expect(decode({ ...unavailableVerificationFor(RUN, 2), ...patch })).toBeNull()
  })

  it.each([null, undefined, 'Recorded', 7, [], [recordedVerificationFor(RUN, 2)]])('refuses a body that is %j', (body) => {
    expect(decode(body)).toBeNull()
  })

  it('refuses a missing root field and the wrong header identity, number, role, provider or contract', () => {
    const { checkpoint: _checkpoint, ...withoutCheckpoint } = recordedVerificationFor(RUN, 2)
    expect(decode(withoutCheckpoint)).toBeNull()
    for (const patch of [
      { id: 'run-1-attempt-9' },
      { attemptNumber: 3 },
      { role: 'Planner' },
      { provider: 'ClaudeCode' },
      { responseContract: 'VerificationDiagnosis' },
      { extra: 1 },
    ]) {
      const answer = recordedVerificationFor(RUN, 2)
      expect(decode({ ...answer, attempt: { ...answer.attempt, ...patch } })).toBeNull()
    }

    expect(decode({ ...recordedVerificationFor(RUN, 2), attempt: null })).toBeNull()
  })

  it('serves Recorded only for the two supported CodeReviewer contracts', () => {
    const planner = { ...expected, role: 'Planner' as const, responseContract: 'Proposal' as const }
    expect(supportsVerificationEvidence(expected)).toBe(true)
    expect(supportsVerificationEvidence(diagnosisExpected)).toBe(true)
    expect(supportsVerificationEvidence(planner)).toBe(false)
    expect(supportsVerificationEvidence({ ...expected, responseContract: 'ReviewCorrection' })).toBe(false)
    expect(decode(recordedVerificationFor(RUN, 2), planner)).toBeNull()
  })

  it.each([
    ['a blank checkpoint id', { id: '  ', checkpointNumber: 1 }],
    ['a zero checkpoint number', { id: 'c', checkpointNumber: 0 }],
    ['a fractional checkpoint number', { id: 'c', checkpointNumber: 1.5 }],
    ['a checkpoint number past Int32', { id: 'c', checkpointNumber: 2 ** 31 }],
    ['a textual checkpoint number', { id: 'c', checkpointNumber: '1' }],
    ['an extra checkpoint field', { id: 'c', checkpointNumber: 1, latest: true }],
    ['a missing checkpoint number', { id: 'c' }],
    ['no checkpoint', null],
  ])('refuses %s', (_, checkpoint) => {
    expect(decode(recordedVerificationFor(RUN, 2, ['Passed'], 'ImplementationReview', { checkpoint }))).toBeNull()
  })

  it.each([
    ['a swapped position', { sequence: 1 }],
    ['a textual position', { sequence: '0' }],
    ['a blank command identity', { commandId: ' ' }],
    ['a missing execution identity', { executionId: undefined }],
    ['a zero execution number', { executionNumber: 0 }],
    ['a fractional execution number', { executionNumber: 1.5 }],
    ['an execution number past Int32', { executionNumber: 2 ** 31 }],
    ['a blank command name', { commandName: '   ' }],
    ['a command name over 200 UTF-16 units', { commandName: 'n'.repeat(201) }],
    ['a non-text command name', { commandName: 7 }],
    ['an unknown status', { status: 'Cancelled' }],
    ['Passed with a nonzero exit code', { exitCode: 1 }],
    ['a fractional exit code', { exitCode: 0.5 }],
    ['a textual exit code', { exitCode: '0' }],
    ['an exit code past Int32', { status: 'Failed', exitCode: 2 ** 31 }],
    ['a missing exit code', { exitCode: undefined }],
    ['a member field that is not part of the contract', { snapshotSha256: 'x' }],
    ['a missing time', { completedAtUtc: undefined }],
  ])('refuses a member with %s', (_, patch) => {
    expect(decode(withMember(patch))).toBeNull()
  })

  it('refuses Failed with exit code 0 and accepts a negative or maximal nonzero exit code', () => {
    expect(decode(withMember({ status: 'Failed', exitCode: 0 }, ['Failed']), diagnosisExpected)).toBeNull()
    expect(decode(withMember({ exitCode: -1 }, ['Failed']), diagnosisExpected)).not.toBeNull()
    expect(decode(withMember({ exitCode: -(2 ** 31) }, ['Failed']), diagnosisExpected)).not.toBeNull()
    expect(decode(withMember({ exitCode: 2 ** 31 - 1 }, ['Failed']), diagnosisExpected)).not.toBeNull()
    expect(decode(withMember({ commandName: 'n'.repeat(200) }))).not.toBeNull()
  })

  it.each([
    ['fewer than seven fractional digits', '2026-10-09T10:00:00.123Z'],
    ['an offset instead of Z', '2026-10-09T10:00:00.1234567+00:00'],
    ['a lowercase separator', '2026-10-09t10:00:00.1234567Z'],
    ['an impossible calendar day', '2026-02-30T10:00:00.1234567Z'],
    ['no zone', '2026-10-09T10:00:00.1234567'],
    ['a bare date', '2026-10-09'],
    ['a padded text', ' 2026-10-09T10:00:00.1234567Z'],
    ['a number', 1_790_000_000_000],
    ['null', null],
  ])('refuses a timestamp with %s', (_, value) => {
    for (const key of ['claimedAtUtc', 'dispatchedAtUtc', 'completedAtUtc']) {
      expect(decode(withMember({ [key]: value }))).toBeNull()
    }
  })

  it('refuses duplicated command, execution or artifact identities and an ordinary review with a failed member', () => {
    const two = recordedVerificationFor(RUN, 2, ['Passed', 'Passed'])
    const second = (patch: Record<string, unknown>) => ({ ...two, members: [two.members[0], { ...two.members[1], ...patch }] })

    expect(decode(two)).not.toBeNull()
    expect(decode(second({ commandId: two.members[0].commandId }))).toBeNull()
    expect(decode(second({ executionId: two.members[0].executionId }))).toBeNull()
    expect(decode(second({ standardOutput: two.members[0].standardOutput }))).toBeNull()
    expect(decode(second({ standardError: two.members[0].standardOutput }))).toBeNull()
    expect(decode(recordedVerificationFor(RUN, 2, ['Passed', 'Failed'], 'ImplementationReview'))).toBeNull()
  })

  it('refuses a diagnosis that has no failed member', () => {
    expect(decode(recordedVerificationFor(RUN, 2, ['Passed', 'Passed'], 'VerificationDiagnosis'), diagnosisExpected)).toBeNull()
  })

  it('accepts every truthful stream descriptor and keeps false, true and unknown truncation distinct', () => {
    const accepted = decode(
      withMember(
        {
          standardOutput: recordedStream(artifactIdFor(RUN, 2, 0, 'stdout'), 0, true),
          standardError: recordedStream(artifactIdFor(RUN, 2, 0, 'stderr'), 7, null),
        },
        ['Passed'],
      ),
    )
    const member = accepted?.status === 'Recorded' ? accepted.members[0] : null
    expect(member?.standardOutput).toMatchObject({ status: 'Recorded', byteLength: 0, truncated: true })
    expect(member?.standardError).toMatchObject({ status: 'Recorded', byteLength: 7, captureOutcome: 'RecoveredAfterHostInterruption', truncated: null })

    const sides = [
      [notRecordedStream(), unavailableStream()],
      [unavailableStream(), recordedStream('a-1', 3, false)],
    ]
    for (const [output, error] of sides) {
      expect(decode(withMember({ standardOutput: output, standardError: error }))).not.toBeNull()
    }
  })

  it.each([
    ['NotRecorded with an identity', { ...notRecordedStream(), artifactId: 'a' }],
    ['NotRecorded with a length', { ...notRecordedStream(), byteLength: 0 }],
    ['Unavailable with a truncation', { ...unavailableStream(), truncated: false }],
    ['Unavailable with a capture outcome', { ...unavailableStream(), captureOutcome: 'CapturedWithKnownTruncation' }],
    ['an unknown descriptor status', { ...notRecordedStream(), status: 'Missing' }],
    ['Recorded with a blank identity', { ...recordedStream('a', 1), artifactId: ' ' }],
    ['Recorded with a missing identity', { ...recordedStream('a', 1), artifactId: null }],
    ['Recorded with a negative length', { ...recordedStream('a', 1), byteLength: -1 }],
    ['Recorded with a fractional length', { ...recordedStream('a', 1), byteLength: 1.5 }],
    ['Recorded with a length past the safe integer', { ...recordedStream('a', 1), byteLength: 2 ** 53 }],
    ['Recorded with a textual length', { ...recordedStream('a', 1), byteLength: '1' }],
    ['Recorded with a null length', { ...recordedStream('a', 1), byteLength: null }],
    ['a known capture with unknown truncation', { ...recordedStream('a', 1), truncated: null }],
    ['a known capture with a textual truncation', { ...recordedStream('a', 1), truncated: 'false' }],
    ['a recovered capture with a known truncation', { ...recordedStream('a', 1, null), truncated: false }],
    ['an unknown capture outcome', { ...recordedStream('a', 1), captureOutcome: 'Captured' }],
    ['an undeclared field', { ...recordedStream('a', 1), path: 'x' }],
    ['a missing field', { status: 'NotRecorded' }],
    ['null', null],
  ])('refuses a stream descriptor with %s', (_, descriptor) => {
    expect(decode(withMember({ standardOutput: descriptor }))).toBeNull()
    expect(decode(withMember({ standardError: descriptor }))).toBeNull()
  })
})

describe('decideVerificationWindowAnswer', () => {
  const request = verificationWindowRequest()
  const ok = (text: string, fromOffset = 0, patch: Record<string, unknown> = {}, facts = request) =>
    decideVerificationWindowAnswer(verificationWindowAnswer(facts, text, fromOffset, patch), facts)

  it('accepts a window of the requested member and stream and reports its cursor and total', () => {
    expect(ok('a'.repeat(100))).toEqual({ kind: 'ok', text: 'a'.repeat(100), nextOffset: 100, totalLength: 120 })
    expect(ok('b'.repeat(20), 100, {}, { ...request, fromOffset: 100 })).toEqual({
      kind: 'ok',
      text: 'b'.repeat(20),
      nextOffset: 120,
      totalLength: 120,
    })
  })

  it('accepts an empty output read at offset zero and a read exactly at the end', () => {
    const empty = verificationWindowRequest({ descriptor: recordedStream('a', 0) as never })
    expect(decideVerificationWindowAnswer(verificationWindowAnswer(empty, ''), empty)).toEqual({
      kind: 'ok',
      text: '',
      nextOffset: 0,
      totalLength: 0,
    })
    const atEnd = { ...request, fromOffset: 120 }
    expect(ok('', 120, {}, atEnd)).toEqual({ kind: 'ok', text: '', nextOffset: 120, totalLength: 120 })
  })

  it('trusts the byte cursor for multibyte text instead of re-encoding the decoded text', () => {
    // "é" is two bytes and "𝄞" four, but also two UTF-16 units: the text is never shorter than one unit per consumed byte.
    expect(ok('é𝄞', 0, { nextOffset: 6 })).toMatchObject({ kind: 'ok', nextOffset: 6 })
    // Replacement decoding of three malformed bytes can produce three units from three bytes.
    expect(ok('���', 0, { nextOffset: 3 })).toMatchObject({ kind: 'ok' })
    expect(ok('abcd', 0, { nextOffset: 3 })).toEqual({ kind: 'incoherent' })
  })

  it.each([
    ['another run', { runId: 'run-9' }],
    ['another attempt', { attemptId: 'attempt-9' }],
    ['another member', { executionId: 'execution-9' }],
    ['the other stream', { stream: 'stderr' }],
    ['a noncanonical stream', { stream: 'STDOUT' }],
    ['an undeclared field', { path: 'x' }],
    ['no status', { status: undefined }],
    ['an unknown status', { status: 'NoAgentEvidence' }],
    ['a non-text body', { text: 5 }],
    ['a null text', { text: null }],
  ])('refuses an answer for %s', (_, patch) => {
    expect(ok('abc', 0, patch)).toEqual({ kind: 'incoherent' })
  })

  it.each([null, undefined, 'Ok', 5, [], [verificationWindowAnswer(request, 'abc')]])('refuses a body that is %j', (body) => {
    expect(decideVerificationWindowAnswer(body, request)).toEqual({ kind: 'incoherent' })
  })

  it.each([
    ['the descriptor missing', { artifact: null }],
    ['another artifact identity', { artifact: { ...request.descriptor, artifactId: 'other' } }],
    ['another recorded length', { artifact: { ...request.descriptor, byteLength: 121 } }],
    ['another capture outcome', { artifact: { ...request.descriptor, captureOutcome: 'RecoveredAfterHostInterruption', truncated: null } }],
    ['another truncation', { artifact: { ...request.descriptor, truncated: true } }],
    ['a NotRecorded descriptor', { artifact: notRecordedStream() }],
    ['a total that is not the recorded length', { totalLength: 119 }],
    ['a cursor before the request', { nextOffset: -1 }],
    ['a cursor past the total', { nextOffset: 121 }],
    ['a fractional cursor', { nextOffset: 3.5 }],
    ['an unsafe cursor', { nextOffset: 2 ** 53 }],
    ['a textual cursor', { nextOffset: '3' }],
    ['a missing total', { totalLength: undefined }],
  ])('refuses an Ok window with %s', (_, patch) => {
    expect(ok('abc', 0, patch)).toEqual({ kind: 'incoherent' })
  })

  it('binds the advance to the cap, to progress while bytes remain and to the text', () => {
    const capped = { ...request, maxBytes: 64 }
    expect(ok('a'.repeat(64), 0, {}, capped)).toMatchObject({ kind: 'ok' })
    expect(ok('a'.repeat(65), 0, {}, capped)).toEqual({ kind: 'incoherent' })
    expect(ok('', 0, { nextOffset: 0 })).toEqual({ kind: 'incoherent' })
    expect(ok('', 0, { nextOffset: 5 })).toEqual({ kind: 'incoherent' })
    expect(ok('abc', 0, { nextOffset: 0 })).toEqual({ kind: 'incoherent' })
    expect(ok('abcd', 0, { nextOffset: 3 })).toEqual({ kind: 'incoherent' })
  })

  it('decides each closed non-Ok status only when it carries nothing else', () => {
    const refused = (status: 'Unavailable' | 'NotRecorded' | 'Missing' | 'IntegrityMismatch', fromOffset = 7, patch = {}) =>
      decideVerificationWindowAnswer(refusedVerificationWindow(request, status, fromOffset, patch), { ...request, fromOffset })

    expect(refused('Unavailable')).toEqual({ kind: 'unavailable' })
    expect(refused('NotRecorded')).toEqual({ kind: 'not-recorded' })
    expect(refused('Missing')).toEqual({ kind: 'missing' })
    expect(refused('IntegrityMismatch')).toEqual({ kind: 'integrity-mismatch' })
    for (const patch of [
      { artifact: request.descriptor },
      { text: 'leaked' },
      { nextOffset: 8 },
      { totalLength: 120 },
      { totalLength: 1 },
    ]) {
      expect(refused('IntegrityMismatch', 7, patch)).toEqual({ kind: 'incoherent' })
      expect(refused('Missing', 7, patch)).toEqual({ kind: 'incoherent' })
    }
  })
})
