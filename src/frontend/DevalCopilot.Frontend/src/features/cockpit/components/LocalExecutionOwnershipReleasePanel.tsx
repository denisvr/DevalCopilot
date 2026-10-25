import type { UseLocalExecutionOwnershipReleaseResult } from '../hooks/useLocalExecutionOwnershipRelease'
import type { LocalExecutionReleaseCandidate, LocalExecutionReleaseReceipt, LocalExecutionReleaseTarget } from '../localExecutionOwnershipRelease'
import {
  RELEASED_ALREADY_LINE,
  RELEASED_LINE,
  RELEASE_CLEAR_LABEL,
  RELEASE_CONFIRM_LABEL,
  RELEASE_EXPIRED,
  RELEASE_EXPLANATION,
  RELEASE_HIDE_LABEL,
  RELEASE_NO_TARGETS,
  RELEASE_OUTCOME_LABEL,
  RELEASE_READING,
  RELEASE_READ_AGAIN_LABEL,
  RELEASE_READ_FAILURES,
  RELEASE_REASON_COPY,
  RELEASE_REGION_LABEL,
  RELEASE_SELECT_LABEL,
  RELEASE_SUBMITTING_LABEL,
  describeReleaseConfirmation,
} from '../localExecutionOwnershipReleaseCopy'
import { LOCAL_EXECUTION_STEP_KIND_LABEL } from '../localExecutionSequenceCopy'

interface LocalExecutionOwnershipReleasePanelProps {
  release: UseLocalExecutionOwnershipReleaseResult
  /** What the accepted status offers; the choice is made only among these. */
  targets: readonly LocalExecutionReleaseTarget[]
  /** Whether the run's one status reader can start a command now (nothing else is unconfirmed). */
  commandsAvailable: boolean
}

// One tick is 100 nanoseconds: ten thousand ticks are one millisecond.
const milliseconds = (ticks: string): string => `${(BigInt(ticks) / 10_000n).toString()} ms (${ticks} ticks)`

const stepLabel = (target: LocalExecutionReleaseTarget): string =>
  `Step ${target.ordinal + 1} · ${LOCAL_EXECUTION_STEP_KIND_LABEL[target.stepKind]} · pass ${target.passNumber}`

function Receipt({ release }: { release: LocalExecutionReleaseReceipt }) {
  return (
    <dl className="dc-local-execution-release-receipt" aria-label="Ownership release receipt">
      <dt>Release</dt>
      <dd>{release.releaseId}</dd>
      <dt>Operation</dt>
      <dd>
        {release.operationId} ({release.operationFamily === 'Verification' ? 'verification run' : 'agent attempt'}, {LOCAL_EXECUTION_STEP_KIND_LABEL[release.stepKind]})
      </dd>
      <dt>Released at (UTC)</dt>
      <dd>{release.releasedAtUtc}</dd>
      <dt>Human event sequence</dt>
      <dd>{release.eventSequence}</dd>
    </dl>
  )
}

function Candidate({ candidate }: { candidate: LocalExecutionReleaseCandidate }) {
  return (
    <dl className="dc-local-execution-release-candidate" aria-label="Original-host termination evidence">
      <dt>Operation</dt>
      <dd>
        {candidate.operationId} ({candidate.operationFamily === 'Verification' ? 'verification run' : 'agent attempt'}, {LOCAL_EXECUTION_STEP_KIND_LABEL[candidate.stepKind]})
      </dd>
      <dt>Recorded result</dt>
      <dd>
        {candidate.operationStatus} · process outcome {RELEASE_OUTCOME_LABEL[candidate.processOutcome]}
      </dd>
      <dt>Recorded exit code of the result</dt>
      <dd>{candidate.semanticExitCode === null ? 'none recorded' : candidate.semanticExitCode}</dd>
      <dt>Exit code the operating system reported at termination</dt>
      <dd>{candidate.physicalExitCode}</dd>
      <dt>Process duration</dt>
      <dd>{milliseconds(candidate.processDurationTicks)}</dd>
      <dt>Termination observed after</dt>
      <dd>{milliseconds(candidate.observedElapsedTicks)}</dd>
      <dt>Dispatched (UTC)</dt>
      <dd>{candidate.dispatchedAtUtc}</dd>
      <dt>Observation recorded (UTC)</dt>
      <dd>{candidate.observedAtUtc}</dd>
      <dt>Observation</dt>
      <dd>{candidate.observationId}</dd>
      <dt>Confirmation (SHA-256, version {candidate.confirmationVersion})</dt>
      <dd>{candidate.confirmationSha256}</dd>
      <dt>Confirmation expires (UTC)</dt>
      <dd>{candidate.confirmationExpiresAtUtc}</dd>
    </dl>
  )
}

/**
 * The collapsed, explicit release of one terminal operation's retained quarantine (ADR-0065), for the current run. Everything it says
 * comes from the run's accepted status and from one explicit read of one chosen operation: it never polls, never reads until an
 * operation is chosen and never offers a release for anything but that operation's one fresh Available answer. Confirming submits once
 * and consumes the confirmation; it marks nothing released itself, because only the run's own status read, after the host recorded it,
 * ever shows the fence, workspace or capacity released. Every host value is rendered as literal text.
 */
export function LocalExecutionOwnershipReleasePanel({ release, targets, commandsAvailable }: LocalExecutionOwnershipReleasePanelProps) {
  const { open, selected, reading, readFailure, answer, expired, submission } = release
  const submitting = submission.phase === 'submitting'
  const consumed = submission.phase === 'released' || submission.phase === 'refused' || submission.phase === 'unknown'
  const canRead = selected !== null && !reading && !submitting

  return (
    <section className="dc-local-execution-release" aria-label={RELEASE_REGION_LABEL}>
      <button type="button" aria-expanded={open} disabled={submitting} onClick={open ? release.close : release.show}>
        {open ? RELEASE_HIDE_LABEL : RELEASE_REGION_LABEL}
      </button>
      {open && (
        <div className="dc-local-execution-release-body">
          <ul className="dc-local-execution-explanation">
            {RELEASE_EXPLANATION.map((line) => (
              <li key={line}>{line}</li>
            ))}
          </ul>
          {targets.length === 0 ? (
            <p className="dc-local-execution-note">{RELEASE_NO_TARGETS}</p>
          ) : (
            <ol className="dc-local-execution-release-targets" aria-label={RELEASE_SELECT_LABEL}>
              {targets.map((target) => (
                <li key={target.stepId} data-step-id={target.stepId}>
                  <span>
                    {stepLabel(target)} · {target.releasedInStatus ? 'quarantine released' : 'quarantine retained'}
                  </span>{' '}
                  <button
                    type="button"
                    aria-label={`Select ${stepLabel(target).toLowerCase()}`}
                    aria-pressed={selected?.stepId === target.stepId}
                    disabled={submitting}
                    onClick={() => release.select(target.stepId)}
                  >
                    Select
                  </button>
                </li>
              ))}
            </ol>
          )}
          {selected !== null && (
            <div className="dc-local-execution-release-selection" data-step-id={selected.stepId}>
              <p>
                <strong>{stepLabel(selected)}</strong>
              </p>
              {reading && (
                <p role="status" className="dc-local-execution-note">
                  {RELEASE_READING}
                </p>
              )}
              {readFailure !== null && (
                <p role="alert" className="dc-local-execution-error">
                  {RELEASE_READ_FAILURES[readFailure]}
                </p>
              )}
              {answer?.status === 'NotEligible' || answer?.status === 'Unavailable' ? (
                <p role="status" className="dc-local-execution-reason" data-release-status={answer.status}>
                  {RELEASE_REASON_COPY[answer.reasonCode]}
                </p>
              ) : null}
              {answer?.status === 'Released' && (
                <>
                  <p role="status" data-release-status="Released">
                    {RELEASED_ALREADY_LINE}
                  </p>
                  <Receipt release={answer.release} />
                </>
              )}
              {answer?.status === 'Available' && !consumed && (
                <div role="group" aria-label="Confirm ownership release" data-release-status="Available">
                  <ul className="dc-local-execution-explanation">
                    {describeReleaseConfirmation(answer.candidate).map((line) => (
                      <li key={line}>{line}</li>
                    ))}
                  </ul>
                  <Candidate candidate={answer.candidate} />
                  {expired ? (
                    <p role="status" className="dc-local-execution-note">
                      {RELEASE_EXPIRED}
                    </p>
                  ) : (
                    <button type="button" disabled={!commandsAvailable || submitting} onClick={release.confirm}>
                      {submitting ? RELEASE_SUBMITTING_LABEL : RELEASE_CONFIRM_LABEL}
                    </button>
                  )}
                  {submission.phase === 'idle' && submission.message !== null && (
                    <p role="alert" className="dc-local-execution-error">
                      {submission.message}
                    </p>
                  )}
                </div>
              )}
              {submission.phase === 'released' && (
                <>
                  <p role="status" data-release-status="Released">
                    {RELEASED_LINE}
                  </p>
                  <Receipt release={submission.release} />
                </>
              )}
              {(submission.phase === 'refused' || submission.phase === 'unknown') && (
                <p role="alert" className="dc-local-execution-error" data-release-outcome={submission.phase}>
                  {submission.message}
                </p>
              )}
              <p>
                <button type="button" disabled={!canRead} onClick={release.reload}>
                  {RELEASE_READ_AGAIN_LABEL}
                </button>{' '}
                <button type="button" disabled={submitting} onClick={release.clear}>
                  {RELEASE_CLEAR_LABEL}
                </button>
              </p>
            </div>
          )}
        </div>
      )}
    </section>
  )
}
