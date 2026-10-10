import { useRunEventHistory } from '../hooks/useRunEventHistory'
import { actorLabel, eventTypeLabel } from '../runEventHistory'
import { toUtcText } from '../utcText'

const NOTE =
  'Recorded events lists the event headers the host stored for this run, newest first by recorded sequence. Only each header is shown: the details of an event are omitted. These are recorded facts, not live status, not a current approval and not a complete replay. Sequence numbers can skip, and the recorded time does not decide the order. Each page was read when it was requested, so the list is not a snapshot of the whole run and shows no total.'

const MESSAGES = {
  first: {
    read: 'The recorded events could not be read.',
    invalid: 'The host answered with recorded events that are not coherent, so none of them are shown.',
  },
  older: {
    read: 'The older events could not be read. The events already shown are unchanged.',
    invalid: 'The host answered with older events that are not coherent, so none of that page is shown. The events already shown are unchanged.',
  },
} as const

interface RunEventHistoryPanelProps {
  /** The historical run whose recorded events this reads; null when none. */
  runId: string | null
}

/**
 * Read-only, on-demand journal headers of one historical run, newest first (ADR-0034). It is collapsed for every run until the owner
 * opens it, then reads one page; older pages, a retry of a failed older page and a reload of the newest page are explicit actions and
 * nothing is polled. Headers, reads and errors belong to the committed run and open lifetime, so another run, closing, reopening or
 * reloading shows none of the previous ones. There is no event selection, payload viewer, playback or live control, and every value
 * is rendered as text.
 */
export function RunEventHistoryPanel({ runId }: RunEventHistoryPanelProps) {
  const { open, setOpen, rows, pending, failure, failedPage, hasMore, loadOlder, reload } = useRunEventHistory(runId)

  return (
    <section className="dc-run-event-history" aria-label="Recorded events">
      <button type="button" aria-expanded={open} onClick={() => setOpen(!open)}>
        {open ? 'Hide recorded events' : 'Show recorded events'}
      </button>
      {open ? (
        <div className="dc-run-event-history-body">
          <p className="dc-local-commit-note">{NOTE}</p>
          <p>
            <button type="button" onClick={reload}>
              Reload newest events
            </button>
          </p>
          {pending === 'first' ? <p className="dc-empty-state">Loading recorded events…</p> : null}
          {failure !== null && failedPage !== null ? (
            <p className="dc-run-event-history-error" role="alert">
              {MESSAGES[failedPage][failure]}
            </p>
          ) : null}
          {pending === null && failure === null && rows.length === 0 ? (
            <p className="dc-empty-state">No events have been recorded for this run.</p>
          ) : null}
          {rows.length > 0 ? (
            <ol className="dc-run-event-history-items">
              {rows.map((row) => (
                <li key={row.id}>
                  <span className="dc-run-event-history-sequence">Sequence {row.sequence}</span>{' '}
                  <span className="dc-run-event-history-type">{eventTypeLabel(row.eventType)}</span>{' '}
                  <span>{actorLabel(row.actor)}</span> <span>{toUtcText(row.occurredAtUtc)}</span>{' '}
                  <span className="dc-run-event-history-id">Event {row.id}</span>
                </li>
              ))}
            </ol>
          ) : null}
          {pending === 'older' ? <p className="dc-empty-state">Loading older events…</p> : null}
          {failure !== null && failedPage === 'older' ? (
            <button type="button" onClick={loadOlder}>
              Try the older events again
            </button>
          ) : null}
          {hasMore && failure === null ? (
            <button type="button" onClick={loadOlder}>
              Load older events
            </button>
          ) : null}
        </div>
      ) : null}
    </section>
  )
}
