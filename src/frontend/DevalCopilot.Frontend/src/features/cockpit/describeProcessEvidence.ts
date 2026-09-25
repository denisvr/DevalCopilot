import { formatDuration } from './formatDuration'

/** The bounded, host-measured process evidence the API exposes beside an attempt's semantic
 * outcome. Structurally matches the generated `AgentProcessExecutionResponse`. */
export interface ProcessEvidenceView {
  outcome?: string
  exitCode?: number
  durationMilliseconds?: number
  timeoutMilliseconds?: number
}

export interface ProcessEvidenceContext {
  /** Whether the provider was durably dispatched for this attempt. */
  dispatched: boolean
  /** Whether the attempt is still running (no terminal result recorded yet). */
  running: boolean
}

export function formatMilliseconds(milliseconds: number): string {
  const bounded = Math.max(0, milliseconds)
  if (bounded < 1000) {
    return `${Math.round(bounded)} ms`
  }
  if (bounded < 60_000) {
    return `${(bounded / 1000).toFixed(1)} s`
  }
  return formatDuration(bounded / 1000)
}

function describeMeasuredEvidence(evidence: ProcessEvidenceView): string | null {
  const duration =
    typeof evidence.durationMilliseconds === 'number' ? ` after ${formatMilliseconds(evidence.durationMilliseconds)}` : ''
  switch (evidence.outcome) {
    case 'Exited':
      return typeof evidence.exitCode === 'number' ? `Process exited with code ${evidence.exitCode}${duration}` : null
    case 'TimedOut':
      return `Process timed out${duration}`
    case 'Cancelled':
      return `Process cancelled${duration}`
    default:
      return null
  }
}

export function hasKnownProcessEvidence(evidence: ProcessEvidenceView | null | undefined): evidence is ProcessEvidenceView {
  return evidence != null && describeMeasuredEvidence(evidence) !== null
}

/**
 * Whether `evidence` is both shaped like known process evidence AND safe to trust for display,
 * given the attempt's own dispatch/running state. Status is checked FIRST and is decisive: a
 * still-`running` or not-yet-`dispatched` attempt is never trusted, even when the object handed in
 * already has well-formed-looking `outcome`/`exitCode`/`durationMilliseconds` fields — for example a
 * malformed or tampered value from an inconsistent persisted row. This mirrors the backend's own
 * fail-closed rule (`Attempt.GetAgentProcessExecutionEvidence`), so the same "not yet concluded,
 * therefore not trusted" principle holds all the way to presentation, regardless of what is
 * physically in the object.
 */
export function hasTrustedProcessEvidence(
  evidence: ProcessEvidenceView | null | undefined,
  context: ProcessEvidenceContext,
): evidence is ProcessEvidenceView {
  return context.dispatched && !context.running && hasKnownProcessEvidence(evidence)
}

/**
 * Describes host-measured process evidence as plain text, never merging it with the attempt's
 * semantic outcome: a clean exit can accompany a rejected response, and a timeout is a process
 * fact, not a workflow classification. Absent evidence is stated truthfully — never inferred.
 * Dispatch/running state is checked BEFORE the evidence object's own shape, so a still-running or
 * undispatched attempt is always described as such even when handed a malformed object that
 * superficially looks like known evidence (see `hasTrustedProcessEvidence`). The configured timeout
 * is a separate, always-known-at-claim-time value and is shown regardless of trust state.
 */
export function describeProcessEvidence(
  evidence: ProcessEvidenceView | null | undefined,
  context: ProcessEvidenceContext,
): string {
  const base = !context.dispatched
    ? 'Process not started'
    : context.running
      ? 'Process result not yet recorded'
      : (hasKnownProcessEvidence(evidence) ? describeMeasuredEvidence(evidence) : null) ?? 'Process evidence unknown'
  const timeout =
    typeof evidence?.timeoutMilliseconds === 'number' ? ` · timeout ${formatMilliseconds(evidence.timeoutMilliseconds)}` : ''
  return `${base}${timeout}`
}
