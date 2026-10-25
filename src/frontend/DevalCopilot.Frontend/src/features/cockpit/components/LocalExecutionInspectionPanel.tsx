import { useLocalExecutionInspection } from '../hooks/useLocalExecutionInspection'
import {
  INSPECTION_FAILURES,
  INSPECTION_NOTE,
  INSPECTION_NOT_RECORDED,
  INSPECTION_READING,
  INSPECTION_REGION_LABEL,
  INSPECTION_UNAVAILABLE,
} from '../localExecutionInspectionCopy'
import {
  LOCAL_EXECUTION_PINNED_NOTE,
  LOCAL_EXECUTION_REASON_PHRASE,
  LOCAL_EXECUTION_STATE_LABEL,
  LOCAL_EXECUTION_STEP_KIND_LABEL,
  LOCAL_EXECUTION_STEP_STATE_LABEL,
} from '../localExecutionSequenceCopy'
import type { CollaborationTimelineCard } from '../types'
import { CollaborationEvidenceDrilldown } from './CollaborationEvidenceDrilldown'
import { LocalExecutionAdmissionPanel } from './LocalExecutionAdmissionPanel'
import { LocalExecutionInspectionStepDetail } from './LocalExecutionInspectionStepDetail'

const NO_CARDS: ReadonlyMap<string, CollaborationTimelineCard> = new Map()

/**
 * The collapsed, explicit **Inspect local execution** region of one run, for the current run and a selected historical run alike
 * (ADR-0061). Opening or reloading reads the recorded index; choosing one step reads only that step's operation; a stream window and
 * every Agent evidence read are separate explicit actions. It never pages through unrelated history to find an identifier, never changes
 * the current run selection, and mounts no command, eligibility, live-control or mutation control: it neither enables, disables nor
 * dispatches anything and does not replace the live status. Every host value is rendered as literal text.
 */
export function LocalExecutionInspectionPanel({ runId }: { runId: string | null }) {
  const inspection = useLocalExecutionInspection(runId)
  const { open, pending, index, failure, show, close, reload, selectedStep, selectStep, stepKey } = inspection
  const header = index?.status === 'Recorded' ? index.sequence : null

  return (
    <section className="dc-local-inspection" aria-label={INSPECTION_REGION_LABEL}>
      <button type="button" aria-expanded={open} onClick={open ? close : show} disabled={runId === null}>
        {open ? 'Hide local execution inspection' : INSPECTION_REGION_LABEL}
      </button>
      {open && runId !== null ? (
        <div className="dc-local-inspection-body">
          <p className="dc-local-commit-note">{INSPECTION_NOTE}</p>
          <p>
            <button type="button" onClick={reload}>
              Reload recorded sequence
            </button>
          </p>
          {pending ? <p className="dc-empty-state">{INSPECTION_READING}</p> : null}
          {failure !== null ? (
            <p className="dc-card-evidence-error" role="alert">
              {INSPECTION_FAILURES[failure]}
            </p>
          ) : null}
          {index?.status === 'NotRecorded' ? <p className="dc-empty-state">{INSPECTION_NOT_RECORDED}</p> : null}
          {index?.status === 'Unavailable' ? (
            <p className="dc-empty-state" role="status">
              {INSPECTION_UNAVAILABLE}
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
                    {LOCAL_EXECUTION_STATE_LABEL[header.recordedState]}
                    {header.reasonCode === null ? '' : ` · ${LOCAL_EXECUTION_REASON_PHRASE[header.reasonCode]}`} · revision {header.revision}
                  </dd>
                </div>
                <div>
                  <dt>Admission mode</dt>
                  <dd>
                    {header.admissionMode === 'WaitForAdmission' ? 'Queued (waits for admission)' : 'Immediate'}
                    {header.recordedState === 'WaitingForAdmission'
                      ? ' · recorded as waiting: bookkeeping, not current eligibility, and no pending step is an operation'
                      : ''}
                  </dd>
                </div>
                <div>
                  <dt>Plan authority</dt>
                  <dd>{header.planAuthority}</dd>
                </div>
                <div>
                  <dt>Pinned source</dt>
                  <dd>
                    checkpoint {header.checkpointId} · {header.fingerprintSha256}. {LOCAL_EXECUTION_PINNED_NOTE}
                  </dd>
                </div>
                <div>
                  <dt>Recipes · corrections allowed</dt>
                  <dd>
                    {header.recipeCount} · {header.correctionAllowance}
                  </dd>
                </div>
                <div>
                  <dt>Armed · updated · ended (UTC)</dt>
                  <dd>
                    {header.armedAtUtc} · {header.updatedAtUtc} · {header.endedAtUtc ?? 'not ended'}
                  </dd>
                </div>
              </dl>
              <div className="dc-local-inspection-plan">
                <p>Recorded plan: {header.planProposalMessageId}</p>
                <CollaborationEvidenceDrilldown
                  key={`${inspection.visitKey}:plan`}
                  runId={runId}
                  messageId={header.planProposalMessageId}
                  cardsById={NO_CARDS}
                />
              </div>
              {header.admissionMode === 'WaitForAdmission' ? (
                <LocalExecutionAdmissionPanel key={runId} runId={runId} inspection={inspection} />
              ) : null}
              {index.steps.length === 0 ? (
                <p className="dc-empty-state">No operation has been reserved.</p>
              ) : (
                <ol className="dc-local-inspection-steps">
                  {index.steps.map((step) => (
                    <li key={step.stepId}>
                      <span>
                        Step {step.ordinal + 1} · {LOCAL_EXECUTION_STEP_KIND_LABEL[step.kind]} · pass {step.passNumber}
                        {step.recipeOrdinal === null ? '' : ` · recipe #${step.recipeOrdinal + 1} ${step.recipeName ?? ''}`} ·{' '}
                        {LOCAL_EXECUTION_STEP_STATE_LABEL[step.recordedState]}
                        {step.reasonCode === null ? '' : ` · ${LOCAL_EXECUTION_REASON_PHRASE[step.reasonCode]}`}
                      </span>{' '}
                      <button
                        type="button"
                        aria-label={`Inspect step ${step.ordinal + 1}`}
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
                <LocalExecutionInspectionStepDetail
                  key={stepKey}
                  inspection={inspection}
                  runId={runId}
                  sequenceId={header.sequenceId}
                  admissionMode={header.admissionMode}
                  step={selectedStep}
                />
              ) : null}
            </div>
          ) : null}
        </div>
      ) : null}
    </section>
  )
}
