import type { AgentAttemptIdentityExpectation } from '../agentAttemptHistory'
import type { AgentAttemptInputReference } from '../agentAttemptInputs'
import { typeLabelFor } from '../collaborationMessageTypeLabel'
import { RECORDED_INPUTS_CAVEAT } from '../describeAttemptInputMessages'
import { AGENT_ATTEMPT_INPUTS_DISCLOSURE_STEP, useAgentAttemptInputs } from '../hooks/useAgentAttemptInputs'
import type { AgentAttemptInputsFailure } from '../hooks/useAgentAttemptInputs'
import { useAgentAttemptInputMessage } from '../hooks/useAgentAttemptInputMessage'
import { SelectedAttemptInputMessage } from './SelectedAttemptInputMessage'

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
 * The collapsed "Recorded collaboration inputs" section of the selected attempt (ADR-0051/0053): read only when opened, with the whole
 * admitted set of at most 64 references requested once, retried only on request, and independent of the attempt's evidence, facts and
 * artifact viewer and of any message it produced. It reveals ten references, then further loaded references in local chunks of ten on
 * request, without another read; that local disclosure is distinct from anything the host omitted or could not verify. Each revealed
 * reference has one explicit "Inspect input" action into the single content inspector, which reads that one message only when chosen.
 * Everything is plain text; nothing is linked, prefetched or followed.
 */
export function SelectedAttemptRecordedInputs({ selectionKey, runId, expected }: SelectedAttemptRecordedInputsProps) {
  const { open, openingKey, pending, inputs, failure, visibleCount, selection, show, close, retry, showMore, select, clearSelection } =
    useAgentAttemptInputs({ selectionKey, runId, expected })
  const message = useAgentAttemptInputMessage({ openingKey, runId, expected, selection })

  const loaded = inputs?.status === 'Recorded' ? inputs.inputs.length : 0
  const hidden = loaded - visibleCount

  return (
    <section className="dc-attempt-recorded-inputs" aria-label="Recorded collaboration inputs">
      <button type="button" aria-expanded={open} onClick={open ? close : show}>
        Recorded collaboration inputs
      </button>
      {open ? (
        <div>
          <p className="dc-attempt-input-messages-caveat">
            {RECORDED_INPUTS_CAVEAT} Only the references are listed here; the content of one input is read only when you inspect it.
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
                {inputs.omitted ? ` The host returned only the first ${loaded}.` : ''}
              </p>
              <ol className="dc-attempt-recorded-input-items">
                {inputs.inputs.slice(0, visibleCount).map((reference) => (
                  <li key={reference.collaborationMessageId}>
                    <span className="dc-attempt-recorded-input-reference">{describeReference(reference)}</span>{' '}
                    <button
                      type="button"
                      aria-pressed={selection?.reference.collaborationMessageId === reference.collaborationMessageId}
                      onClick={() => select(reference.collaborationMessageId)}
                    >
                      Inspect input {reference.position + 1}
                    </button>
                  </li>
                ))}
              </ol>
              {hidden > 0 ? (
                <p className="dc-attempt-recorded-input-disclosure">
                  Showing {visibleCount} of the {loaded} loaded references; the others are only hidden in this view, not missing.{' '}
                  <button type="button" onClick={showMore}>
                    Show {Math.min(AGENT_ATTEMPT_INPUTS_DISCLOSURE_STEP, hidden)} more references
                  </button>
                </p>
              ) : null}
              {selection !== null ? (
                <SelectedAttemptInputMessage
                  reference={selection.reference}
                  pending={message.pending}
                  answer={message.answer}
                  failure={message.failure}
                  onClose={clearSelection}
                  onRetry={message.retry}
                />
              ) : null}
            </div>
          ) : null}
        </div>
      ) : null}
    </section>
  )
}
