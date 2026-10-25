import { releaseTargetsOf } from './localExecutionOwnershipRelease'
import type { LocalExecutionReleaseTarget } from './localExecutionOwnershipRelease'
import { toLocalExecutionSequenceStatus } from './localExecutionSequence'
import { LOCAL_CHECKPOINT_ID, LOCAL_FINGERPRINT, LOCAL_SEQUENCE_ID, localGuid, localSequenceFor, localStatusFor, localStepFor } from './localExecutionSequenceFixture'

/**
 * Test-only builders of the parsed ownership release answers the host writes (ADR-0065). They write exactly the members the contract
 * names with explicit nulls, and every member can be replaced through a patch to build a contradiction. The defaults describe the
 * quarantined implementation step of `localStepFor(0, 'quarantined')` in a NeedsAttention sequence.
 */
type Raw = Record<string, unknown>

export const RELEASE_RUN = 'run-1'
export const RELEASE_STEP_ID = localGuid(100)
export const RELEASE_OPERATION_ID = localGuid(200)
export const RELEASE_OBSERVATION_ID = localGuid(600)
export const RELEASE_ID = localGuid(601)
export const RELEASE_CONFIRMATION = '3c'.repeat(32)

/** The target the accepted status offers for its one quarantined step. */
export function releaseTargetFor(patch: Partial<LocalExecutionReleaseTarget> = {}): LocalExecutionReleaseTarget {
  const status = toLocalExecutionSequenceStatus(
    localStatusFor(RELEASE_RUN, 'NeedsAttention', {}, { steps: [localStepFor(0, 'quarantined')], reasonCode: 'exit_evidence_missing', reasonText: 'x' }),
    RELEASE_RUN,
  )
  const target = releaseTargetsOf(RELEASE_RUN, status!.sequence!)[0]
  return { ...target, ...patch }
}

/** The release candidate of that step: a timed-out implementation whose process the original host observed terminated. */
export const releaseCandidateFor = (patch: Raw = {}): Raw => ({
  sequenceId: LOCAL_SEQUENCE_ID,
  sequenceState: 'NeedsAttention',
  admissionMode: 'Immediate',
  sourceCheckpointId: LOCAL_CHECKPOINT_ID,
  sourceFingerprintSha256: LOCAL_FINGERPRINT,
  stepKind: 'Implementation',
  ordinal: 0,
  passNumber: 0,
  operationFamily: 'Agent',
  operationId: RELEASE_OPERATION_ID,
  operationStatus: 'Failed',
  processOutcome: 'TimedOut',
  semanticExitCode: null,
  observationId: RELEASE_OBSERVATION_ID,
  physicalExitCode: 137,
  observedElapsedTicks: '40000',
  processDurationTicks: '50000000',
  dispatchedAtUtc: '2026-10-09T10:00:30.1234567Z',
  observedAtUtc: '2026-10-09T10:05:30.1234567Z',
  confirmationVersion: '1',
  confirmationSha256: RELEASE_CONFIRMATION,
  confirmationExpiresAtUtc: '2026-10-09T10:07:30.0000000Z',
  ...patch,
})

export const releaseReceiptFor = (patch: Raw = {}): Raw => ({
  releaseId: RELEASE_ID,
  operationFamily: 'Agent',
  operationId: RELEASE_OPERATION_ID,
  stepKind: 'Implementation',
  releasedAtUtc: '2026-10-09T10:06:00.1234567Z',
  eventSequence: '42',
  ...patch,
})

const answer = (status: string, patch: Raw): Raw => ({
  runId: RELEASE_RUN,
  stepId: RELEASE_STEP_ID,
  status,
  reasonCode: null,
  candidate: null,
  release: null,
  ...patch,
})

export const availableReleaseFor = (patch: Raw = {}, candidate: Raw = {}): Raw => answer('Available', { candidate: releaseCandidateFor(candidate), ...patch })
export const releasedReleaseFor = (patch: Raw = {}, receipt: Raw = {}): Raw => answer('Released', { release: releaseReceiptFor(receipt), ...patch })
export const notEligibleReleaseFor = (reasonCode = 'already_free', patch: Raw = {}): Raw => answer('NotEligible', { reasonCode, ...patch })
export const unavailableReleaseFor = (reasonCode = 'evidence_missing', patch: Raw = {}): Raw => answer('Unavailable', { reasonCode, ...patch })

/** A sequence whose one step is quarantined (`released` false) or released, in the shape the status decoder accepts. */
export const releasableSequenceFor = (released = false): Raw =>
  localSequenceFor('NeedsAttention', {
    steps: [localStepFor(0, released ? 'released' : 'quarantined')],
    reasonCode: 'exit_evidence_missing',
    reasonText: 'A dispatched operation ended without recorded process exit evidence.',
  })
