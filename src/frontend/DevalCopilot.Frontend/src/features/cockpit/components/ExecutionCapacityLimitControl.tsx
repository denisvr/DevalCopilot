import { useState } from 'react'
import { EXECUTION_CAPACITY_CHANGE_DISCLOSURE, describeExecutionCapacityChange } from '../executionCapacity'
import type { ExecutionCapacityLimit } from '../executionCapacity'
import type { UseExecutionCapacityResult } from '../hooks/useExecutionCapacity'

interface ExecutionCapacityLimitControlProps {
  /** The host session's capacity lifetime; the control only reads it, applies through it and refreshes it. */
  capacity: UseExecutionCapacityResult
}

const WORDS: Readonly<Record<ExecutionCapacityLimit, string>> = { 1: 'one', 2: 'two' }
const CHOICES: readonly ExecutionCapacityLimit[] = [1, 2]

/**
 * The one global control of the recorded-execution limit (ADR-0059), shown once near the top of the host session and independent of any
 * selected project: the validated current limit, the one-or-two choice, an explicit Apply and Refresh and fixed English feedback.
 * Choosing applies nothing, changing projects applies nothing, and a lower-limit refusal is worded as exactly that, never as a saved
 * pending reduction. It carries no host text, identifier or path, and it starts, cancels and releases nothing.
 */
export function ExecutionCapacityLimitControl({ capacity }: ExecutionCapacityLimitControlProps) {
  const [choice, setChoice] = useState<ExecutionCapacityLimit | null>(null)
  const current = capacity.reading?.limit ?? null
  const selected = choice ?? current
  const busy = capacity.change.phase !== 'idle'
  const feedback = describeExecutionCapacityChange(capacity.change, capacity.reading)

  return (
    <section className="dc-capacity-control" aria-label="Recorded-execution limit">
      <span className="dc-capacity-control-current">
        {current === null ? 'Current limit: unknown' : `Current limit: ${WORDS[current]}`}
      </span>
      <fieldset className="dc-capacity-control-choice" disabled={busy}>
        <legend className="dc-visually-hidden">Choose the recorded-execution limit</legend>
        {CHOICES.map((limit) => (
          <label key={limit}>
            <input
              type="radio"
              name="execution-capacity-limit"
              checked={selected === limit}
              disabled={busy}
              onChange={() => setChoice(limit)}
            />
            {limit === 1 ? 'One' : 'Two'}
          </label>
        ))}
      </fieldset>
      <button
        type="button"
        className="dc-button"
        data-variant="primary"
        disabled={busy || selected === null}
        onClick={() => selected !== null && capacity.applyLimit(selected)}
      >
        Apply limit
      </button>
      <button type="button" className="dc-button" onClick={() => capacity.refresh()}>
        Refresh limit
      </button>
      {feedback !== null && (
        <p className="dc-capacity-control-feedback" role="status">
          {feedback}
        </p>
      )}
      <p className="dc-workspace-evidence-empty dc-capacity-control-note">{EXECUTION_CAPACITY_CHANGE_DISCLOSURE}</p>
    </section>
  )
}
