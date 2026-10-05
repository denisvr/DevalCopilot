import type { VerificationDiagnosisStatusResponse } from '../../../api/clients'
import type { GlobalAgentClaimBlock } from '../deriveGlobalAgentClaimBlock'
import { describeGlobalAgentClaimBlock } from '../deriveGlobalAgentClaimBlock'
import type { AgentClaimPathTimeFit } from '../deriveAgentClaimPathTimeFit'
import { describeAgentClaimPathTimeFitBlock, isAgentClaimPathTimeFitBlocking } from '../deriveAgentClaimPathTimeFit'
import { DirectGuidanceEditor } from './DirectGuidanceEditor'
import { DirectGuidanceFact } from './DirectGuidanceFact'
import { ProcessEvidenceLine } from './ProcessEvidenceLine'
import { TokenUsageLine } from './TokenUsageLine'
import { ACCOUNT_USAGE_OUTCOME_LABELS } from '../accountUsageOutcomeLabels'

interface VerificationDiagnosisActionProps {
  /** The run that owns the correction draft, with the diagnosis attempt the status names. */
  runId: string
  status: VerificationDiagnosisStatusResponse | null
  statusLoading: boolean
  statusError: string | null
  requesting: boolean
  requestError: string | null
  /** Requests the diagnosis of the report the host names in `diagnosableExecutionReportMessageId`. */
  onRequest: () => void
  correctionRequesting: boolean
  correctionError: string | null
  /** Requests the correction of the diagnosis the host names in `attemptId` (or the recorded human escalation at exhaustion). */
  onRequestCorrection: () => void
  /** Requests the same correction with optional direct human guidance (ADR-0019). Resolves true only when the server accepted
   * it AND the submission still belonged to the current lifetime. */
  onRequestCorrectionWithGuidance: (guidance: string) => Promise<boolean>
  /** A known global Agent-claim hard stop (ADR-0012/ADR-0013), or `null` when none is known.
   * Never a positive eligibility signal — see `deriveGlobalAgentClaimBlock`. */
  globalClaimBlock: GlobalAgentClaimBlock | null
  /** The advisory time-fit result for the diagnosis, which runs under the code-review timeout. */
  timeFit: AgentClaimPathTimeFit
  /** The advisory time-fit result for the correction, which runs under the review-correction timeout. */
  correctionTimeFit: AgentClaimPathTimeFit
}

const OUTCOME_LABEL: Record<string, string> = {
  ...ACCOUNT_USAGE_OUTCOME_LABELS,
  DiagnosisFindingsRecorded: 'Diagnosis findings recorded',
  DiagnosisEscalated: 'Diagnosis escalated for a human decision',
  InvalidStructuredOutput: 'Codex returned an invalid structured diagnosis',
  ProviderInvocationFailed: 'Codex could not be invoked for the diagnosis',
  CheckpointEvidenceUnavailable: 'Source evidence could not be captured',
  SourceChanged: 'Source changed before the diagnosis completed',
  WorkspaceNoLongerEligible: 'Workspace no longer eligible for dispatch',
  InputAlreadyDiagnosed: 'This failed verification was already diagnosed',
  VerificationEvidenceChanged: 'The verification evidence changed before the diagnosis completed',
}

const CORRECTION_OUTCOME_LABEL: Record<string, string> = {
  CorrectionApplied: 'Correction applied',
  CorrectionNoChangesProduced: 'No correction changes produced',
  InputAlreadyCorrected: 'These findings were already corrected',
  CorrectionHeadChanged: 'Unexpected HEAD change requires attention',
  SourceChanged: 'Source changed before correction completed',
  InvalidStructuredOutput: 'The Implementer returned an invalid correction response',
  ProviderInvocationFailed: 'The Implementer could not be invoked for correction',
  CheckpointEvidenceUnavailable: 'Source evidence could not be captured',
  WorkspaceNoLongerEligible: 'Workspace no longer eligible for correction',
}

/** Fixed copy per `diagnosisUnavailableCode`; the closed codes are display hints, never eligibility. */
const UNAVAILABLE_COPY: Record<string, string> = {
  'verification_diagnosis.workspace_not_ready': 'The workspace is not ready, so failed verification cannot be diagnosed yet.',
  'verification_diagnosis.no_current_execution_report': 'There is no current implementation report whose verification can be diagnosed.',
  'verification_diagnosis.no_verification_commands_enabled': 'No verification command is enabled, so there is nothing to diagnose.',
  'verification_diagnosis.evidence_missing':
    'Verification has not been run for the current source. Run it before requesting a diagnosis.',
  'verification_diagnosis.evidence_running': 'Verification is still running. Wait for it to finish before requesting a diagnosis.',
  'verification_diagnosis.evidence_not_diagnosable':
    'The current verification evidence cannot be diagnosed. Run verification again before requesting a diagnosis.',
  'verification_diagnosis.no_failed_verification':
    'No enabled verification command failed for the current source, so there is nothing to diagnose. Request the ordinary code review instead.',
  'verification_diagnosis.failed_output_unavailable':
    'The output of the failed verification is unavailable, so it cannot be diagnosed. Run verification again.',
  'agent_attempts.already_diagnosed':
    'The failed verification of the current source was already diagnosed. Run verification again before requesting a new diagnosis.',
}

const FAILED_OUTCOMES = new Set([
  'AccountUsageStopReached',
  'AccountUsageEvidenceUnavailable',
  'InvalidStructuredOutput',
  'ProviderInvocationFailed',
  'CheckpointEvidenceUnavailable',
  'SourceChanged',
  'WorkspaceNoLongerEligible',
])

function phaseLabel(status: VerificationDiagnosisStatusResponse): string {
  if (status.status === 'Running') {
    return status.dispatchedAtUtc ? 'Running' : 'Pending'
  }
  if (status.outcome) {
    return OUTCOME_LABEL[status.outcome] ?? status.outcome
  }
  return status.status ?? ''
}

function correctionPhaseLabel(status: VerificationDiagnosisStatusResponse): string {
  if (status.correctionStatus === 'Running') {
    return 'Running'
  }
  if (status.correctionOutcome) {
    return CORRECTION_OUTCOME_LABEL[status.correctionOutcome] ?? status.correctionOutcome
  }
  return status.correctionStatus ?? ''
}

/**
 * The durable diagnosis of a FAILED local verification: a read-only Codex analysis of the bounded
 * failure evidence (ADR-0018). It is a different thing from the ordinary code review below it: a
 * diagnosis never approves anything and ends in findings or one escalation. Every step is
 * requested explicitly by the human — the diagnosis, then separately the Claude correction of its
 * findings, then a new verification run, then the ordinary code review — and the host stays
 * authoritative for each one's eligibility: the identifiers the requests carry come from the
 * host's own status fields, and `diagnosisUnavailableCode` is only a display hint. A diagnosis has
 * no repair attempt; a failed one is simply requested again. The correction shares ONE allowance
 * with ordinary review corrections, and its exhaustion records a human escalation that grants no
 * authorization, so no authorize control exists here. Never displays an executable path,
 * argument, prompt, raw manifest, credential, or provider output — only closed labels, counts and
 * the pinned verification list.
 */
export function VerificationDiagnosisAction({
  runId,
  status,
  statusLoading,
  statusError,
  requesting,
  requestError,
  onRequest,
  correctionRequesting,
  correctionError,
  onRequestCorrection,
  onRequestCorrectionWithGuidance,
  globalClaimBlock,
  timeFit,
  correctionTimeFit,
}: VerificationDiagnosisActionProps) {
  if (!status && !statusError) {
    return null
  }

  const hasAttempt = status?.hasAttempt === true
  const diagnosisRunning = hasAttempt && status?.status === 'Running'
  const correctionRunning = hasAttempt && status?.correctionStatus === 'Running'
  const anyRunning = diagnosisRunning || correctionRunning
  const diagnosable = Boolean(status?.diagnosableExecutionReportMessageId)
  const timeFitBlocked = isAgentClaimPathTimeFitBlocking(timeFit)
  const correctionTimeFitBlocked = isAgentClaimPathTimeFitBlocking(correctionTimeFit)
  const unavailableCopy = status?.diagnosisUnavailableCode ? UNAVAILABLE_COPY[status.diagnosisUnavailableCode] : undefined
  const findingsRecorded = hasAttempt && status?.status === 'Completed' && status.outcome === 'DiagnosisFindingsRecorded'
  const escalated = hasAttempt && status?.status === 'Completed' && status.outcome === 'DiagnosisEscalated'
  const failedWithoutFindings = hasAttempt && Boolean(status?.outcome) && FAILED_OUTCOMES.has(status?.outcome ?? '')
  const hasCorrectionAttempt = hasAttempt && Boolean(status?.correctionAttemptId)
  const budgetExhausted = findingsRecorded && status?.correctionBudgetExhausted === true
  const hasCorrectionEscalation = Boolean(status?.correctionEscalationId)
  const correctionApplicable = findingsRecorded && status?.correctionApplicable === true
  const correctionBlocked = Boolean(globalClaimBlock) || correctionTimeFitBlocked
  const canRequestCorrection = correctionApplicable && !anyRunning && !budgetExhausted
  const canRecordEscalation = correctionApplicable && !anyRunning && budgetExhausted && !hasCorrectionEscalation
  const correctionTerminal = hasCorrectionAttempt && status?.correctionStatus !== 'Running'
  const configuredCommandSandbox = status?.configuredCommandSandbox === 'read-only' ? 'read-only' : 'Unknown'
  const configuredRolloutPersistence = status?.configuredRolloutPersistence === 'Disabled' ? 'Disabled' : 'Unknown'

  return (
    <section className="dc-verification-diagnosis-action" aria-label="Verification diagnosis">
      <p className="dc-verification-diagnosis-heading">
        <strong>Diagnosis of failed local verification (Codex, read-only)</strong>
      </p>
      <p className="dc-verification-diagnosis-note">
        This is not a code review: a diagnosis explains why local verification failed and is never a verdict on the implementation.
        Each step is requested explicitly.
      </p>
      {diagnosisRunning && status && (
        <p className="dc-verification-diagnosis-status" aria-busy="true">
          Verification diagnosis {phaseLabel(status).toLowerCase()}…
        </p>
      )}
      {!anyRunning && diagnosable && globalClaimBlock && (
        <p className="dc-verification-diagnosis-global-block" role="status">
          {describeGlobalAgentClaimBlock(globalClaimBlock)}
        </p>
      )}
      {!anyRunning && diagnosable && timeFitBlocked && (
        <p className="dc-verification-diagnosis-time-fit-block" role="status">
          {describeAgentClaimPathTimeFitBlock(timeFit)}
        </p>
      )}
      {!anyRunning && diagnosable && !globalClaimBlock && !timeFitBlocked && (
        <button
          type="button"
          className="dc-verification-diagnosis-request"
          disabled={requesting || statusLoading}
          onClick={onRequest}
        >
          {requesting ? 'Requesting…' : 'Diagnose failed verification with Codex'}
        </button>
      )}
      {!anyRunning && !diagnosable && status && (
        <p className="dc-verification-diagnosis-unavailable">
          {unavailableCopy ?? 'No failed local verification can be diagnosed right now.'}
        </p>
      )}
      {status && hasAttempt && !diagnosisRunning && (
        <p className="dc-verification-diagnosis-status">
          Last diagnosis #{status.attemptNumber}: {phaseLabel(status)}.
        </p>
      )}
      {status?.outcome === 'VerificationEvidenceChanged' && (
        <p className="dc-verification-diagnosis-evidence-changed" role="status">
          The verification evidence changed while the diagnosis was running, so it was not recorded. Run or inspect verification
          again and request a new diagnosis.
        </p>
      )}
      {status?.outcome === 'InputAlreadyDiagnosed' && (
        <p className="dc-verification-diagnosis-already" role="status">
          This failed verification already has a diagnosis, so nothing new was recorded.
        </p>
      )}
      {failedWithoutFindings && (
        <p className="dc-verification-diagnosis-failed" role="status">
          The diagnosis did not produce findings. A diagnosis has no repair attempt; when the failed verification is
          diagnosable, a new diagnosis can be requested explicitly.
        </p>
      )}
      {status && hasAttempt && (
        <p className="dc-verification-diagnosis-assignment">
          Configured command sandbox: {configuredCommandSandbox} · Configured rollout persistence: {configuredRolloutPersistence}
        </p>
      )}
      {status && hasAttempt && (status.verification ?? []).length > 0 && (
        <ul className="dc-verification-diagnosis-members" aria-label="Verification evidence pinned by this diagnosis">
          {(status.verification ?? []).map((member) => (
            <li key={`${member.position}:${member.commandName}`}>
              #{member.position} {member.commandName} · execution {member.executionNumber} · {member.status}
              {member.exitCode === undefined || member.exitCode === null ? '' : ` · exit code ${member.exitCode}`}
            </li>
          ))}
        </ul>
      )}
      {findingsRecorded && status && (
        <p className="dc-verification-diagnosis-findings">
          Codex recorded {status.findingCount ?? 0} {status.findingCount === 1 ? 'finding' : 'findings'} from the failed
          verification. They appear in the collaboration timeline as review findings. This diagnosis is not a verdict on the
          implementation.
        </p>
      )}
      {escalated && (
        <div className="dc-verification-diagnosis-escalation" role="status">
          <p>
            Codex escalated instead of recording findings. The Escalation message in the collaboration timeline needs a human
            decision.
          </p>
          <p>
            It grants no authority to change recipes, tools, permissions, or scope, and there is nothing to correct from this
            diagnosis.
          </p>
        </div>
      )}
      {findingsRecorded && status && (
        <>
          <p className="dc-verification-diagnosis-allowance">
            Correction allowance, shared with ordinary review corrections: {status.reviewCorrectionAttemptsUsed ?? 0} of{' '}
            {status.maximumReviewCorrectionAttempts ?? 0} used.
          </p>
          {correctionRunning && (
            <p className="dc-verification-diagnosis-status" aria-busy="true">
              Diagnosis correction {correctionPhaseLabel(status).toLowerCase()}…
            </p>
          )}
          {!anyRunning && (canRequestCorrection || canRecordEscalation) && globalClaimBlock && (
            <p className="dc-verification-diagnosis-global-block" role="status">
              {describeGlobalAgentClaimBlock(globalClaimBlock)}
            </p>
          )}
          {!anyRunning && (canRequestCorrection || canRecordEscalation) && correctionTimeFitBlocked && (
            <p className="dc-verification-diagnosis-time-fit-block" role="status">
              {describeAgentClaimPathTimeFitBlock(correctionTimeFit)}
            </p>
          )}
          {!correctionRunning && !correctionBlocked && !canRecordEscalation && (
            <button
              type="button"
              className="dc-verification-diagnosis-correct"
              disabled={!canRequestCorrection || correctionRequesting || statusLoading}
              onClick={onRequestCorrection}
            >
              {correctionRequesting ? 'Requesting…' : 'Correct the diagnosed findings with Claude'}
            </button>
          )}
          {!correctionRunning && !correctionBlocked && canRequestCorrection && status.attemptId && (
            <DirectGuidanceEditor
              runId={runId}
              sourceId={status.attemptId}
              label="Direct guidance for this diagnosis correction"
              formLabel="Correct the diagnosed findings with guidance"
              submitLabel="Correct the diagnosed findings with guidance"
              pendingLabel="Requesting with guidance…"
              requesting={correctionRequesting}
              statusLoading={statusLoading}
              onSubmit={onRequestCorrectionWithGuidance}
            />
          )}
          {!correctionRunning && !correctionBlocked && canRecordEscalation && (
            <button
              type="button"
              className="dc-verification-diagnosis-record-escalation"
              disabled={correctionRequesting || statusLoading}
              onClick={onRequestCorrection}
            >
              {correctionRequesting ? 'Recording…' : 'Record human escalation'}
            </button>
          )}
          {!correctionRunning && !correctionApplicable && !hasCorrectionAttempt && (
            <p className="dc-verification-diagnosis-correction-unavailable">
              These findings no longer apply exactly to the current source, so they cannot be corrected.
            </p>
          )}
          {budgetExhausted && (
            <p className="dc-verification-diagnosis-guidance-unavailable">
              Direct guidance is available only within the shared correction allowance.
            </p>
          )}
          {budgetExhausted && (
            <div className="dc-verification-diagnosis-exhausted" role="status">
              <p>
                The shared correction allowance is exhausted, so no further correction can be requested.
                {hasCorrectionEscalation
                  ? ' A human escalation was recorded for this run; it needs a human decision.'
                  : ' A human escalation has not been recorded yet.'}
              </p>
              {hasCorrectionEscalation && (
                <p>It grants no authority to continue, change recipes, tools, permissions, or scope.</p>
              )}
            </div>
          )}
          {hasCorrectionAttempt && !correctionRunning && (
            <p className="dc-verification-diagnosis-status">
              Last correction #{status.correctionAttemptNumber}: {correctionPhaseLabel(status)}.
            </p>
          )}
          {hasCorrectionAttempt && (
            <DirectGuidanceFact fact={status.correctionDirectGuidance} className="dc-verification-diagnosis-direct-guidance" />
          )}
          {correctionTerminal && (
            <p className="dc-verification-diagnosis-after-correction" role="status">
              Run local verification again explicitly. An ordinary code review requires every enabled verification command to
              pass for the new checkpoint.
              {status.reviewableExecutionReportMessageId
                ? ' The code review below now targets the corrected implementation report.'
                : ''}
            </p>
          )}
        </>
      )}
      {status && hasAttempt && (
        <>
          <ProcessEvidenceLine
            processExecution={status.processExecution}
            dispatchedAtUtc={status.dispatchedAtUtc}
            status={status.status}
          />
          <TokenUsageLine tokenUsage={status.tokenUsage} dispatchedAtUtc={status.dispatchedAtUtc} status={status.status} />
        </>
      )}
      {(requestError ?? correctionError ?? statusError) && (
        <p className="dc-verification-diagnosis-error" role="status">
          {requestError ?? correctionError ?? statusError}
        </p>
      )}
    </section>
  )
}
