import {
  describeAttemptInputMessage,
  describeAttemptInputMessagesStatus,
  type AttemptInputMessagesEvidenceView,
} from '../describeAttemptInputMessages'
import type { CollaborationTimelineCard } from '../types'

interface AttemptInputMessagesProps {
  evidence: AttemptInputMessagesEvidenceView | null | undefined
  cardsById: ReadonlyMap<string, CollaborationTimelineCard>
}

/**
 * Renders the exact, ordered collaboration-message references the producing Agent attempt was
 * launched against, labeled honestly as **recorded collaboration inputs** — never the complete
 * prompt, complete context manifest, or a resumable provider session. Reused across every attempt
 * shape (a Planner attempt legitimately shows the empty state; a Resolver or Implementer shows its
 * real ordered set). An `Invalid` set is never partially shown — the whole section states the
 * verification failure instead of listing an untrustworthy subset.
 */
export function AttemptInputMessages({ evidence, cardsById }: AttemptInputMessagesProps) {
  const status = evidence?.inputMessagesStatus
  const message = describeAttemptInputMessagesStatus(status)

  if (message) {
    return (
      <p className="dc-attempt-input-messages-empty" data-input-messages-status={status ?? 'Unknown'}>
        {message}
      </p>
    )
  }

  const entries = evidence?.inputMessages ?? []

  return (
    <div className="dc-attempt-input-messages" data-input-messages-status="Recorded">
      <p className="dc-attempt-input-messages-caveat">
        Recorded collaboration inputs — the durable messages this attempt was launched against, never the complete prompt,
        complete context manifest, or a resumable provider session.
      </p>
      <ol>
        {entries.map((entry) => (
          <li key={entry.collaborationMessageId ?? entry.sequence}>{describeAttemptInputMessage(entry, cardsById)}</li>
        ))}
      </ol>
      {evidence?.inputMessagesOmitted && (
        <p className="dc-empty-state">
          {evidence.inputMessageTotalCount ?? entries.length} recorded inputs in total; only the first {entries.length} are shown.
        </p>
      )}
    </div>
  )
}
