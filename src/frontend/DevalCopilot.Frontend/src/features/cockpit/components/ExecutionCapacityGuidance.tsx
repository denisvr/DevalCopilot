import type { ExecutionCapacityStartGate, ExecutionCapacityState } from '../executionCapacity'

interface ExecutionCapacityGuidanceProps {
  gate: ExecutionCapacityStartGate
  /** The host session's capacity state; its refresh reads the execution capacity again and nothing else. */
  state: ExecutionCapacityState | null
  /** The region this guidance belongs to, so two regions of one page name their own button. */
  region: string
}

/**
 * The one fixed guidance of a region while starting a new potentially mutating execution (an implementation, a correction, verification
 * or a local commit) is withheld by the host's execution capacity (ADR-0058). It is shown once per region, never once per control, and
 * renders nothing when the capacity allows the existing actions. It carries no host text, identifier or path, and its refresh is a read:
 * it cancels nothing, queues nothing and releases nothing.
 */
export function ExecutionCapacityGuidance({ gate, state, region }: ExecutionCapacityGuidanceProps) {
  if (gate.guidance === null) {
    return null
  }

  return (
    <div className="dc-workspace-execution-guidance" role="status" aria-label={`Execution capacity: ${region}`}>
      <span className="dc-workspace-evidence-empty">{gate.guidance}</span>
      {state !== null && (
        <button type="button" className="dc-button" onClick={() => state.refresh()}>
          Refresh execution capacity
        </button>
      )}
    </div>
  )
}
