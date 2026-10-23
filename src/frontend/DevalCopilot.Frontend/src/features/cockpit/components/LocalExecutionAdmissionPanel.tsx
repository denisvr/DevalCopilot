import { useLocalExecutionAdmissionHistory } from '../hooks/useLocalExecutionAdmissionHistory'
import type { UseLocalExecutionInspectionResult } from '../hooks/useLocalExecutionInspection'
import type { LocalExecutionAdmissionRecord } from '../localExecutionAdmissionHistory'
import {
  ADMISSIONS_FAILURES,
  ADMISSIONS_HIDE_LABEL,
  ADMISSIONS_NOTE,
  ADMISSIONS_NOT_QUEUED,
  ADMISSIONS_NOT_RECORDED,
  ADMISSIONS_READING,
  ADMISSIONS_REGION_LABEL,
  ADMISSIONS_RELOAD_LABEL,
  ADMISSIONS_UNAVAILABLE,
  ADMISSION_NO_STEP_RETIRED,
  ADMISSION_NO_STEP_WAITING,
  ADMISSION_STATE_LABEL,
  ADMISSION_STEP_NOT_INDEXED,
  ADMISSION_STEP_OTHER_SEQUENCE,
  ADMISSION_STEP_RETIRED_NOTE,
  ADMISSION_STEP_UNRESOLVED_NOTE,
} from '../localExecutionAdmissionHistoryCopy'
import {
  LOCAL_EXECUTION_QUEUE_RECORDED_NOTE,
  LOCAL_EXECUTION_REASON_PHRASE,
  LOCAL_EXECUTION_STATE_LABEL,
  LOCAL_EXECUTION_STEP_KIND_LABEL,
  LOCAL_EXECUTION_WAITING_REASON_LABEL,
} from '../localExecutionSequenceCopy'

/**
 * The collapsed, explicit **Inspect queue admissions** region of one recorded queued local execution cycle, shown inside the local
 * execution inspector of the current run and of a selected historical run alike (ADR-0063). Opening or reloading reads only the recorded
 * admission ledger: every admission the host recorded, including a wait that closed without a step. A row that links a step offers one
 * more explicit action that reuses the inspector's own selected-step evidence (ADR-0061) when, and only when, that step is in the
 * inspector's currently accepted index; otherwise it says the recorded sequence must be reloaded and selects, searches and substitutes
 * nothing. It mounts no queue, execution, retry, release or mutation control and never changes the run selection. Every host value is
 * rendered as literal text.
 */
export function LocalExecutionAdmissionPanel({ runId, inspection }: { runId: string; inspection: UseLocalExecutionInspectionResult }) {
  const { selectedStep, selectStep, index } = inspection
  const { open, pending, history, failure, show, close, reload, inspectLinkedStep } = useLocalExecutionAdmissionHistory(runId, selectStep)
  const loaded = index?.status === 'Recorded' ? index : null

  const linkOf = (admission: LocalExecutionAdmissionRecord, sequenceId: string) => {
    if (admission.stepId === null) {
      return <p>{admission.recordedState === 'Waiting' ? ADMISSION_NO_STEP_WAITING : ADMISSION_NO_STEP_RETIRED}</p>
    }

    const stepId = admission.stepId
    if (loaded !== null && loaded.sequence.sequenceId !== sequenceId) {
      return <p>{ADMISSION_STEP_OTHER_SEQUENCE}</p>
    }

    if (loaded === null || !loaded.steps.some((step) => step.stepId === stepId)) {
      return <p>{ADMISSION_STEP_NOT_INDEXED}</p>
    }

    return (
      <p>
        <button
          type="button"
          aria-label={`Open linked step ${admission.ordinal + 1} evidence from admission ${admission.ordinal + 1}`}
          aria-pressed={selectedStep?.stepId === stepId}
          onClick={() => inspectLinkedStep(stepId)}
        >
          Open step evidence
        </button>
        {admission.recordedState === 'Retired' ? ` ${ADMISSION_STEP_RETIRED_NOTE}` : ''}
        {admission.recordedState === 'Unresolved' ? ` ${ADMISSION_STEP_UNRESOLVED_NOTE}` : ''}
      </p>
    )
  }

  return (
    <section className="dc-local-inspection-admissions" aria-label={ADMISSIONS_REGION_LABEL}>
      <button type="button" aria-expanded={open} onClick={open ? close : show}>
        {open ? ADMISSIONS_HIDE_LABEL : ADMISSIONS_REGION_LABEL}
      </button>
      {open ? (
        <div className="dc-local-inspection-body">
          <p className="dc-local-commit-note">{ADMISSIONS_NOTE}</p>
          <p>
            <button type="button" onClick={reload}>
              {ADMISSIONS_RELOAD_LABEL}
            </button>
          </p>
          {pending ? <p className="dc-empty-state">{ADMISSIONS_READING}</p> : null}
          {failure !== null ? (
            <p className="dc-card-evidence-error" role="alert">
              {ADMISSIONS_FAILURES[failure]}
            </p>
          ) : null}
          {history?.status === 'NotRecorded' ? <p className="dc-empty-state">{ADMISSIONS_NOT_RECORDED}</p> : null}
          {history?.status === 'NotQueued' ? <p className="dc-empty-state">{ADMISSIONS_NOT_QUEUED}</p> : null}
          {history?.status === 'Unavailable' ? (
            <p className="dc-empty-state" role="status">
              {ADMISSIONS_UNAVAILABLE}
            </p>
          ) : null}
          {history?.status === 'Recorded' ? (
            <div>
              <dl>
                <div>
                  <dt>Queued sequence</dt>
                  <dd>{history.sequence.sequenceId}</dd>
                </div>
                <div>
                  <dt>Recorded state</dt>
                  <dd>
                    {LOCAL_EXECUTION_STATE_LABEL[history.sequence.recordedState]}
                    {history.sequence.reasonCode === null ? '' : ` · ${LOCAL_EXECUTION_REASON_PHRASE[history.sequence.reasonCode]}`} · revision{' '}
                    {history.sequence.sequenceRevision}
                  </dd>
                </div>
                <div>
                  <dt>Armed · advancement deadline · updated · ended (UTC)</dt>
                  <dd>
                    {history.sequence.armedAtUtc} · {history.sequence.advancementDeadlineUtc} · {history.sequence.updatedAtUtc} ·{' '}
                    {history.sequence.endedAtUtc ?? 'not ended'}
                  </dd>
                </div>
              </dl>
              <ol className="dc-local-inspection-steps">
                {history.admissions.map((admission) => (
                  <li key={admission.admissionId}>
                    <span>
                      Admission {admission.ordinal + 1} · recorded order {admission.admissionNumber} ·{' '}
                      {LOCAL_EXECUTION_STEP_KIND_LABEL[admission.kind]} · pass {admission.passNumber}
                      {admission.recipeOrdinal === null ? '' : ` · recipe #${admission.recipeOrdinal + 1} ${admission.recipeName ?? ''}`} ·{' '}
                      {ADMISSION_STATE_LABEL[admission.recordedState]}
                      {admission.reasonCode === null ? '' : ` · ${LOCAL_EXECUTION_REASON_PHRASE[admission.reasonCode]}`}
                    </span>
                    <p>
                      Last recorded admission check: {LOCAL_EXECUTION_WAITING_REASON_LABEL[admission.waitingReason]} (recorded{' '}
                      {admission.waitingReasonRecordedAtUtc})
                    </p>
                    <p>
                      Enqueued {admission.enqueuedAtUtc} · updated {admission.updatedAtUtc} · revision {admission.revision}
                    </p>
                    {linkOf(admission, history.sequence.sequenceId)}
                  </li>
                ))}
              </ol>
              <p className="dc-local-commit-note">{LOCAL_EXECUTION_QUEUE_RECORDED_NOTE}</p>
            </div>
          ) : null}
        </div>
      ) : null}
    </section>
  )
}
