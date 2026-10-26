import { useProjectVerificationInspection } from '../hooks/useProjectVerificationInspection'
import {
  PROJECT_VERIFICATION_EMPTY,
  PROJECT_VERIFICATION_HIDE_LABEL,
  PROJECT_VERIFICATION_NOTE,
  PROJECT_VERIFICATION_OLDER_LABEL,
  PROJECT_VERIFICATION_PAGE_FAILURES,
  PROJECT_VERIFICATION_PAGE_UNAVAILABLE,
  PROJECT_VERIFICATION_READING,
  PROJECT_VERIFICATION_RELOAD_LABEL,
  PROJECT_VERIFICATION_REGION_LABEL,
} from '../projectVerificationInspectionCopy'
import { ProjectVerificationInspectionDetail } from './ProjectVerificationInspectionDetail'

/**
 * The collapsed, explicit **Inspect recorded verifications** region of one project's verification area (ADR-0069), independent of the
 * current-run controls and of the live latest-20 list and its output viewer. Opening reads only the newest page of 20; **Older
 * verifications** replaces the page by the next older one without accumulating history; **Reload recorded verifications** clears the
 * selection and its content and reads the newest page again. Choosing one execution reads only its metadata, and only an explicit stream
 * choice and Load reads a window. It attributes no execution to a run, never polls, never enables or disables Run or any
 * configuration action, and mounts no execution, recovery, approval or publication control. Every host value is rendered as literal text.
 */
export function ProjectVerificationInspectionPanel({ projectId }: { projectId: string | null }) {
  const inspection = useProjectVerificationInspection(projectId)
  const { open, pending, page, failure, show, close, older, reload, retryPage, selectedItem, selectExecution, clearExecution } = inspection
  const items = page?.status === 'Recorded' ? page.items : null
  const first = items?.at(0)
  const last = items?.at(-1)

  return (
    <section className="dc-project-verification-inspection" aria-label={PROJECT_VERIFICATION_REGION_LABEL}>
      <button type="button" aria-expanded={open} onClick={open ? close : show} disabled={projectId === null}>
        {open ? PROJECT_VERIFICATION_HIDE_LABEL : PROJECT_VERIFICATION_REGION_LABEL}
      </button>
      {open && projectId !== null ? (
        <div className="dc-project-verification-inspection-body">
          <p className="dc-local-commit-note">{PROJECT_VERIFICATION_NOTE}</p>
          <p>
            <button type="button" onClick={reload}>
              {PROJECT_VERIFICATION_RELOAD_LABEL}
            </button>
          </p>
          {pending ? <p className="dc-empty-state">{PROJECT_VERIFICATION_READING}</p> : null}
          {failure !== null ? (
            <p className="dc-card-evidence-error" role="alert">
              {PROJECT_VERIFICATION_PAGE_FAILURES[failure]}{' '}
              <button type="button" onClick={retryPage}>
                Try the recorded verifications again
              </button>
            </p>
          ) : null}
          {page?.status === 'Unavailable' ? (
            <p className="dc-empty-state" role="status">
              {PROJECT_VERIFICATION_PAGE_UNAVAILABLE}
            </p>
          ) : null}
          {items !== null && items.length === 0 ? <p className="dc-empty-state">{PROJECT_VERIFICATION_EMPTY}</p> : null}
          {items !== null && items.length > 0 && first !== undefined && last !== undefined ? (
            <div>
              <p>
                Recorded verifications #{first.executionNumber} to #{last.executionNumber}.
              </p>
              <ul className="dc-project-verification-items">
                {items.map((item) => (
                  <li key={item.executionId}>
                    <span>
                      Verification #{item.executionNumber} · {item.commandName} · claimed {item.claimedAtUtc}
                    </span>{' '}
                    <button
                      type="button"
                      aria-label={`${selectedItem?.executionId === item.executionId ? 'Hide' : 'Inspect'} verification #${item.executionNumber}`}
                      aria-pressed={selectedItem?.executionId === item.executionId}
                      onClick={selectedItem?.executionId === item.executionId ? clearExecution : () => selectExecution(item.executionId)}
                    >
                      {selectedItem?.executionId === item.executionId ? 'Hide' : 'Inspect'}
                    </button>
                  </li>
                ))}
              </ul>
              {page?.status === 'Recorded' && page.hasMore ? (
                <p>
                  <button type="button" onClick={older}>
                    {PROJECT_VERIFICATION_OLDER_LABEL}
                  </button>
                </p>
              ) : null}
              {selectedItem !== null ? <ProjectVerificationInspectionDetail inspection={inspection} item={selectedItem} /> : null}
            </div>
          ) : null}
        </div>
      ) : null}
    </section>
  )
}
