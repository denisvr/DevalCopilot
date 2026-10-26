import type { UsePlanningSequenceInspectionResult } from '../hooks/usePlanningSequenceInspection'
import type { PlanningInspectionStep } from '../planningSequenceInspection'
import {
  PLANNING_NOT_RECORDED,
  PLANNING_RECORDED_REASON_PHRASE,
  PLANNING_STEP_AGENT_NOTE,
  PLANNING_STEP_FAILURES,
  PLANNING_STEP_INPUT_NOTE,
  PLANNING_STEP_NO_LINKED_ATTEMPT,
  PLANNING_STEP_UNAVAILABLE,
} from '../planningSequenceInspectionCopy'
import { PLANNING_SEQUENCE_PROVIDER_LABEL, PLANNING_SEQUENCE_ROLE_LABEL, PLANNING_SEQUENCE_STEP_STATE_LABEL } from '../planningSequenceCopy'
import type { CollaborationTimelineCard } from '../types'
import { AgentAttemptDetail } from './AgentAttemptDetail'
import { CollaborationEvidenceDrilldown } from './CollaborationEvidenceDrilldown'

const NO_CARDS: ReadonlyMap<string, CollaborationTimelineCard> = new Map()

function StepFacts({ step }: { step: PlanningInspectionStep }) {
  return (
    <dl>
      <div>
        <dt>Step</dt>
        <dd>
          {step.ordinal + 1} · {PLANNING_SEQUENCE_ROLE_LABEL[step.role]} · {PLANNING_SEQUENCE_PROVIDER_LABEL[step.provider]} ·{' '}
          {step.responseContract}
        </dd>
      </div>
      <div>
        <dt>Recorded state</dt>
        <dd>
          {PLANNING_SEQUENCE_STEP_STATE_LABEL[step.recordedState]}
          {step.reasonCode === null ? '' : ` · ${PLANNING_RECORDED_REASON_PHRASE[step.reasonCode]}`} · revision {step.revision}
        </dd>
      </div>
      <div>
        <dt>Preparation reserved · deadline · updated (UTC)</dt>
        <dd>
          {step.preparationReservedAtUtc} · {step.preparationDeadlineUtc} · {step.updatedAtUtc}
        </dd>
      </div>
      <div>
        <dt>Predecessor attempt</dt>
        <dd>{step.predecessorAttemptId ?? PLANNING_NOT_RECORDED}</dd>
      </div>
      {step.attemptId === null ? null : (
        <div>
          <dt>Recorded attempt link</dt>
          <dd>{step.attemptId}</dd>
        </div>
      )}
    </dl>
  )
}

interface PlanningSequenceInspectionStepDetailProps {
  inspection: UsePlanningSequenceInspectionResult
  runId: string
  step: PlanningInspectionStep
}

/**
 * The selected step's recorded association (ADR-0068). The step facts come from the accepted index; the association was read once for
 * this step visit. A linked step shows its immutable attempt header and mounts the existing selected-attempt detail (its evidence,
 * recorded inputs, outputs and verification, each an explicit read of its own), so a Running, failed, refused, interrupted or no-output
 * attempt stays inspectable. The step's input Proposal is opened only on request through the existing guarded message inspector. A step
 * with no recorded link says only that. Every value is literal text and nothing here sends a command.
 */
export function PlanningSequenceInspectionStepDetail({ inspection, runId, step }: PlanningSequenceInspectionStepDetailProps) {
  const { stepKey, stepPending, stepAnswer, stepFailure, retryStep, clearStep } = inspection
  return (
    <section className="dc-planning-inspection-step" aria-label="Selected planning step">
      <p>
        <button type="button" onClick={clearStep}>
          Close step
        </button>
      </p>
      <StepFacts step={step} />
      {step.inputProposalMessageId !== null && stepKey !== null && (
        <div className="dc-planning-inspection-input">
          <p>Recorded input Proposal this step names: {step.inputProposalMessageId}</p>
          <p className="dc-card-evidence-caveat">{PLANNING_STEP_INPUT_NOTE}</p>
          <CollaborationEvidenceDrilldown key={`${stepKey}:input`} runId={runId} messageId={step.inputProposalMessageId} cardsById={NO_CARDS} />
        </div>
      )}
      {stepPending && <p className="dc-empty-state">Loading this step&apos;s recorded attempt association…</p>}
      {stepFailure !== null && (
        <p className="dc-card-evidence-error" role="status">
          {PLANNING_STEP_FAILURES[stepFailure]}{' '}
          <button type="button" onClick={retryStep}>
            Try this step again
          </button>
        </p>
      )}
      {stepAnswer?.kind === 'no-linked-attempt' && (
        <p className="dc-empty-state" role="status">
          {PLANNING_STEP_NO_LINKED_ATTEMPT}
        </p>
      )}
      {stepAnswer?.kind === 'unavailable' && (
        <p className="dc-empty-state" role="status">
          {PLANNING_STEP_UNAVAILABLE}
        </p>
      )}
      {stepAnswer?.kind === 'agent' && stepKey !== null && (
        <div>
          <p className="dc-card-evidence-caveat">{PLANNING_STEP_AGENT_NOTE}</p>
          <AgentAttemptDetail selectionKey={stepKey} runId={runId} expected={stepAnswer.agent} onClose={clearStep} />
        </div>
      )}
    </section>
  )
}
