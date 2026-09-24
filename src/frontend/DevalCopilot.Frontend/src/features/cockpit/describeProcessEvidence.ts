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

/**
 * Describes host-measured process evidence as plain text, never merging it with the attempt's
 * semantic outcome: a clean exit can accompany a rejected response, and a timeout is a process
 * fact, not a workflow classification. Absent evidence is stated truthfully — never inferred.
 */
export function describeProcessEvidence(
  evidence: ProcessEvidenceView | null | undefined,
  context: ProcessEvidenceContext,
): string {
  const measured = evidence ? describeMeasuredEvidence(evidence) : null
  const base =
    measured
    ?? (!context.dispatched ? 'Process not started' : context.running ? 'Process result not yet recorded' : 'Process evidence unknown')
  const timeout =
    typeof evidence?.timeoutMilliseconds === 'number' ? ` · timeout ${formatMilliseconds(evidence.timeoutMilliseconds)}` : ''
  return `${base}${timeout}`
}
