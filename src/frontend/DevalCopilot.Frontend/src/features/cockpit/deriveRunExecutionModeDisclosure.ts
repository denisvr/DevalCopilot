/**
 * The truthful, fixed disclosure of how a run executes. The execution mode is the server's durable
 * fact; this only words it and decides whether the Agent request actions may be offered. A missing
 * or unknown value is never guessed into a known mode, and never authorizes Agent work.
 */
export type RunExecutionModeKind = 'ManualAgent' | 'Simulated' | 'Legacy' | 'Unrecognized'

export interface RunExecutionModeDisclosure {
  kind: RunExecutionModeKind
  /** A short label for badges and summaries. */
  label: string
  /** True only for a mode that admits explicit Agent requests: ManualAgent and Legacy. */
  agentActionsAllowed: boolean
  /** A fixed note about what a Created run is waiting for; null when there is nothing to add. */
  waitingHint: string | null
  /** A fixed note shown in place of the Agent actions when they are not offered; null otherwise. */
  agentActionsNote: string | null
}

export const WAITING_FOR_PLANNING_HINT = 'Waiting for an explicit planning request. Nothing runs until one is requested.'

export function deriveRunExecutionModeDisclosure(
  mode: string | null | undefined,
  lifecycle?: string | null,
): RunExecutionModeDisclosure {
  switch (mode) {
    case 'ManualAgent':
      return {
        kind: 'ManualAgent',
        label: 'Manual Agent run',
        agentActionsAllowed: true,
        waitingHint: lifecycle === 'Created' ? WAITING_FOR_PLANNING_HINT : null,
        agentActionsNote: null,
      }
    case 'Legacy':
      return {
        kind: 'Legacy',
        label: 'Legacy run — execution mode was not recorded',
        agentActionsAllowed: true,
        waitingHint: null,
        agentActionsNote: null,
      }
    case 'Simulated':
      return {
        kind: 'Simulated',
        label: 'Simulated demo run',
        agentActionsAllowed: false,
        waitingHint: null,
        agentActionsNote: 'This is a simulated demo run. Agent requests are not available for it.',
      }
    default:
      return {
        kind: 'Unrecognized',
        label: 'Unrecognized execution mode',
        agentActionsAllowed: false,
        waitingHint: null,
        agentActionsNote:
          'This run reports an execution mode this version does not recognize, so Agent requests are not offered for it.',
      }
  }
}
