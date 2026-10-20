import type { WorkspaceExecutionStartGate, WorkspaceExecutionState } from '../workspaceExecution'

interface WorkspaceExecutionGuidanceProps {
  gate: WorkspaceExecutionStartGate
  /** The selected project's status; its refresh reads the workspace execution status again and nothing else. */
  state: WorkspaceExecutionState | null
  /** The region this guidance belongs to, so two regions of one page name their own button. */
  region: string
}

/**
 * The one fixed guidance of a region while starting an Agent, repair, planning sequence, verification or local commit is withheld by
 * the workspace execution status (ADR-0057). It is shown once per region, never once per control, and renders nothing when the status
 * allows the existing actions. It carries no host text, identifier or path, and its refresh is a read: it cancels nothing and releases
 * nothing.
 */
export function WorkspaceExecutionGuidance({ gate, state, region }: WorkspaceExecutionGuidanceProps) {
  if (gate.guidance === null) {
    return null
  }

  return (
    <div className="dc-workspace-execution-guidance" role="status" aria-label={`Workspace execution status: ${region}`}>
      <span className="dc-workspace-evidence-empty">{gate.guidance}</span>
      {state !== null && (
        <button type="button" className="dc-button" onClick={() => state.refresh()}>
          Refresh workspace execution status
        </button>
      )}
    </div>
  )
}
