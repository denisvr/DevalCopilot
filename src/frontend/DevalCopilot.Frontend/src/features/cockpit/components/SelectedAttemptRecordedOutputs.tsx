import type { AgentAttemptIdentityExpectation } from '../agentAttemptHistory'
import { RECORDED_OUTPUTS_CAVEAT } from '../agentAttemptOutputs'
import type { AgentAttemptOutputReference } from '../agentAttemptOutputs'
import { typeLabelFor } from '../collaborationMessageTypeLabel'
import { AGENT_ATTEMPT_OUTPUTS_DISCLOSURE_STEP, useAgentAttemptOutputs } from '../hooks/useAgentAttemptOutputs'
import type { AgentAttemptOutputsFailure } from '../hooks/useAgentAttemptOutputs'
import { useAgentAttemptOutputMessage } from '../hooks/useAgentAttemptOutputMessage'
import { SelectedAttemptOutputMessage } from './SelectedAttemptOutputMessage'

interface SelectedAttemptRecordedOutputsProps {
  selectionKey: string
  runId: string
  expected: AgentAttemptIdentityExpectation
}

const FAILURES: Record<AgentAttemptOutputsFailure, string> = {
  read: 'The recorded collaboration outputs could not be loaded.',
  'not-found': 'This attempt is no longer available.',
  invalid: 'The host answered with recorded collaboration outputs that are not coherent for this attempt, so none of them is shown.',
}

function describeReference(reference: AgentAttemptOutputReference): string {
  return `Output ${reference.position + 1}: ${typeLabelFor(reference.type)} · message sequence ${reference.collaborationSequence}`
    + ` · recorded ${reference.occurredAtUtc} · message ${reference.collaborationMessageId}`
}

/**
 * The collapsed "Recorded collaboration outputs" section of the selected attempt (ADR-0054): read only when opened, with the whole
 * admitted set of at most 64 references requested once, retried only on request, and independent of the attempt's evidence, facts,
 * inputs, verification and artifact viewer. It reveals ten references, then further loaded references in local chunks of ten on
 * request, without another read; that local disclosure is not host truncation, which this complete answer never has. Each revealed
 * reference has one explicit "Inspect output" action into the single content inspector, which reads that one message only when chosen.
 * Everything is plain text; nothing is linked, prefetched or followed.
 */
export function SelectedAttemptRecordedOutputs({ selectionKey, runId, expected }: SelectedAttemptRecordedOutputsProps) {
  const { open, openingKey, pending, outputs, failure, visibleCount, selection, show, close, retry, showMore, select, clearSelection } =
    useAgentAttemptOutputs({ selectionKey, runId, expected })
  const message = useAgentAttemptOutputMessage({ openingKey, runId, expected, selection })

  const loaded = outputs?.status === 'Recorded' ? outputs.outputs.length : 0
  const hidden = loaded - visibleCount

  return (
    <section className="dc-attempt-recorded-outputs" aria-label="Recorded collaboration outputs">
      <button type="button" aria-expanded={open} onClick={open ? close : show}>
        Recorded collaboration outputs
      </button>
      {open ? (
        <div>
          <p className="dc-attempt-output-messages-caveat">
            {RECORDED_OUTPUTS_CAVEAT} Only the references are listed here; the content of one output is read only when you inspect it.
          </p>
          {pending ? <p className="dc-empty-state">Loading recorded collaboration outputs…</p> : null}
          {failure !== null ? (
            <p className="dc-card-evidence-error" role="status">
              {FAILURES[failure]}{' '}
              <button type="button" onClick={retry}>
                Try the recorded outputs again
              </button>
            </p>
          ) : null}
          {outputs?.status === 'Unavailable' ? (
            <p className="dc-empty-state" role="status">
              Recorded collaboration outputs unavailable: they could not be verified, or there are more than this view inspects. This
              does not mean the attempt produced no output.
            </p>
          ) : null}
          {outputs?.status === 'Empty' ? (
            <p className="dc-empty-state" role="status">
              No stored collaboration message is attributed to this attempt in this read. This does not prove that the provider produced
              no output, or whether the attempt succeeded or failed.
            </p>
          ) : null}
          {outputs?.status === 'Recorded' ? (
            <div>
              <p>{outputs.totalCount === 1 ? '1 recorded output.' : `${outputs.totalCount} recorded outputs.`}</p>
              <ol className="dc-attempt-recorded-output-items">
                {outputs.outputs.slice(0, visibleCount).map((reference) => (
                  <li key={reference.collaborationMessageId}>
                    <span className="dc-attempt-recorded-output-reference">{describeReference(reference)}</span>{' '}
                    <button
                      type="button"
                      aria-pressed={selection?.reference.collaborationMessageId === reference.collaborationMessageId}
                      onClick={() => select(reference.collaborationMessageId)}
                    >
                      Inspect output {reference.position + 1}
                    </button>
                  </li>
                ))}
              </ol>
              {hidden > 0 ? (
                <p className="dc-attempt-recorded-output-disclosure">
                  Showing {visibleCount} of the {loaded} loaded references; the others are only hidden in this view, not missing.{' '}
                  <button type="button" onClick={showMore}>
                    Show {Math.min(AGENT_ATTEMPT_OUTPUTS_DISCLOSURE_STEP, hidden)} more references
                  </button>
                </p>
              ) : null}
              {selection !== null ? (
                <SelectedAttemptOutputMessage
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
