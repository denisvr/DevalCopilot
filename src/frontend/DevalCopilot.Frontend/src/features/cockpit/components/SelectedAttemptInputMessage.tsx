import type { AgentAttemptInputMessage } from '../agentAttemptInputMessage'
import type { AgentAttemptInputReference } from '../agentAttemptInputs'
import { messageTypeLabel, participantLabel, protocolVersionLabel, provenanceLabel } from '../collaborationHistory'
import type { AgentAttemptInputMessageFailure } from '../hooks/useAgentAttemptInputMessage'

const NOTE =
  'This is the stored collaboration message this input references, read now when requested. It is not the exact or complete prompt, '
  + 'not all the context and not proof of what the Agent saw, and it is neither a current approval nor proof that any provider succeeded.'
const SET_UNAVAILABLE =
  'Recorded input unavailable: the recorded inputs of this attempt could not be verified as a whole, so no content is shown. This does '
  + 'not mean the attempt had no inputs.'
const CONTENT_UNAVAILABLE =
  'Content unavailable: the recorded content is not recognized, coherent, safe or within the recorded bounds, so none of it is shown. '
  + 'The recorded input reference itself is unchanged.'
const FAILURES: Record<AgentAttemptInputMessageFailure, string> = {
  read: 'The recorded input could not be read.',
  'not-found': 'This input is no longer recorded for this attempt.',
  invalid: 'The host answered with a recorded input that is not coherent for this selection, so none of it is shown.',
}

interface SelectedAttemptInputMessageProps {
  reference: AgentAttemptInputReference
  pending: boolean
  answer: AgentAttemptInputMessage | null
  failure: AgentAttemptInputMessageFailure | null
  onClose: () => void
  onRetry: () => void
}

/**
 * The one explicit content inspector of a selected recorded input (ADR-0053): the selected input's identity, then, once read, the
 * recorded message's participants, provenance, summary and admitted fields as literal text in the closed schema's order and fixed
 * labels. Content unavailable, an unavailable input set, not found, an incoherent answer and a read failure are distinct fixed states;
 * none names a rejected stored value. It links nothing and follows no reply, attempt or artifact.
 */
export function SelectedAttemptInputMessage({ reference, pending, answer, failure, onClose, onRetry }: SelectedAttemptInputMessageProps) {
  return (
    <section className="dc-attempt-input-message" aria-label="Selected recorded input">
      <p className="dc-attempt-input-message-title">
        Recorded input {reference.position + 1} · {messageTypeLabel(reference.type)} · message sequence {reference.collaborationSequence}
      </p>
      <p className="dc-local-commit-note">{NOTE}</p>
      {pending ? <p className="dc-empty-state">Loading the recorded input…</p> : null}
      {failure !== null ? (
        <p className="dc-card-evidence-error" role="status">
          {FAILURES[failure]}{' '}
          <button type="button" onClick={onRetry}>
            Try the recorded input again
          </button>
        </p>
      ) : null}
      {answer?.status === 'Unavailable' ? (
        <p className="dc-empty-state" role="status">
          {SET_UNAVAILABLE}
        </p>
      ) : null}
      {answer?.status === 'Recorded' ? (
        <div className="dc-attempt-input-message-body">
          <p>
            Sequence {answer.message.sequence} · {messageTypeLabel(answer.message.type)} · {protocolVersionLabel(answer.message.protocolVersion)}
            {' · '}
            {provenanceLabel(answer.message.provenance)} · recorded {answer.message.occurredAtUtc}
          </p>
          <p>
            From {participantLabel(answer.message.actor)} to {participantLabel(answer.message.recipient)}
          </p>
          {answer.message.content.status === 'Available' ? (
            <>
              <p className="dc-attempt-input-message-summary">Summary: {answer.message.content.summary}</p>
              <dl>
                {answer.message.content.fields.map((field) => (
                  <div key={field.name}>
                    <dt>{field.label}</dt>
                    <dd>{field.value}</dd>
                  </div>
                ))}
              </dl>
            </>
          ) : (
            <p className="dc-attempt-input-message-unavailable" role="status">
              {CONTENT_UNAVAILABLE}
            </p>
          )}
          <p className="dc-attempt-input-message-id">Message {answer.message.id}</p>
        </div>
      ) : null}
      <button type="button" onClick={onClose}>
        Close recorded input
      </button>
    </section>
  )
}
