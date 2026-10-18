import type { AgentAttemptIdentityExpectation } from '../agentAttemptHistory'
import type { AgentAttemptInputReference } from '../agentAttemptInputs'
import { typeLabelFor } from '../collaborationMessageTypeLabel'
import { RECORDED_INPUTS_CAVEAT } from '../describeAttemptInputMessages'
import { useAgentAttemptInputs } from '../hooks/useAgentAttemptInputs'
import type { AgentAttemptInputsFailure } from '../hooks/useAgentAttemptInputs'

interface SelectedAttemptRecordedInputsProps {
  selectionKey: string
  runId: string
  expected: AgentAttemptIdentityExpectation
}

const FAILURES: Record<AgentAttemptInputsFailure, string> = {
  read: 'The recorded collaboration inputs could not be loaded.',
  'not-found': 'This attempt is no longer available.',
  invalid: 'The host answered with recorded collaboration inputs that are not coherent for this attempt, so none of them is shown.',
}

function describeReference(reference: AgentAttemptInputReference): string {
  return `Input ${reference.position + 1}: ${typeLabelFor(reference.type)} · message sequence ${reference.collaborationSequence}`
    + ` · recorded ${reference.occurredAtUtc} · message ${reference.collaborationMessageId}`
}

/**
 * The collapsed "Recorded collaboration inputs" section of the selected attempt (ADR-0051): read only when opened, retried only on
 * request, independent of the attempt's evidence, facts and artifact viewer and of any message it produced. It shows the recorded
 * references as plain text, never their content, the complete prompt, context or session, and links nothing.
 */
export function SelectedAttemptRecordedInputs({ selectionKey, runId, expected }: SelectedAttemptRecordedInputsProps) {
  const { open, pending, inputs, failure, show, close, retry } = useAgentAttemptInputs({ selectionKey, runId, expected })

  return (
    <section className="dc-attempt-recorded-inputs" aria-label="Recorded collaboration inputs">
      <button type="button" aria-expanded={open} onClick={open ? close : show}>
        Recorded collaboration inputs
      </button>
      {open ? (
        <div>
          <p className="dc-attempt-input-messages-caveat">
            {RECORDED_INPUTS_CAVEAT} Only the references are shown here; their content is not loaded.
          </p>
          {pending ? <p className="dc-empty-state">Loading recorded collaboration inputs…</p> : null}
          {failure !== null ? (
            <p className="dc-card-evidence-error" role="status">
              {FAILURES[failure]}{' '}
              <button type="button" onClick={retry}>
                Try the recorded inputs again
              </button>
            </p>
          ) : null}
          {inputs?.status === 'Unavailable' ? (
            <p className="dc-empty-state" role="status">
              Recorded collaboration inputs unavailable: they could not be verified, or there are more than this view inspects. This
              does not mean the attempt had no inputs.
            </p>
          ) : null}
          {inputs?.status === 'Empty' ? (
            <p className="dc-empty-state" role="status">
              No recorded collaboration inputs — this attempt started from none.
            </p>
          ) : null}
          {inputs?.status === 'Recorded' ? (
            <div>
              <p>
                {inputs.totalCount === 1 ? '1 recorded input.' : `${inputs.totalCount} recorded inputs.`}
                {inputs.omitted ? ` Only the first ${inputs.inputs.length} are shown.` : ''}
              </p>
              <ol className="dc-attempt-recorded-input-items">
                {inputs.inputs.map((reference) => (
                  <li key={reference.collaborationMessageId}>{describeReference(reference)}</li>
                ))}
              </ol>
            </div>
          ) : null}
        </div>
      ) : null}
    </section>
  )
}
