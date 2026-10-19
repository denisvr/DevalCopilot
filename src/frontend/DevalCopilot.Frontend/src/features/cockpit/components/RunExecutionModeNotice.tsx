import { deriveRunExecutionModeDisclosure } from '../deriveRunExecutionModeDisclosure'
import type { PlanningSequenceControl } from '../planningSequence'

interface RunExecutionModeNoticeProps {
  executionMode: string | undefined
  lifecycle: string | undefined
  /**
   * What the run's planning-sequence status established (ADR-0055). The "waiting for an explicit request" hint says nothing may be
   * running yet, which a recorded sequence or an unestablished status does not support, so it is shown only when the host recorded no
   * sequence. Omitted: the hint is shown as the mode states it.
   */
  planningSequenceControl?: PlanningSequenceControl
}

/** States how this run executes, and what a Created manual run is waiting for. */
export function RunExecutionModeNotice({ executionMode, lifecycle, planningSequenceControl }: RunExecutionModeNoticeProps) {
  const disclosure = deriveRunExecutionModeDisclosure(executionMode, lifecycle)
  const showHint = planningSequenceControl === undefined || planningSequenceControl === 'none'
  return (
    <div className="dc-run-execution-mode" data-mode={disclosure.kind}>
      <span className="dc-badge">{disclosure.label}</span>
      {showHint && disclosure.waitingHint ? <span className="dc-run-execution-mode-hint">{disclosure.waitingHint}</span> : null}
    </div>
  )
}
