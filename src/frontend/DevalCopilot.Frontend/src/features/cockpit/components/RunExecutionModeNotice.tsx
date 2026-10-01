import { deriveRunExecutionModeDisclosure } from '../deriveRunExecutionModeDisclosure'

interface RunExecutionModeNoticeProps {
  executionMode: string | undefined
  lifecycle: string | undefined
}

/** States how this run executes, and what a Created manual run is waiting for. */
export function RunExecutionModeNotice({ executionMode, lifecycle }: RunExecutionModeNoticeProps) {
  const disclosure = deriveRunExecutionModeDisclosure(executionMode, lifecycle)
  return (
    <div className="dc-run-execution-mode" data-mode={disclosure.kind}>
      <span className="dc-badge">{disclosure.label}</span>
      {disclosure.waitingHint ? <span className="dc-run-execution-mode-hint">{disclosure.waitingHint}</span> : null}
    </div>
  )
}
