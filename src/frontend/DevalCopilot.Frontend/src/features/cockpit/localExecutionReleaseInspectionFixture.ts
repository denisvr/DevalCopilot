import { releaseInspectionTargetOf } from './localExecutionReleaseInspection'
import type { LocalExecutionReleaseInspectionTarget } from './localExecutionReleaseInspection'
import { CHECKPOINT, FINGERPRINT, RUN, SEQUENCE, guidOf, headerFor, indexedStep, operationIdFor, stepIdFor } from './localExecutionInspectionFixture'
import type { LocalExecutionInspectionHeader } from './localExecutionInspection'

/**
 * Test-only builders of the parsed recorded quarantine-release answers the host writes (ADR-0067). They write exactly the members the
 * contract names with explicit nulls, and every member can be replaced through a patch to build a contradiction. The defaults describe
 * the released implementation (step 0) of the standard inspection fixture; `verification*` describes its verification step (step 1).
 */
type Raw = Record<string, unknown>

export const RELEASE_ID = guidOf('l', 1)
export const WITNESS_ID = guidOf('x', 1)
export const OBSERVATION_ID = guidOf('b', 1)

const header = () => headerFor() as unknown as LocalExecutionInspectionHeader

/** The target the accepted index offers for the proved linked step of `ordinal` (0 implementation, 1 verification, 2 code review). */
export function releaseInspectionTargetFor(ordinal = 0, patch: Partial<LocalExecutionReleaseInspectionTarget> = {}): LocalExecutionReleaseInspectionTarget {
  const target = releaseInspectionTargetOf(RUN, header(), indexedStep(ordinal))
  if (target === null) {
    throw new Error('the fixture step names no operation')
  }

  return { ...target, ...patch }
}

/** The Human release receipt of the implementation step: a timed-out Claude Code attempt the original host observed terminated. */
export const releaseInspectionReceiptFor = (patch: Raw = {}): Raw => ({
  releaseId: RELEASE_ID,
  releasedAtUtc: '2026-10-20T09:06:00.1234567Z',
  eventSequence: '42',
  sequenceId: SEQUENCE,
  sequenceState: 'NeedsAttention',
  admissionMode: 'Immediate',
  stepKind: 'Implementation',
  ordinal: 0,
  passNumber: 0,
  operationFamily: 'Agent',
  operationId: operationIdFor(0),
  sourceCheckpointId: CHECKPOINT,
  sourceFingerprintSha256: FINGERPRINT,
  operationStatus: 'Failed',
  processOutcome: 'TimedOut',
  semanticExitCode: null,
  processDurationTicks: '50000000',
  witnessId: WITNESS_ID,
  observationId: OBSERVATION_ID,
  dispatchedAtUtc: '2026-10-20T09:00:30.1234567Z',
  recordedAtUtc: '2026-10-20T09:05:30.1234567Z',
  physicalExitCode: 137,
  observedElapsedTicks: '40000',
  ...patch,
})

/** The Human release receipt of the verification step: a timed-out recipe run. */
export const verificationReleaseInspectionReceiptFor = (patch: Raw = {}): Raw =>
  releaseInspectionReceiptFor({
    stepKind: 'Verification',
    ordinal: 1,
    passNumber: 1,
    operationFamily: 'Verification',
    operationId: operationIdFor(1),
    operationStatus: 'TimedOut',
    ...patch,
  })

const answer = (ordinal: number, status: string, receipt: Raw | null): Raw => ({
  runId: RUN,
  stepId: stepIdFor(ordinal),
  status,
  receipt,
})

export const recordedReleaseInspectionFor = (patch: Raw = {}, receipt: Raw = {}): Raw => ({
  ...answer(0, 'Recorded', releaseInspectionReceiptFor(receipt)),
  ...patch,
})

export const recordedVerificationReleaseInspectionFor = (patch: Raw = {}, receipt: Raw = {}): Raw => ({
  ...answer(1, 'Recorded', verificationReleaseInspectionReceiptFor(receipt)),
  ...patch,
})

export const notRecordedReleaseInspectionFor = (ordinal = 0, patch: Raw = {}): Raw => ({ ...answer(ordinal, 'NotRecorded', null), ...patch })
export const unavailableReleaseInspectionFor = (ordinal = 0, patch: Raw = {}): Raw => ({ ...answer(ordinal, 'Unavailable', null), ...patch })
