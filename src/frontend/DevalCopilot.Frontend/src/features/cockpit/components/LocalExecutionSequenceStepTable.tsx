import type { LocalExecutionStep } from '../localExecutionSequence'
import {
  LOCAL_EXECUTION_QUARANTINE_NOTE,
  LOCAL_EXECUTION_STEP_KIND_LABEL,
  LOCAL_EXECUTION_STEP_STATE_LABEL,
  describeLocalExecutionStepOperation,
} from '../localExecutionSequenceCopy'

const NONE = '—'

/**
 * The bounded step receipts of a local execution sequence. Every cell is literal text: the recipe name is untrusted and is never
 * interpreted. A claim, a dispatch marker or a recorded exit is worded as bookkeeping, and a quarantined operation is never described
 * as an actively running process.
 */
export function LocalExecutionSequenceStepTable({ steps }: { steps: readonly LocalExecutionStep[] }) {
  if (steps.length === 0) {
    return <p className="dc-local-execution-steps-empty">No operation has been reserved.</p>
  }

  return (
    <table className="dc-local-execution-steps" aria-label="Local execution sequence steps">
      <thead>
        <tr>
          <th scope="col">Step</th>
          <th scope="col">Kind</th>
          <th scope="col">Pass</th>
          <th scope="col">Recipe</th>
          <th scope="col">State</th>
          <th scope="col">Operation receipt</th>
          <th scope="col">Result deadline (UTC)</th>
        </tr>
      </thead>
      <tbody>
        {steps.map((step) => (
          <tr key={step.stepId} data-step-state={step.state} data-quarantined={step.quarantined}>
            <td>{step.ordinal}</td>
            <td>{LOCAL_EXECUTION_STEP_KIND_LABEL[step.kind]}</td>
            <td>{step.passNumber}</td>
            <td>{step.recipeOrdinal === null ? NONE : `#${step.recipeOrdinal + 1} ${step.recipeName ?? ''}`}</td>
            <td>{LOCAL_EXECUTION_STEP_STATE_LABEL[step.state]}</td>
            <td>
              {describeLocalExecutionStepOperation(step)}
              {step.quarantined && <span className="dc-local-execution-quarantine"> · {LOCAL_EXECUTION_QUARANTINE_NOTE}</span>}
            </td>
            <td>{step.resultDeadlineUtc ?? NONE}</td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
