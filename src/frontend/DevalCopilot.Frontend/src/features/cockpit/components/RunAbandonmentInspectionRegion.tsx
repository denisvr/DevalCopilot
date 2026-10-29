import { useMemo } from 'react'
import { useRunAbandonmentInspection } from '../hooks/useRunAbandonmentInspection'
import type { RunAbandonmentInspectionRecorded } from '../runAbandonmentInspection'
import {
  RUN_ABANDONMENT_INSPECTION_FAILURES,
  RUN_ABANDONMENT_INSPECTION_HIDE_LABEL,
  RUN_ABANDONMENT_INSPECTION_NOTE,
  RUN_ABANDONMENT_INSPECTION_NOT_RECORDED,
  RUN_ABANDONMENT_INSPECTION_READING,
  RUN_ABANDONMENT_INSPECTION_RECORDED_NOTE,
  RUN_ABANDONMENT_INSPECTION_REGION_LABEL,
  RUN_ABANDONMENT_INSPECTION_RELOAD_LABEL,
  RUN_ABANDONMENT_INSPECTION_UNAVAILABLE,
} from '../runAbandonmentInspectionCopy'

function RecordedView({ abandonment }: { abandonment: RunAbandonmentInspectionRecorded }) {
  return (
    <div className="dc-run-abandonment-inspection-recorded">
      <p className="dc-local-commit-note">{RUN_ABANDONMENT_INSPECTION_RECORDED_NOTE}</p>
      <dl>
        <div>
          <dt>Recorded reason</dt>
          <dd style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{abandonment.reason}</dd>
        </div>
        <div>
          <dt>Recorded abandoned at (UTC)</dt>
          <dd>{abandonment.abandonedAtUtc}</dd>
        </div>
        <div>
          <dt>Supporting event</dt>
          <dd>
            {abandonment.eventId} · sequence {abandonment.eventSequence}
          </dd>
        </div>
        <div>
          <dt>Recorded project</dt>
          <dd>{abandonment.projectId}</dd>
        </div>
      </dl>
    </div>
  )
}

interface RunAbandonmentInspectionRegionProps {
  runId: string
  /** The project of the selected history entry: a region of another project or run never shares state with this one. */
  projectId: string
}

/**
 * The collapsed, explicit **Inspect recorded abandonment** region of one selected historical run, for every selected run regardless of
 * its listed lifecycle or receipt source (ADR-0071). Opening or reloading reads the recorded abandonment once; a closed region reads
 * nothing, and nothing polls, retries or refreshes it. It describes what was recorded, never present eligibility, ownership, the
 * workspace or publication, and it mounts no abandon, cancel, repair, release, retry, intake, export or live-status control. Every host
 * value is rendered as literal text, the reason with its own line feeds preserved and never interpreted.
 */
export function RunAbandonmentInspectionRegion({ runId, projectId }: RunAbandonmentInspectionRegionProps) {
  const ownerKey = useMemo(() => JSON.stringify([projectId, runId]), [projectId, runId])
  const target = useMemo(() => ({ runId, projectId }), [runId, projectId])
  const { open, pending, answer, failure, show, close, reload } = useRunAbandonmentInspection(ownerKey, target)

  return (
    <section className="dc-run-abandonment-inspection" aria-label={RUN_ABANDONMENT_INSPECTION_REGION_LABEL}>
      <button type="button" aria-expanded={open} onClick={open ? close : show}>
        {open ? RUN_ABANDONMENT_INSPECTION_HIDE_LABEL : RUN_ABANDONMENT_INSPECTION_REGION_LABEL}
      </button>
      {open ? (
        <div className="dc-local-inspection-body">
          {RUN_ABANDONMENT_INSPECTION_NOTE.map((line) => (
            <p key={line} className="dc-local-commit-note">
              {line}
            </p>
          ))}
          <p>
            <button type="button" onClick={reload}>
              {RUN_ABANDONMENT_INSPECTION_RELOAD_LABEL}
            </button>
          </p>
          {pending ? <p className="dc-empty-state">{RUN_ABANDONMENT_INSPECTION_READING}</p> : null}
          {failure !== null ? (
            <p className="dc-card-evidence-error" role="alert">
              {RUN_ABANDONMENT_INSPECTION_FAILURES[failure]}
            </p>
          ) : null}
          {answer?.status === 'NotRecorded' ? (
            <p className="dc-empty-state" role="status">
              {RUN_ABANDONMENT_INSPECTION_NOT_RECORDED}
            </p>
          ) : null}
          {answer?.status === 'Unavailable' ? (
            <p className="dc-empty-state" role="status">
              {RUN_ABANDONMENT_INSPECTION_UNAVAILABLE}
            </p>
          ) : null}
          {answer?.status === 'Recorded' ? <RecordedView abandonment={answer.abandonment} /> : null}
        </div>
      ) : null}
    </section>
  )
}
