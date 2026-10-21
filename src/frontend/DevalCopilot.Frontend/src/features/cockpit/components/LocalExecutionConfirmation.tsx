import type { LocalExecutionAvailabilityCandidate } from '../localExecutionSequence'
import {
  LOCAL_EXECUTION_CANCEL_LABEL,
  LOCAL_EXECUTION_CONFIRM_LABEL,
  describeLocalExecutionConfirmation,
} from '../localExecutionSequenceCopy'

interface LocalExecutionConfirmationProps {
  planProposalMessageId: string
  candidate: LocalExecutionAvailabilityCandidate
  /** Whether the confirmation can be submitted now: settled availability, no pending request, no withheld reason. */
  canConfirm: boolean
  submitting: boolean
  onConfirm: () => void
  onCancel: () => void
}

const show = (value: string | number | null): string => (value === null ? 'not requested' : String(value))

/**
 * The explicit confirmation shown before one arming is submitted. It discloses the exact plan and source, the verification commands
 * that can mutate the workspace (by number and inert name, with their timeouts), the shared correction allowance, the two-hour
 * advancement bound, the separate approval and commit, the Stop semantics and the requested assignments the host pins. Names are
 * literal text and are never interpreted.
 */
export function LocalExecutionConfirmation({
  planProposalMessageId,
  candidate,
  canConfirm,
  submitting,
  onConfirm,
  onCancel,
}: LocalExecutionConfirmationProps) {
  const { disclosure } = candidate
  return (
    <div className="dc-local-execution-confirmation" role="group" aria-label="Confirm local execution cycle">
      <ul className="dc-local-execution-explanation">
        {describeLocalExecutionConfirmation(candidate).map((line) => (
          <li key={line}>{line}</li>
        ))}
      </ul>
      <dl className="dc-local-execution-candidate" aria-label="Local execution arming candidate">
        <dt>Plan message</dt>
        <dd>{planProposalMessageId}</dd>
        <dt>Plan authority</dt>
        <dd>{candidate.planAuthority}</dd>
        <dt>Workspace</dt>
        <dd>{candidate.workspaceId}</dd>
        <dt>Checkpoint</dt>
        <dd>{candidate.checkpointId}</dd>
        <dt>Fingerprint (SHA-256)</dt>
        <dd>{candidate.fingerprintSha256}</dd>
        <dt>Confirmation (SHA-256)</dt>
        <dd>{candidate.confirmationSha256}</dd>
        <dt>Automatic corrections</dt>
        <dd>{candidate.correctionAllowance}</dd>
        <dt>Maximum operations</dt>
        <dd>{candidate.maximumSteps}</dd>
      </dl>
      <p>Verification commands that can mutate the workspace, in the order they run:</p>
      <ol className="dc-local-execution-recipes" aria-label="Verification commands that can mutate the workspace">
        {candidate.recipes.map((recipe) => (
          <li key={recipe.commandId} data-command-number={recipe.commandNumber}>
            #{recipe.commandNumber} {recipe.name} · {recipe.timeoutSeconds}s timeout
          </li>
        ))}
      </ol>
      <dl className="dc-local-execution-assignments" aria-label="Requested assignments pinned by this authorization">
        <dt>Codex model / effort</dt>
        <dd>
          {show(disclosure.codexModel)} / {show(disclosure.codexEffort)}
        </dd>
        <dt>Claude model / effort</dt>
        <dd>
          {show(disclosure.claudeModel)} / {show(disclosure.claudeEffort)}
        </dd>
        <dt>Claude turn limit</dt>
        <dd>{show(disclosure.claudeMaxTurns)}</dd>
        <dt>Codex account usage stop (percent)</dt>
        <dd>{show(disclosure.codexAccountUsageStopPercent)}</dd>
        <dt>Codex token stop threshold</dt>
        <dd>{show(disclosure.codexTokenStopThreshold)}</dd>
        <dt>Claude token stop threshold</dt>
        <dd>{show(disclosure.claudeTokenStopThreshold)}</dd>
      </dl>
      <button type="button" disabled={!canConfirm} onClick={onConfirm}>
        {submitting ? 'Authorizing…' : LOCAL_EXECUTION_CONFIRM_LABEL}
      </button>
      <button type="button" disabled={submitting} onClick={onCancel}>
        {LOCAL_EXECUTION_CANCEL_LABEL}
      </button>
    </div>
  )
}
