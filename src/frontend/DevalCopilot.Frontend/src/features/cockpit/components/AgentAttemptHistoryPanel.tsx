import type { AgentAttemptHistoryEntryResponse } from '../../../api/clients'
import { useAgentAttemptHistory } from '../hooks/useAgentAttemptHistory'
import { AgentAttemptDetail } from './AgentAttemptDetail'
import { describeRepairLineage } from '../describeRepairLineage'
import { toAgentAttemptIdentityExpectation } from '../agentAttemptHistory'

const MESSAGES = {
  first: {
    read: 'The Agent attempt history could not be loaded.',
    invalid: 'The host answered with an Agent attempt history that is not coherent, so none of it is shown.',
  },
  older: {
    read: 'The older attempts could not be loaded. The attempts already shown are unchanged.',
    invalid: 'The host answered with older attempts that are not coherent, so none of that page is shown. The attempts already shown are unchanged.',
  },
} as const

function describeEntry(entry: AgentAttemptHistoryEntryResponse): string {
  if (entry.identityValid !== true) {
    return `Attempt #${entry.attemptNumber} · ${entry.status ?? 'status not readable'} · identity could not be verified`
  }

  const outcome = entry.outcome ? ` · Result: ${entry.outcome}` : ''
  const lineage = describeRepairLineage(entry.repairSourceAttemptId, entry.repairSourceAttemptNumber)
  return `Attempt #${entry.attemptNumber} · ${entry.role} · ${entry.provider} · ${entry.status}${outcome}${lineage ? ` · ${lineage}` : ''}`
}

/**
 * Read-only, on-demand history of one run's Agent attempts, newest first, with a single selected attempt drill-down. It serves the
 * current run in the usage rail and a selected historical run in Run history alike: nothing is fetched until the owner opens it, older
 * pages are explicit, evidence is read only for a selected attempt and raw artifact text only after a purpose is chosen and loaded.
 * Every page, selection, evidence read and text window belongs to the committed run and its open lifetime (see
 * `useAgentAttemptHistory`), so another run, a return to an earlier one, closing, reopening or unmounting shows none of the previous
 * ones. It mounts no mutation, live, eligibility or configuration control, and every value is rendered as text.
 */
export function AgentAttemptHistoryPanel({ runId }: { runId: string | null }) {
  const { open, setOpen, rows, pending, failure, failedPage, hasMore, loadOlder, retry, selected, selectionKey, select, closeSelected } =
    useAgentAttemptHistory(runId)
  const expected = selected === null ? null : toAgentAttemptIdentityExpectation(selected)

  return (
    <section className="dc-attempt-history" aria-label="Agent attempt history">
      <button type="button" aria-expanded={open} onClick={() => setOpen(!open)}>
        {open ? 'Hide Agent attempt history' : 'Show Agent attempt history'}
      </button>
      {open && runId !== null ? (
        <div className="dc-attempt-history-list">
          {pending === 'first' ? <p className="dc-empty-state">Loading attempt history…</p> : null}
          {pending === null && failure === null && rows.length === 0 ? (
            <p className="dc-empty-state">No Agent attempts have been recorded for this run.</p>
          ) : null}
          {rows.length > 0 ? (
            <ol className="dc-attempt-history-items">
              {rows.map((entry) => (
                <li key={entry.attemptId}>
                  <span>{describeEntry(entry)}</span>{' '}
                  {entry.identityValid === true && (
                    <button
                      type="button"
                      aria-label={`Inspect attempt ${entry.attemptNumber}`}
                      aria-pressed={selected?.attemptId === entry.attemptId}
                      onClick={() => select(entry.attemptId ?? '')}
                    >
                      Inspect
                    </button>
                  )}
                </li>
              ))}
            </ol>
          ) : null}
          {failure !== null && failedPage !== null ? (
            <p className="dc-card-evidence-error" role="status">
              {MESSAGES[failedPage][failure]}{' '}
              <button type="button" onClick={retry}>
                Retry
              </button>
            </p>
          ) : null}
          {pending === 'older' ? <p className="dc-empty-state">Loading older attempts…</p> : null}
          {hasMore ? (
            <button type="button" onClick={loadOlder}>
              Load older attempts
            </button>
          ) : null}
          {expected !== null && selectionKey !== null ? (
            <AgentAttemptDetail selectionKey={selectionKey} runId={runId} expected={expected} onClose={closeSelected} />
          ) : null}
        </div>
      ) : null}
    </section>
  )
}
