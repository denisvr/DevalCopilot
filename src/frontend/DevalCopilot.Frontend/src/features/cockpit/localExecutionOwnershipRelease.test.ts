import { describe, expect, it } from 'vitest'
import { releaseTargetsOf, toLocalExecutionOwnershipRelease } from './localExecutionOwnershipRelease'
import {
  RELEASE_OBSERVATION_ID,
  RELEASE_OPERATION_ID,
  RELEASE_RUN,
  RELEASE_STEP_ID,
  availableReleaseFor,
  notEligibleReleaseFor,
  releasableSequenceFor,
  releaseTargetFor,
  releasedReleaseFor,
  unavailableReleaseFor,
} from './localExecutionOwnershipReleaseFixture'
import { toLocalExecutionSequenceStatus } from './localExecutionSequence'
import { localGuid, localStatusFor, localStepFor } from './localExecutionSequenceFixture'

const target = releaseTargetFor()
const decode = (raw: unknown) => toLocalExecutionOwnershipRelease(raw, target)

describe('toLocalExecutionOwnershipRelease', () => {
  it('accepts the available candidate for exactly the chosen operation', () => {
    const answer = decode(availableReleaseFor())
    expect(answer).toMatchObject({ status: 'Available', runId: RELEASE_RUN, stepId: RELEASE_STEP_ID })
    if (answer?.status !== 'Available') {
      throw new Error('expected an Available answer')
    }

    expect(answer.candidate).toMatchObject({
      operationId: RELEASE_OPERATION_ID,
      observationId: RELEASE_OBSERVATION_ID,
      processOutcome: 'TimedOut',
      semanticExitCode: null,
      physicalExitCode: 137,
      confirmationVersion: '1',
    })
  })

  it('keeps the exact tick and time texts the host wrote', () => {
    const answer = decode(availableReleaseFor({}, { observedElapsedTicks: '0', processDurationTicks: '9223372036854775807' }))
    expect(answer).toMatchObject({ candidate: { observedElapsedTicks: '0', processDurationTicks: '9223372036854775807' } })
  })

  it('never offers a clean exit as a candidate: that proof already frees the operation without a Human release', () => {
    expect(decode(availableReleaseFor({}, { processOutcome: 'Exited', semanticExitCode: 3, physicalExitCode: 3 }))).toBeNull()
    expect(decode(availableReleaseFor({}, { processOutcome: 'Exited', semanticExitCode: null }))).toBeNull()
    expect(decode(availableReleaseFor({}, { semanticExitCode: 0 }))).toBeNull()
  })

  const verificationTarget = releaseTargetFor({ stepKind: 'Verification', passNumber: 1, ordinal: 1 })
  const verificationCandidate = (patch: Record<string, unknown>) =>
    availableReleaseFor({}, { stepKind: 'Verification', passNumber: 1, ordinal: 1, operationFamily: 'Verification', operationStatus: 'TimedOut', ...patch })

  it.each([
    ['a timed-out verification', { operationStatus: 'TimedOut', processOutcome: 'TimedOut' }],
    ['a cancelled verification', { operationStatus: 'Cancelled', processOutcome: 'Cancelled' }],
    ['a source-changed verification whose process timed out', { operationStatus: 'SourceChanged', processOutcome: 'TimedOut' }],
    ['a source-changed verification whose process was cancelled', { operationStatus: 'SourceChanged', processOutcome: 'Cancelled' }],
  ])('accepts %s', (_name, patch) => {
    expect(toLocalExecutionOwnershipRelease(verificationCandidate(patch), verificationTarget)).toMatchObject({ status: 'Available' })
  })

  it.each([
    ['Running', 'TimedOut'],
    ['Passed', 'TimedOut'],
    ['Failed', 'TimedOut'],
    ['Interrupted', 'TimedOut'],
    ['Completed', 'TimedOut'],
    ['NotAStatus', 'TimedOut'],
    ['Cancelled', 'TimedOut'],
    ['TimedOut', 'Cancelled'],
    ['LocalExecutionSequenceDispatchRefused', 'TimedOut'],
  ])('refuses a verification candidate with status %s and a %s process', (operationStatus, processOutcome) => {
    expect(toLocalExecutionOwnershipRelease(verificationCandidate({ operationStatus, processOutcome }), verificationTarget)).toBeNull()
  })

  it.each(['Running', 'Passed', 'NotAStatus', 'Completed', 'Interrupted', 'TimedOut', 'SourceChanged', 'failed', ''])(
    'refuses an Agent candidate with operation status %s: only a Failed attempt can carry a timed-out or cancelled process',
    (operationStatus) => {
      expect(decode(availableReleaseFor({}, { operationStatus }))).toBeNull()
    },
  )

  it('accepts a Failed Agent candidate for a timeout and for a cancellation', () => {
    expect(decode(availableReleaseFor({}, { operationStatus: 'Failed', processOutcome: 'TimedOut' }))).toMatchObject({ status: 'Available' })
    expect(decode(availableReleaseFor({}, { operationStatus: 'Failed', processOutcome: 'Cancelled' }))).toMatchObject({ status: 'Available' })
  })

  it('accepts a verification family for a verification step and a closed refusal for the two non-candidate statuses', () => {
    const verification = releaseTargetFor({ stepKind: 'Verification', passNumber: 1, ordinal: 1 })
    expect(
      toLocalExecutionOwnershipRelease(
        availableReleaseFor({}, { stepKind: 'Verification', passNumber: 1, ordinal: 1, operationFamily: 'Verification', operationStatus: 'TimedOut' }),
        verification,
      ),
    ).not.toBeNull()
    for (const reason of ['step_not_linked', 'operation_not_terminal', 'not_dispatched', 'already_free', 'sequence_not_ended']) {
      expect(decode(notEligibleReleaseFor(reason))).toMatchObject({ status: 'NotEligible', reasonCode: reason })
    }

    for (const reason of ['evidence_missing', 'evidence_incoherent']) {
      expect(decode(unavailableReleaseFor(reason))).toMatchObject({ status: 'Unavailable', reasonCode: reason })
    }
  })

  it('accepts the released receipt for exactly the chosen operation', () => {
    expect(decode(releasedReleaseFor())).toMatchObject({ status: 'Released', release: { eventSequence: '42', operationId: RELEASE_OPERATION_ID } })
  })

  const refusals: [string, unknown][] = [
    ['a non-object', 'Available'],
    ['null', null],
    ['an answer with an unknown member', { ...availableReleaseFor(), extra: 1 }],
    ['an answer without a member', (() => { const { release: _omitted, ...rest } = availableReleaseFor(); return rest })()],
    ['another run', availableReleaseFor({ runId: 'run-2' })],
    ['another step', availableReleaseFor({ stepId: localGuid(101) })],
    ['an unknown status', availableReleaseFor({ status: 'Pending' })],
    ['an available answer with a reason', availableReleaseFor({ reasonCode: 'evidence_missing' })],
    ['an available answer with a receipt', availableReleaseFor({ release: { releaseId: localGuid(1) } })],
    ['an available answer without a candidate', availableReleaseFor({ candidate: null })],
    ['a released answer with a candidate', releasedReleaseFor({ candidate: {} })],
    ['a released answer with a reason', releasedReleaseFor({ reasonCode: 'already_free' })],
    ['a released answer without a receipt', releasedReleaseFor({ release: null })],
    ['a not eligible answer without a reason', notEligibleReleaseFor('already_free', { reasonCode: null })],
    ['a not eligible answer with an unavailable reason', notEligibleReleaseFor('evidence_missing')],
    ['an unavailable answer with a not eligible reason', unavailableReleaseFor('already_free')],
    ['a not eligible answer with free text', notEligibleReleaseFor('please retry')],
    ['a not eligible answer with a candidate', notEligibleReleaseFor('already_free', { candidate: {} })],
    ['a candidate with an unknown member', availableReleaseFor({}, { pid: 1 })],
    ['a candidate of another sequence', availableReleaseFor({}, { sequenceId: localGuid(9) })],
    ['a candidate in a live sequence state', availableReleaseFor({}, { sequenceState: 'AwaitingStepResult' })],
    ['a candidate of another sequence state than the status', availableReleaseFor({}, { sequenceState: 'Stopped' })],
    ['a candidate of another admission mode', availableReleaseFor({}, { admissionMode: 'WaitForAdmission' })],
    ['a candidate of another step kind', availableReleaseFor({}, { stepKind: 'CodeReview', operationFamily: 'Agent' })],
    ['a candidate of another ordinal', availableReleaseFor({}, { ordinal: 1 })],
    ['a candidate of another operation', availableReleaseFor({}, { operationId: localGuid(201) })],
    ['a candidate with the wrong family for its kind', availableReleaseFor({}, { operationFamily: 'Verification' })],
    ['a candidate with an unknown process outcome', availableReleaseFor({}, { processOutcome: 'Crashed' })],
    ['a timed-out candidate with a semantic exit code', availableReleaseFor({}, { semanticExitCode: 0 })],
    ['a candidate with a string physical exit code', availableReleaseFor({}, { physicalExitCode: '137' })],
    ['a candidate with an out-of-range physical exit code', availableReleaseFor({}, { physicalExitCode: 2 ** 31 })],
    ['a candidate with a fractional physical exit code', availableReleaseFor({}, { physicalExitCode: 1.5 })],
    ['a candidate with a leading-zero tick text', availableReleaseFor({}, { observedElapsedTicks: '01' })],
    ['a candidate with a negative tick text', availableReleaseFor({}, { observedElapsedTicks: '-1' })],
    ['a candidate with a numeric tick', availableReleaseFor({}, { observedElapsedTicks: 40000 })],
    ['a candidate with an Int64 overflow tick text', availableReleaseFor({}, { processDurationTicks: '9223372036854775808' })],
    ['a candidate observed longer than its process ran', availableReleaseFor({}, { observedElapsedTicks: '50000001' })],
    ['a candidate with a non-canonical time', availableReleaseFor({}, { observedAtUtc: '2026-10-09T10:05:30Z' })],
    ['a candidate observed before it was dispatched', availableReleaseFor({}, { observedAtUtc: '2026-10-09T10:00:29.1234567Z' })],
    ['a candidate whose confirmation expires before the observation', availableReleaseFor({}, { confirmationExpiresAtUtc: '2026-10-09T10:05:00.0000000Z' })],
    ['a candidate with another confirmation version', availableReleaseFor({}, { confirmationVersion: '2' })],
    ['a candidate with a numeric confirmation version', availableReleaseFor({}, { confirmationVersion: 1 })],
    ['a candidate with an upper-case confirmation digest', availableReleaseFor({}, { confirmationSha256: 'AB'.repeat(32) })],
    ['a candidate with a short confirmation digest', availableReleaseFor({}, { confirmationSha256: 'ab' })],
    ['a candidate with an empty observation identity', availableReleaseFor({}, { observationId: '00000000-0000-0000-0000-000000000000' })],
    ['a candidate with a free-text operation status', availableReleaseFor({}, { operationStatus: '<b>Failed</b>' })],
    ['a receipt with an unknown member', releasedReleaseFor({}, { host: 'x' })],
    ['a receipt of another operation', releasedReleaseFor({}, { operationId: localGuid(201) })],
    ['a receipt of another step kind', releasedReleaseFor({}, { stepKind: 'Verification', operationFamily: 'Verification' })],
    ['a receipt with a zero event sequence', releasedReleaseFor({}, { eventSequence: '0' })],
    ['a receipt with a numeric event sequence', releasedReleaseFor({}, { eventSequence: 42 })],
    ['a receipt with a non-canonical time', releasedReleaseFor({}, { releasedAtUtc: 'yesterday' })],
  ]
  it.each(refusals)('refuses %s', (_name, raw) => {
    expect(decode(raw)).toBeNull()
  })
})

describe('releaseTargetsOf', () => {
  const decodeStatus = (raw: unknown) => toLocalExecutionSequenceStatus(raw, RELEASE_RUN)

  it('offers exactly the quarantined and the released steps, each with the facts an answer must match', () => {
    const quarantined = decodeStatus(localStatusFor(RELEASE_RUN, 'NeedsAttention', {}, { steps: [localStepFor(0, 'quarantined')], reasonCode: 'exit_evidence_missing', reasonText: 'x' }))
    expect(releaseTargetsOf(RELEASE_RUN, quarantined!.sequence!)).toEqual([
      expect.objectContaining({ stepId: RELEASE_STEP_ID, operationId: RELEASE_OPERATION_ID, sequenceState: 'NeedsAttention', releasedInStatus: false }),
    ])

    const released = decodeStatus(localStatusFor(RELEASE_RUN, 'NeedsAttention', { manualFence: 'Released' }, releasableSequenceFor(true)))
    expect(releaseTargetsOf(RELEASE_RUN, released!.sequence!)).toEqual([expect.objectContaining({ releasedInStatus: true })])
  })

  it('offers nothing for a sequence without a retained quarantine', () => {
    const ended = decodeStatus(localStatusFor(RELEASE_RUN, 'ReadyForHumanApproval'))
    expect(releaseTargetsOf(RELEASE_RUN, ended!.sequence!)).toEqual([])
    const reserved = decodeStatus(localStatusFor(RELEASE_RUN, 'NeedsAttention', {}, { steps: [localStepFor(0, 'unresolved')], reasonCode: 'preparation_unresolved', reasonText: 'x' }))
    expect(releaseTargetsOf(RELEASE_RUN, reserved!.sequence!)).toEqual([])
  })

  it('a released step leaves the sequence with no outstanding operation and a lifted fence', () => {
    const released = decodeStatus(localStatusFor(RELEASE_RUN, 'NeedsAttention', { manualFence: 'Released' }, releasableSequenceFor(true)))
    expect(released?.sequence?.awaitingReconciliation).toBe(false)
    expect(released?.sequence?.outstandingOperationId).toBeNull()
    expect(released?.manualFence).toBe('Released')
  })
})
