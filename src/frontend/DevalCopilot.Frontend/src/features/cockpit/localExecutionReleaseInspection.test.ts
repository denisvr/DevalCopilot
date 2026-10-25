import { describe, expect, it } from 'vitest'
import { RUN, SEQUENCE, guidOf, indexedStep, operationIdFor, stepIdFor } from './localExecutionInspectionFixture'
import { releaseInspectionTargetOf, toLocalExecutionReleaseInspection } from './localExecutionReleaseInspection'
import {
  RELEASE_ID,
  notRecordedReleaseInspectionFor,
  recordedReleaseInspectionFor,
  recordedVerificationReleaseInspectionFor,
  releaseInspectionReceiptFor,
  releaseInspectionTargetFor,
  unavailableReleaseInspectionFor,
  verificationReleaseInspectionReceiptFor,
} from './localExecutionReleaseInspectionFixture'

const AGENT = releaseInspectionTargetFor(0)
const VERIFICATION = releaseInspectionTargetFor(1)

const decode = (raw: unknown, target = AGENT) => toLocalExecutionReleaseInspection(raw, target)
const recordedWith = (receipt: Record<string, unknown>) => decode(recordedReleaseInspectionFor({}, receipt))
const verificationWith = (receipt: Record<string, unknown>) => decode(recordedVerificationReleaseInspectionFor({}, receipt), VERIFICATION)
const withoutKey = (raw: Record<string, unknown>, key: string) => Object.fromEntries(Object.entries(raw).filter(([name]) => name !== key))

describe('releaseInspectionTargetOf', () => {
  it('describes a proved linked step by the accepted index and nothing else', () => {
    expect(AGENT).toEqual({
      runId: RUN,
      stepId: stepIdFor(0),
      sequenceId: SEQUENCE,
      admissionMode: 'Immediate',
      stepKind: 'Implementation',
      ordinal: 0,
      passNumber: 0,
      operationFamily: 'Agent',
      operationId: operationIdFor(0),
      sourceCheckpointId: expect.any(String),
      sourceFingerprintSha256: 'a'.repeat(64),
    })
    expect(VERIFICATION).toMatchObject({ operationFamily: 'Verification', stepKind: 'Verification', passNumber: 1 })
  })

  it('offers no target for a step that names no operation', () => {
    expect(releaseInspectionTargetOf(RUN, { sequenceId: SEQUENCE, admissionMode: 'Immediate' }, indexedStep(1, { operationId: null }))).toBeNull()
  })

  it('names the queued admission mode of the accepted header', () => {
    expect(releaseInspectionTargetOf(RUN, { sequenceId: SEQUENCE, admissionMode: 'WaitForAdmission' }, indexedStep(2))?.admissionMode).toBe('WaitForAdmission')
  })
})

describe('toLocalExecutionReleaseInspection', () => {
  describe('acceptance', () => {
    it('accepts a Recorded Agent receipt exactly, with the closed members and no extra member', () => {
      const accepted = recordedWith({})

      expect(accepted).toEqual({ status: 'Recorded', runId: RUN, stepId: stepIdFor(0), receipt: releaseInspectionReceiptFor() })
      expect(Object.keys(accepted?.status === 'Recorded' ? accepted.receipt : {}).sort()).toEqual(Object.keys(releaseInspectionReceiptFor()).sort())
    })

    it('accepts a Recorded verification receipt, a cancelled one and a SourceChanged one', () => {
      expect(verificationWith({})?.status).toBe('Recorded')
      expect(verificationWith({ processOutcome: 'Cancelled', operationStatus: 'Cancelled' })?.status).toBe('Recorded')
      expect(verificationWith({ operationStatus: 'SourceChanged' })?.status).toBe('Recorded')
      expect(verificationWith({ processOutcome: 'Cancelled', operationStatus: 'SourceChanged' })?.status).toBe('Recorded')
      expect(recordedWith({ processOutcome: 'Cancelled' })?.status).toBe('Recorded')
    })

    it('accepts every sequence state that retains a quarantine and both admission modes for a matching target', () => {
      for (const sequenceState of ['Stopped', 'NeedsAttention', 'Interrupted']) {
        expect(recordedWith({ sequenceState })?.status).toBe('Recorded')
      }

      const queued = releaseInspectionTargetFor(0, { admissionMode: 'WaitForAdmission' })
      expect(decode(recordedReleaseInspectionFor({}, { admissionMode: 'WaitForAdmission' }), queued)?.status).toBe('Recorded')
    })

    it('accepts NotRecorded and Unavailable as themselves with an explicit null receipt', () => {
      expect(decode(notRecordedReleaseInspectionFor(0))).toEqual({ status: 'NotRecorded', runId: RUN, stepId: stepIdFor(0) })
      expect(decode(unavailableReleaseInspectionFor(0))).toEqual({ status: 'Unavailable', runId: RUN, stepId: stepIdFor(0) })
    })

    it('accepts the boundary values: a zero duration and elapsed time, the extreme Int32 exit codes and the largest Int64 text', () => {
      expect(recordedWith({ processDurationTicks: '0', observedElapsedTicks: '0' })?.status).toBe('Recorded')
      expect(recordedWith({ physicalExitCode: -2147483648 })?.status).toBe('Recorded')
      expect(recordedWith({ physicalExitCode: 2147483647 })?.status).toBe('Recorded')
      expect(recordedWith({ processDurationTicks: '9223372036854775807', observedElapsedTicks: '9223372036854775807' })?.status).toBe('Recorded')
      expect(recordedWith({ eventSequence: '9223372036854775807' })?.status).toBe('Recorded')
      expect(recordedWith({ observedElapsedTicks: '50000000' })?.status).toBe('Recorded')
    })
  })

  describe('the whole answer or nothing', () => {
    it.each([undefined, null, 'x', 7, [], [recordedReleaseInspectionFor()]])('refuses a non-object answer %#', (raw) => {
      expect(decode(raw)).toBeNull()
    })

    it('refuses an answer with an extra, missing or renamed member', () => {
      expect(decode({ ...recordedReleaseInspectionFor(), extra: 1 })).toBeNull()
      for (const key of ['runId', 'stepId', 'status', 'receipt']) {
        expect(decode(withoutKey(recordedReleaseInspectionFor(), key))).toBeNull()
      }

      expect(decode(withoutKey(notRecordedReleaseInspectionFor(0), 'receipt'))).toBeNull()
    })

    it('refuses a receipt with an extra, missing or undefined member', () => {
      expect(recordedWith({ extra: 1 })).toBeNull()
      expect(recordedWith({ confirmationSha256: 'a'.repeat(64) })).toBeNull()
      for (const key of Object.keys(releaseInspectionReceiptFor())) {
        expect(decode({ ...recordedReleaseInspectionFor(), receipt: withoutKey(releaseInspectionReceiptFor(), key) })).toBeNull()
      }

      expect(recordedWith({ semanticExitCode: undefined })).toBeNull()
    })

    it('refuses a receipt that is not an object', () => {
      for (const receipt of [null, undefined, 'x', 7, [], [releaseInspectionReceiptFor()]]) {
        expect(decode({ ...recordedReleaseInspectionFor(), receipt })).toBeNull()
      }
    })

    it('refuses an unknown status and a receipt beside any status but Recorded', () => {
      expect(decode({ ...recordedReleaseInspectionFor(), status: 'Released' })).toBeNull()
      expect(decode({ ...recordedReleaseInspectionFor(), status: 'recorded' })).toBeNull()
      expect(decode({ ...notRecordedReleaseInspectionFor(0), receipt: releaseInspectionReceiptFor() })).toBeNull()
      expect(decode({ ...unavailableReleaseInspectionFor(0), receipt: releaseInspectionReceiptFor() })).toBeNull()
      expect(decode({ ...notRecordedReleaseInspectionFor(0), receipt: undefined })).toBeNull()
      expect(decode({ ...recordedReleaseInspectionFor(), receipt: null })).toBeNull()
    })
  })

  describe('the answer belongs to exactly the asked run and step', () => {
    it('refuses another run or another step', () => {
      expect(decode(recordedReleaseInspectionFor({ runId: guidOf('r', 2) }))).toBeNull()
      expect(decode(recordedReleaseInspectionFor({ stepId: stepIdFor(1) }))).toBeNull()
      expect(decode(notRecordedReleaseInspectionFor(0, { runId: guidOf('r', 2) }))).toBeNull()
      expect(decode(unavailableReleaseInspectionFor(1))).toBeNull()
    })
  })

  describe('a Recorded receipt names exactly the selected operation', () => {
    it.each([
      ['sequenceId', { sequenceId: guidOf('s', 2) }],
      ['admissionMode', { admissionMode: 'WaitForAdmission' }],
      ['stepKind', { stepKind: 'CodeReview', operationFamily: 'Agent', passNumber: 1 }],
      ['ordinal', { ordinal: 1 }],
      ['passNumber', { passNumber: 1 }],
      ['operationFamily', { stepKind: 'Verification', operationFamily: 'Verification', passNumber: 1, operationStatus: 'TimedOut' }],
      ['operationId', { operationId: guidOf('o', 99) }],
      ['sourceCheckpointId', { sourceCheckpointId: guidOf('c', 2) }],
      ['sourceFingerprintSha256', { sourceFingerprintSha256: 'b'.repeat(64) }],
    ])('refuses a well-formed receipt of another %s', (_name, patch) => {
      expect(recordedWith(patch)).toBeNull()
    })

    it('refuses a verification receipt of another recipe operation and a receipt judged against another family', () => {
      expect(verificationWith({ operationId: operationIdFor(2) })).toBeNull()
      expect(decode(recordedVerificationReleaseInspectionFor(), AGENT)).toBeNull()
      expect(decode(recordedReleaseInspectionFor(), VERIFICATION)).toBeNull()
    })
  })

  describe('closed vocabularies and the structural rules', () => {
    it.each([
      ['an unknown sequence state', { sequenceState: 'Armed' }],
      ['a live sequence state', { sequenceState: 'AwaitingStepResult' }],
      ['a lowercase sequence state', { sequenceState: 'stopped' }],
      ['an unknown admission mode', { admissionMode: 'Queued' }],
      ['an unknown step kind', { stepKind: 'Deploy' }],
      ['an unknown family', { operationFamily: 'Process' }],
      ['a clean exit outcome', { processOutcome: 'Exited' }],
      ['an unknown process outcome', { processOutcome: 'Killed' }],
      ['a failed process outcome', { processOutcome: 'Failed' }],
      ['an Agent status of Completed', { operationStatus: 'Completed' }],
      ['an Agent status of Interrupted', { operationStatus: 'Interrupted' }],
      ['an Agent status of Running', { operationStatus: 'Running' }],
      ['an Agent verification status', { operationStatus: 'TimedOut' }],
      ['an empty status', { operationStatus: '' }],
      ['a numeric status', { operationStatus: 1 }],
    ])('refuses %s', (_name, patch) => {
      expect(recordedWith(patch)).toBeNull()
    })

    it.each([
      ['a Passed verification', { operationStatus: 'Passed' }],
      ['a Failed verification', { operationStatus: 'Failed' }],
      ['a Running verification', { operationStatus: 'Running' }],
      ['a Cancelled status for a timed-out process', { operationStatus: 'Cancelled' }],
      ['a TimedOut status for a cancelled process', { processOutcome: 'Cancelled', operationStatus: 'TimedOut' }],
      ['an Interrupted verification', { operationStatus: 'Interrupted' }],
      ['a dispatch refusal', { operationStatus: 'LocalExecutionSequenceDispatchRefused' }],
    ])('refuses %s', (_name, patch) => {
      expect(verificationWith(patch)).toBeNull()
    })

    it('refuses a family that disagrees with the step kind in either direction', () => {
      expect(recordedWith({ operationFamily: 'Verification', operationStatus: 'TimedOut' })).toBeNull()
      expect(verificationWith({ operationFamily: 'Agent', operationStatus: 'Failed' })).toBeNull()
    })

    it('refuses a semantic exit code, zero included: a stopped process has none and the physical code never stands in for it', () => {
      for (const semanticExitCode of [0, 1, -1, 137, '0']) {
        expect(recordedWith({ semanticExitCode })).toBeNull()
      }
    })

    it('refuses an implementation with a correction pass and a later step on pass zero', () => {
      expect(recordedWith({ passNumber: 1 })).toBeNull()
      expect(decode(recordedReleaseInspectionFor({}, { stepKind: 'CodeReview', passNumber: 0, ordinal: 2 }), releaseInspectionTargetFor(2, { passNumber: 0 }))).toBeNull()
    })
  })

  describe('numbers and counters', () => {
    it.each([-1, 0.5, '0', null, 102, 1e9, Number.NaN, Number.POSITIVE_INFINITY])('refuses the ordinal %s', (ordinal) => {
      expect(recordedWith({ ordinal })).toBeNull()
    })

    it.each([-1, 0.5, '1', 4, null, 1e9])('refuses the pass number %s on a correction target', (passNumber) => {
      expect(decode(recordedReleaseInspectionFor({}, { stepKind: 'CodeReview', ordinal: 2, passNumber }), releaseInspectionTargetFor(2))).toBeNull()
    })

    it.each([2147483648, -2147483649, 1.5, '137', null, Number.NaN, 1e12])('refuses the physical exit code %s', (physicalExitCode) => {
      expect(recordedWith({ physicalExitCode })).toBeNull()
    })

    it.each(['', '01', '-1', '+1', '1.0', '1e3', ' 1', '1 ', '9223372036854775808', 'x', 1, null])(
      'refuses the duration, elapsed and sequence text %j',
      (text) => {
        expect(recordedWith({ processDurationTicks: text })).toBeNull()
        expect(recordedWith({ observedElapsedTicks: text })).toBeNull()
        expect(recordedWith({ eventSequence: text })).toBeNull()
      },
    )

    it('refuses a zero event sequence, a negative one and a numeric one: the Human event number is a canonical positive Int64 text', () => {
      expect(recordedWith({ eventSequence: '0' })).toBeNull()
      expect(recordedWith({ eventSequence: -1 })).toBeNull()
      expect(recordedWith({ eventSequence: 42 })).toBeNull()
    })

    it('refuses an observation that outlasts the process it observed, by one tick', () => {
      expect(recordedWith({ processDurationTicks: '100', observedElapsedTicks: '101' })).toBeNull()
      expect(recordedWith({ processDurationTicks: '100', observedElapsedTicks: '100' })?.status).toBe('Recorded')
    })
  })

  describe('identities and digests', () => {
    it.each([
      'releaseId',
      'sequenceId',
      'operationId',
      'sourceCheckpointId',
      'witnessId',
      'observationId',
    ])('refuses a malformed, uppercase or zero %s', (key) => {
      for (const value of ['', 'not-a-guid', RELEASE_ID.toUpperCase(), '00000000-0000-0000-0000-000000000000', 7, null, `${RELEASE_ID} `]) {
        expect(recordedWith({ [key]: value })).toBeNull()
      }
    })

    it('refuses a source fingerprint that is not a lowercase SHA-256', () => {
      for (const value of ['', 'a'.repeat(63), 'a'.repeat(65), 'A'.repeat(64), 'g'.repeat(64), null, 7]) {
        expect(recordedWith({ sourceFingerprintSha256: value })).toBeNull()
      }
    })
  })

  describe('times and chronology', () => {
    const times = ['releasedAtUtc', 'dispatchedAtUtc', 'recordedAtUtc'] as const

    it.each(times)('refuses a %s that is not a canonical UTC round-trip text', (key) => {
      for (const value of ['', '2026-10-20T09:00:00Z', '2026-10-20 09:00:00.0000000Z', '2026-10-20T09:00:00.0000000+00:00', 'tomorrow', 1760950800000, null]) {
        expect(recordedWith({ [key]: value })).toBeNull()
      }
    })

    it('requires dispatch, then the observation, then the Human release, and accepts equal instants', () => {
      expect(recordedWith({ dispatchedAtUtc: '2026-10-20T09:06:00.2000000Z' })).toBeNull()
      expect(recordedWith({ recordedAtUtc: '2026-10-20T09:06:00.2000000Z' })).toBeNull()
      expect(recordedWith({ releasedAtUtc: '2026-10-20T09:05:30.1234566Z' })).toBeNull()
      expect(
        recordedWith({
          dispatchedAtUtc: '2026-10-20T09:05:00.0000000Z',
          recordedAtUtc: '2026-10-20T09:05:00.0000000Z',
          releasedAtUtc: '2026-10-20T09:05:00.0000000Z',
        })?.status,
      ).toBe('Recorded')
    })

    it('refuses an impossible calendar instant even when it has the canonical shape', () => {
      expect(recordedWith({ releasedAtUtc: '2026-02-30T09:06:00.1234567Z' })).toBeNull()
      expect(recordedWith({ dispatchedAtUtc: '2026-13-20T09:00:30.1234567Z' })).toBeNull()
    })
  })

  it('never returns the supplied object: the accepted receipt is a fresh closed copy', () => {
    const raw = recordedReleaseInspectionFor()
    const accepted = decode(raw)

    expect(accepted?.status === 'Recorded' ? accepted.receipt : null).not.toBe(raw.receipt)
    expect(verificationReleaseInspectionReceiptFor()).not.toBe(verificationReleaseInspectionReceiptFor())
  })
})
