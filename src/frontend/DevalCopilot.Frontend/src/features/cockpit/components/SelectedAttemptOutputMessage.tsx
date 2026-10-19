import type { AgentAttemptOutputMessage } from '../agentAttemptOutputMessage'
import type { AgentAttemptOutputReference } from '../agentAttemptOutputs'
import { messageTypeLabel, participantLabel, protocolVersionLabel, provenanceLabel } from '../collaborationHistory'
import type { AgentAttemptOutputMessageFailure } from '../hooks/useAgentAttemptOutputMessage'

const NOTE =
  "This is a stored collaboration message attributed to this attempt, read now when requested. It is not the attempt's complete provider "
  + 'response, not proof of what it generated or saw, and it is neither a current approval nor proof that the attempt succeeded.'
const SET_UNAVAILABLE =
  'Recorded output unavailable: the recorded outputs of this attempt could not be verified as a whole, so no content is shown. This does '
  + 'not mean the attempt produced no output.'
const CONTENT_UNAVAILABLE =
  'Content unavailable: the recorded content is not recognized, coherent, safe or within the recorded bounds, so none of it is shown. '
  + 'The recorded output reference itself is unchanged.'
const FAILURES: Record<AgentAttemptOutputMessageFailure, string> = {
  read: 'The recorded output could not be read.',
  'not-found': 'This output is no longer recorded for this attempt.',
  invalid: 'The host answered with a recorded output that is not coherent for this selection, so none of it is shown.',
}

interface SelectedAttemptOutputMessageProps {
  reference: AgentAttemptOutputReference
  pending: boolean
  answer: AgentAttemptOutputMessage | null
  failure: AgentAttemptOutputMessageFailure | null
  onClose: () => void
  onRetry: () => void
}

/**
 * The one explicit content inspector of a selected recorded output (ADR-0054): the selected output's identity, then, once read, the
 * recorded message's participants, provenance, summary and admitted fields as literal text in the closed schema's order and fixed
 * labels. Content unavailable, an unavailable output set, not found, an incoherent answer and a read failure are distinct fixed states;
 * none names a rejected stored value. It links nothing and follows no reply, attempt or artifact.
 */
export function SelectedAttemptOutputMessage({ reference, pending, answer, failure, onClose, onRetry }: SelectedAttemptOutputMessageProps) {
  return (
    <section className="dc-attempt-output-message" aria-label="Selected recorded output">
      <p className="dc-attempt-output-message-title">
        Recorded output {reference.position + 1} · {messageTypeLabel(reference.type)} · message sequence {reference.collaborationSequence}
      </p>
      <p className="dc-local-commit-note">{NOTE}</p>
      {pending ? <p className="dc-empty-state">Loading the recorded output…</p> : null}
      {failure !== null ? (
        <p className="dc-card-evidence-error" role="status">
          {FAILURES[failure]}{' '}
          <button type="button" onClick={onRetry}>
            Try the recorded output again
          </button>
        </p>
      ) : null}
      {answer?.status === 'Unavailable' ? (
        <p className="dc-empty-state" role="status">
          {SET_UNAVAILABLE}
        </p>
      ) : null}
      {answer?.status === 'Recorded' ? (
        <div className="dc-attempt-output-message-body">
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
              <p className="dc-attempt-output-message-summary">Summary: {answer.message.content.summary}</p>
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
            <p className="dc-attempt-output-message-unavailable" role="status">
              {CONTENT_UNAVAILABLE}
            </p>
          )}
          <p className="dc-attempt-output-message-id">Message {answer.message.id}</p>
        </div>
      ) : null}
      <button type="button" onClick={onClose}>
        Close recorded output
      </button>
    </section>
  )
}
