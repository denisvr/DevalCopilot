import { useLocalExecutionQueueInspection } from '../hooks/useLocalExecutionQueueInspection'
import { ADMISSION_STATE_LABEL } from '../localExecutionAdmissionHistoryCopy'
import {
  QUEUE_OVERVIEW_CLEAR_LABEL,
  QUEUE_OVERVIEW_EMPTY,
  QUEUE_OVERVIEW_FAILURES,
  QUEUE_OVERVIEW_HIDE_LABEL,
  QUEUE_OVERVIEW_NOTE,
  QUEUE_OVERVIEW_READING,
  QUEUE_OVERVIEW_REGION_LABEL,
  QUEUE_OVERVIEW_RELOAD_LABEL,
  QUEUE_OVERVIEW_SELECTION_NOTE,
  QUEUE_OVERVIEW_SELECT_LABEL,
  QUEUE_OVERVIEW_UNAVAILABLE,
} from '../localExecutionQueueInspectionCopy'
import {
  LOCAL_EXECUTION_QUEUE_RECORDED_NOTE,
  LOCAL_EXECUTION_REASON_PHRASE,
  LOCAL_EXECUTION_STATE_LABEL,
  LOCAL_EXECUTION_STEP_KIND_LABEL,
  LOCAL_EXECUTION_WAITING_REASON_LABEL,
} from '../localExecutionSequenceCopy'
import { LocalExecutionInspectionPanel } from './LocalExecutionInspectionPanel'

/**
 * The one collapsed, explicit **Inspect outstanding queue admissions** region of the authenticated host session (ADR-0064), shown once
 * beside the global recorded-execution limit and independent of any selected project. Opening or reloading reads only the recorded
 * outstanding admissions of the whole database; closing reads nothing, and nothing polls, retries or refreshes it. A row's **Select
 * recorded cycle** action renders the existing local execution inspector for exactly that run, whose own explicit actions stay
 * required: selecting it reads no operation or artifact and changes neither the selected project nor its run. It mounts no queue,
 * execution, retry, release, ordering or mutation control. Every host value, the project name above all, is rendered as literal text.
 */
export function LocalExecutionQueueInspectionRegion() {
  const { open, pending, overview, failure, selection, show, close, reload, select, clearSelection } = useLocalExecutionQueueInspection()

  return (
    <section className="dc-queue-overview" aria-label={QUEUE_OVERVIEW_REGION_LABEL}>
      <button type="button" aria-expanded={open} onClick={open ? close : show}>
        {open ? QUEUE_OVERVIEW_HIDE_LABEL : QUEUE_OVERVIEW_REGION_LABEL}
      </button>
      {open ? (
        <div className="dc-local-inspection-body">
          <p className="dc-local-commit-note">{QUEUE_OVERVIEW_NOTE}</p>
          <p>
            <button type="button" onClick={reload}>
              {QUEUE_OVERVIEW_RELOAD_LABEL}
            </button>
          </p>
          {pending ? <p className="dc-empty-state">{QUEUE_OVERVIEW_READING}</p> : null}
          {failure !== null ? (
            <p className="dc-card-evidence-error" role="alert">
              {QUEUE_OVERVIEW_FAILURES[failure]}
            </p>
          ) : null}
          {overview?.status === 'Empty' ? (
            <p className="dc-empty-state" role="status">
              {QUEUE_OVERVIEW_EMPTY}
            </p>
          ) : null}
          {overview?.status === 'Unavailable' ? (
            <p className="dc-empty-state" role="status">
              {QUEUE_OVERVIEW_UNAVAILABLE}
            </p>
          ) : null}
          {overview?.status === 'Recorded' ? (
            <div>
              <ol className="dc-local-inspection-steps">
                {overview.entries.map((entry) => {
                  const { admission, sequence } = entry
                  return (
                    <li key={admission.admissionId}>
                      <span>
                        Project {entry.projectName} · run {entry.runId} · recorded order {admission.admissionNumber} ·{' '}
                        {ADMISSION_STATE_LABEL[admission.recordedState]}
                      </span>
                      <p>
                        Admission {admission.ordinal + 1} · {LOCAL_EXECUTION_STEP_KIND_LABEL[admission.kind]} · pass {admission.passNumber}
                        {admission.recipeOrdinal === null ? '' : ` · recipe #${admission.recipeOrdinal + 1} ${admission.recipeName ?? ''}`}
                        {admission.reasonCode === null ? '' : ` · ${LOCAL_EXECUTION_REASON_PHRASE[admission.reasonCode]}`}
                      </p>
                      <p>
                        Recorded sequence state: {LOCAL_EXECUTION_STATE_LABEL[sequence.recordedState]}
                        {sequence.reasonCode === null ? '' : ` · ${LOCAL_EXECUTION_REASON_PHRASE[sequence.reasonCode]}`} · revision{' '}
                        {sequence.sequenceRevision}
                      </p>
                      <p>
                        Last recorded admission check: {LOCAL_EXECUTION_WAITING_REASON_LABEL[admission.waitingReason]} (recorded{' '}
                        {admission.waitingReasonRecordedAtUtc})
                      </p>
                      <p>
                        Enqueued {admission.enqueuedAtUtc} · updated {admission.updatedAtUtc} · revision {admission.revision}
                      </p>
                      <p>
                        Armed {sequence.armedAtUtc} · recorded advancement deadline {sequence.advancementDeadlineUtc} · sequence updated{' '}
                        {sequence.updatedAtUtc} · ended {sequence.endedAtUtc ?? 'not ended'}
                      </p>
                      <p>
                        <button
                          type="button"
                          aria-label={`${QUEUE_OVERVIEW_SELECT_LABEL}: project ${entry.projectName}, recorded order ${admission.admissionNumber}`}
                          aria-pressed={selection?.entry.admission.admissionId === admission.admissionId}
                          onClick={() => select(entry)}
                        >
                          {QUEUE_OVERVIEW_SELECT_LABEL}
                        </button>
                      </p>
                    </li>
                  )
                })}
              </ol>
              <p className="dc-local-commit-note">{LOCAL_EXECUTION_QUEUE_RECORDED_NOTE}</p>
            </div>
          ) : null}
          {selection !== null ? (
            <div className="dc-queue-overview-selection">
              <p>
                Selected recorded cycle: project {selection.entry.projectName} · run {selection.entry.runId} · recorded order{' '}
                {selection.entry.admission.admissionNumber}
              </p>
              <p className="dc-local-commit-note">{QUEUE_OVERVIEW_SELECTION_NOTE}</p>
              <p>
                <button type="button" onClick={clearSelection}>
                  {QUEUE_OVERVIEW_CLEAR_LABEL}
                </button>
              </p>
              <LocalExecutionInspectionPanel key={selection.key} runId={selection.entry.runId} />
            </div>
          ) : null}
        </div>
      ) : null}
    </section>
  )
}
