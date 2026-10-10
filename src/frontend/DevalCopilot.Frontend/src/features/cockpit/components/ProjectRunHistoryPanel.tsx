import { deriveRunExecutionModeDisclosure } from '../deriveRunExecutionModeDisclosure'
import { useProjectRunHistory } from '../hooks/useProjectRunHistory'
import { lifecycleLabel, stageLabel } from '../projectRunHistory'
import { ProjectRunHistoryDetail } from './ProjectRunHistoryDetail'

const NOTE =
  'Run history lists the objectives this project has recorded, newest first, as the host stored them. Each entry is a snapshot of recorded metadata: it is not live progress, not the current workspace or current verification, and not a remote publication.'

const MESSAGES = {
  first: {
    read: 'The run history could not be read.',
    invalid: 'The host answered with a run history that is not coherent, so none of it is shown.',
  },
  older: {
    read: 'The older runs could not be read. The runs already shown are unchanged.',
    invalid: 'The host answered with older runs that are not coherent, so none of that page is shown. The runs already shown are unchanged.',
  },
} as const

interface ProjectRunHistoryPanelProps {
  /** The selected project; null when none is selected. */
  projectId: string | null
}

/**
 * Read-only, on-demand history of the selected project's recorded runs, newest first (ADR-0033). It is collapsed for every project
 * until the owner opens it, then reads one page; older pages and a reload are explicit actions and nothing is polled. Selecting a
 * row shows its recorded metadata and, when the host located a delivery source, the existing local-delivery receipt. Rows,
 * selection and reads belong to the committed project and open lifetime, so another project, closing, reopening or reloading shows
 * none of the previous ones. It mounts no live cockpit, eligibility, configuration or mutation control.
 */
export function ProjectRunHistoryPanel({ projectId }: ProjectRunHistoryPanelProps) {
  const history = useProjectRunHistory(projectId)
  const { open, setOpen, rows, pending, failure, failedPage, hasMore, loadOlder, reload, selectedRunId, select, closeSelected } = history
  const selected = rows.find((row) => row.runId === selectedRunId) ?? null

  return (
    <section className="dc-run-history" aria-label="Run history">
      <button type="button" aria-expanded={open} onClick={() => setOpen(!open)}>
        {open ? 'Hide run history' : 'Show run history'}
      </button>
      {open ? (
        <div className="dc-run-history-body">
          <p className="dc-local-commit-note">{NOTE}</p>
          <p>
            <button type="button" onClick={reload}>
              Reload latest runs
            </button>
          </p>
          {pending === 'first' ? <p className="dc-empty-state">Loading run history…</p> : null}
          {failure !== null && failedPage !== null ? (
            <p className="dc-run-history-error" role="alert">
              {MESSAGES[failedPage][failure]}
            </p>
          ) : null}
          {pending === null && failure === null && rows.length === 0 ? (
            <p className="dc-empty-state">No runs have been recorded for this project.</p>
          ) : null}
          {rows.length > 0 ? (
            <ol className="dc-run-history-items">
              {rows.map((row) => (
                <li key={row.runId}>
                  <span className="dc-run-history-number">Run #{row.executionNumber}</span>{' '}
                  <span className="dc-run-history-objective">{row.objective}</span>{' '}
                  <span>
                    {lifecycleLabel(row.lifecycle)} · {stageLabel(row.stage)}
                  </span>{' '}
                  <span>{deriveRunExecutionModeDisclosure(row.executionMode).label}</span>{' '}
                  <button
                    type="button"
                    aria-label={`Inspect run #${row.executionNumber}`}
                    aria-pressed={selectedRunId === row.runId}
                    onClick={() => select(row.runId)}
                  >
                    Inspect
                  </button>
                </li>
              ))}
            </ol>
          ) : null}
          {pending === 'older' ? <p className="dc-empty-state">Loading older runs…</p> : null}
          {failure !== null && failedPage === 'older' ? (
            <button type="button" onClick={loadOlder}>
              Try the older runs again
            </button>
          ) : null}
          {hasMore && failure === null ? (
            <button type="button" onClick={loadOlder}>
              Load older runs
            </button>
          ) : null}
          {selected ? <ProjectRunHistoryDetail entry={selected} onClose={closeSelected} /> : null}
        </div>
      ) : null}
    </section>
  )
}
