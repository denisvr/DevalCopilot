import { usePlanningSequenceInspection } from '../hooks/usePlanningSequenceInspection'
import { PLANNING_SEQUENCE_PROVIDER_LABEL, PLANNING_SEQUENCE_ROLE_LABEL, PLANNING_SEQUENCE_STATE_LABEL, PLANNING_SEQUENCE_STEP_STATE_LABEL } from '../planningSequenceCopy'
import {
  PLANNING_INSPECTION_FAILURES,
  PLANNING_INSPECTION_HIDE_LABEL,
  PLANNING_INSPECTION_NOTE,
  PLANNING_INSPECTION_NOT_RECORDED,
  PLANNING_INSPECTION_READING,
  PLANNING_INSPECTION_REGION_LABEL,
  PLANNING_INSPECTION_RELOAD_LABEL,
  PLANNING_INSPECTION_UNAVAILABLE,
  PLANNING_NOT_RECORDED,
  PLANNING_PINNED_NOTE,
  PLANNING_RECORDED_REASON_PHRASE,
} from '../planningSequenceInspectionCopy'
import { PlanningSequenceInspectionStepDetail } from './PlanningSequenceInspectionStepDetail'

/**
 * The collapsed, explicit **Inspect recorded planning** region of one run, for the current run and a selected historical run alike
 * (ADR-0068). Opening or reloading reads the recorded index; choosing one step reads only that step's attempt association; every Agent
 * evidence read and the input Proposal are separate explicit actions. It never pages through unrelated history to find an identifier,
 * never changes the current run selection, and mounts no arming, stop, implementation, recovery, approval or publication control: it
 * neither enables nor disables anything and does not replace the live planning status. Every host value is rendered as literal text.
 */
export function PlanningSequenceInspectionPanel({ runId }: { runId: string | null }) {
  const inspection = usePlanningSequenceInspection(runId)
  const { open, pending, index, failure, show, close, reload, selectedStep, selectStep, stepKey } = inspection
  const header = index?.status === 'Recorded' ? index.sequence : null

  return (
    <section className="dc-planning-inspection" aria-label={PLANNING_INSPECTION_REGION_LABEL}>
      <button type="button" aria-expanded={open} onClick={open ? close : show} disabled={runId === null}>
        {open ? PLANNING_INSPECTION_HIDE_LABEL : PLANNING_INSPECTION_REGION_LABEL}
      </button>
      {open && runId !== null ? (
        <div className="dc-planning-inspection-body">
          <p className="dc-local-commit-note">{PLANNING_INSPECTION_NOTE}</p>
          <p>
            <button type="button" onClick={reload}>
              {PLANNING_INSPECTION_RELOAD_LABEL}
            </button>
          </p>
          {pending ? <p className="dc-empty-state">{PLANNING_INSPECTION_READING}</p> : null}
          {failure !== null ? (
            <p className="dc-card-evidence-error" role="alert">
              {PLANNING_INSPECTION_FAILURES[failure]}
            </p>
          ) : null}
          {index?.status === 'NotRecorded' ? <p className="dc-empty-state">{PLANNING_INSPECTION_NOT_RECORDED}</p> : null}
          {index?.status === 'Unavailable' ? (
            <p className="dc-empty-state" role="status">
              {PLANNING_INSPECTION_UNAVAILABLE}
            </p>
          ) : null}
          {index?.status === 'Recorded' && header !== null ? (
            <div>
              <dl>
                <div>
                  <dt>Sequence</dt>
                  <dd>{header.sequenceId}</dd>
                </div>
                <div>
                  <dt>Recorded state</dt>
                  <dd>
                    {PLANNING_SEQUENCE_STATE_LABEL[header.recordedState]}
                    {header.reasonCode === null ? '' : ` · ${PLANNING_RECORDED_REASON_PHRASE[header.reasonCode]}`} · revision {header.revision}
                  </dd>
                </div>
                <div>
                  <dt>Pinned source</dt>
                  <dd>
                    workspace {header.workspaceId} · checkpoint {header.checkpointId} · {header.fingerprintSha256}. {PLANNING_PINNED_NOTE}
                  </dd>
                </div>
                <div>
                  <dt>Armed · updated (UTC)</dt>
                  <dd>
                    {header.armedAtUtc} · {header.updatedAtUtc}
                  </dd>
                </div>
                <div>
                  <dt>Stop requested · ended (UTC)</dt>
                  <dd>
                    {header.stopRequestedAtUtc ?? PLANNING_NOT_RECORDED} · {header.endedAtUtc ?? 'not ended'}
                  </dd>
                </div>
              </dl>
              {index.steps.length === 0 ? (
                <p className="dc-empty-state">No step has been reserved.</p>
              ) : (
                <ol className="dc-planning-inspection-steps">
                  {index.steps.map((step) => (
                    <li key={step.stepId}>
                      <span>
                        Step {step.ordinal + 1} · {PLANNING_SEQUENCE_ROLE_LABEL[step.role]} ({PLANNING_SEQUENCE_PROVIDER_LABEL[step.provider]}) ·{' '}
                        {PLANNING_SEQUENCE_STEP_STATE_LABEL[step.recordedState]}
                        {step.reasonCode === null ? '' : ` · ${PLANNING_RECORDED_REASON_PHRASE[step.reasonCode]}`}
                      </span>{' '}
                      <button
                        type="button"
                        aria-label={`Inspect planning step ${step.ordinal + 1}`}
                        aria-pressed={selectedStep?.stepId === step.stepId}
                        onClick={() => selectStep(step.stepId)}
                      >
                        Inspect
                      </button>
                    </li>
                  ))}
                </ol>
              )}
              {selectedStep !== null && stepKey !== null ? (
                <PlanningSequenceInspectionStepDetail key={stepKey} inspection={inspection} runId={runId} step={selectedStep} />
              ) : null}
            </div>
          ) : null}
        </div>
      ) : null}
    </section>
  )
}
