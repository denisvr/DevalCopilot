import { useCollaborationHistory } from '../hooks/useCollaborationHistory'
import { messageTypeLabel, participantLabel, protocolVersionLabel, provenanceLabel } from '../collaborationHistory'
import type { CollaborationHistoryEntry } from '../collaborationHistory'
import { toUtcText } from '../utcText'

const NOTE =
  'Recorded collaboration lists the collaboration messages the host stored for this run, newest first by recorded sequence. These are recorded facts, not live status, not a current approval and not proof that any provider succeeded. A message whose recorded details are not recognized, safe and within the recorded bounds is shown by its header only, with its content marked unavailable. Sequence numbers can skip, and the recorded time does not decide the order. Each page was read when it was requested, so the list is not a snapshot of the whole run and shows no total.'
const UNAVAILABLE = 'Content unavailable: the recorded content is not recognized, coherent, safe or within the recorded bounds, so none of it is shown.'

const MESSAGES = {
  first: {
    read: 'The recorded collaboration could not be read.',
    invalid: 'The host answered with recorded collaboration that is not coherent, so none of it is shown.',
  },
  older: {
    read: 'The older messages could not be read. The messages already shown are unchanged.',
    invalid: 'The host answered with older messages that are not coherent, so none of that page is shown. The messages already shown are unchanged.',
  },
} as const

interface CollaborationHistoryPanelProps {
  /** The historical run whose recorded collaboration this reads; null when none. */
  runId: string | null
}

function Message({ row }: { row: CollaborationHistoryEntry }) {
  return (
    <li>
      <p>
        <span className="dc-collaboration-history-sequence">Sequence {row.sequence}</span>{' '}
        <span className="dc-collaboration-history-type">{messageTypeLabel(row.type)}</span>{' '}
        <span>{protocolVersionLabel(row.protocolVersion)}</span> <span>{provenanceLabel(row.provenance)}</span>{' '}
        <span>{toUtcText(row.occurredAtUtc)}</span>
      </p>
      <p>
        From {participantLabel(row.actor)} to {participantLabel(row.recipient)}
      </p>
      {row.content.status === 'Available' ? (
        <>
          <p className="dc-collaboration-history-summary">Summary: {row.content.summary}</p>
          <dl>
            {row.content.fields.map((field) => (
              <div key={field.name}>
                <dt>{field.label}</dt>
                <dd>{field.value}</dd>
              </div>
            ))}
          </dl>
        </>
      ) : (
        <p className="dc-collaboration-history-unavailable">{UNAVAILABLE}</p>
      )}
      <p className="dc-collaboration-history-id">Message {row.id}</p>
    </li>
  )
}

/**
 * Read-only, on-demand recorded collaboration of one historical run, newest first (ADR-0035). It is collapsed for every run until the
 * owner opens it, then reads one page; older pages, a retry of a failed older page and a reload of the newest page are explicit
 * actions and nothing is polled. Messages, reads and errors belong to the committed run and open lifetime, so another run, closing,
 * reopening or reloading shows none of the previous ones. There is no message selection, reply resolution, evidence lookup or
 * mutation control, and every value is rendered as plain text.
 */
export function CollaborationHistoryPanel({ runId }: CollaborationHistoryPanelProps) {
  const { open, setOpen, rows, pending, failure, failedPage, hasMore, loadOlder, reload } = useCollaborationHistory(runId)

  return (
    <section className="dc-collaboration-history" aria-label="Recorded collaboration">
      <button type="button" aria-expanded={open} onClick={() => setOpen(!open)}>
        {open ? 'Hide recorded collaboration' : 'Show recorded collaboration'}
      </button>
      {open ? (
        <div className="dc-collaboration-history-body">
          <p className="dc-local-commit-note">{NOTE}</p>
          <p>
            <button type="button" onClick={reload}>
              Reload newest messages
            </button>
          </p>
          {pending === 'first' ? <p className="dc-empty-state">Loading recorded collaboration…</p> : null}
          {failure !== null && failedPage !== null ? (
            <p className="dc-collaboration-history-error" role="alert">
              {MESSAGES[failedPage][failure]}
            </p>
          ) : null}
          {pending === null && failure === null && rows.length === 0 ? (
            <p className="dc-empty-state">No collaboration messages have been recorded for this run.</p>
          ) : null}
          {rows.length > 0 ? (
            <ol className="dc-collaboration-history-items">
              {rows.map((row) => (
                <Message key={row.id} row={row} />
              ))}
            </ol>
          ) : null}
          {pending === 'older' ? <p className="dc-empty-state">Loading older messages…</p> : null}
          {failure !== null && failedPage === 'older' ? (
            <button type="button" onClick={loadOlder}>
              Try the older messages again
            </button>
          ) : null}
          {hasMore && failure === null ? (
            <button type="button" onClick={loadOlder}>
              Load older messages
            </button>
          ) : null}
        </div>
      ) : null}
    </section>
  )
}
