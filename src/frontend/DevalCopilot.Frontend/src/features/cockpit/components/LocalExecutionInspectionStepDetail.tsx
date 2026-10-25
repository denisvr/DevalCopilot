import { useMemo } from 'react'
import type { VerificationStream, VerificationStreamDescriptor } from '../agentAttemptVerification'
import type { UseLocalExecutionInspectionResult } from '../hooks/useLocalExecutionInspection'
import { useLocalExecutionInspectionWindow } from '../hooks/useLocalExecutionInspectionWindow'
import type { LocalExecutionInspectionStep, LocalExecutionInspectionVerification } from '../localExecutionInspection'
import {
  AGENT_STEP_NOTE,
  NOT_RECORDED,
  STEP_FAILURES,
  STEP_NO_OPERATION,
  STEP_UNAVAILABLE,
  STREAM_LABELS,
  VERIFICATION_STATUS_LABEL,
  VERIFICATION_STEP_NOTE,
} from '../localExecutionInspectionCopy'
import { releaseInspectionTargetOf } from '../localExecutionReleaseInspection'
import type { LocalExecutionAdmissionMode } from '../localExecutionSequence'
import { LOCAL_EXECUTION_REASON_PHRASE, LOCAL_EXECUTION_STEP_KIND_LABEL, LOCAL_EXECUTION_STEP_STATE_LABEL } from '../localExecutionSequenceCopy'
import type { CollaborationTimelineCard } from '../types'
import { AgentAttemptDetail } from './AgentAttemptDetail'
import { CollaborationEvidenceDrilldown } from './CollaborationEvidenceDrilldown'
import { LocalExecutionReleaseInspectionRegion } from './LocalExecutionReleaseInspectionRegion'
import { VerificationStreamWindowView } from './VerificationStreamWindowView'

const NO_CARDS: ReadonlyMap<string, CollaborationTimelineCard> = new Map()

function describeStream(stream: VerificationStream, descriptor: VerificationStreamDescriptor): string {
  const name = STREAM_LABELS[stream]
  switch (descriptor.status) {
    case 'NotRecorded':
      return `${name}: not recorded.`
    case 'Unavailable':
      return `${name}: recorded metadata unavailable.`
    default: {
      const capture =
        descriptor.truncated === null
          ? 'truncation unknown (recovered after a host interruption)'
          : descriptor.truncated
            ? 'truncated at capture'
            : 'not truncated at capture'
      return `${name}: ${descriptor.byteLength} bytes · ${capture}`
    }
  }
}

function StepFacts({ step }: { step: LocalExecutionInspectionStep }) {
  return (
    <dl>
      <div>
        <dt>Step</dt>
        <dd>
          {step.ordinal + 1} · {LOCAL_EXECUTION_STEP_KIND_LABEL[step.kind]}
          {step.kind === 'Verification' ? ` · recipe #${(step.recipeOrdinal ?? 0) + 1} ${step.recipeName ?? ''}` : ''}
        </dd>
      </div>
      <div>
        <dt>Pass</dt>
        <dd>{step.passNumber}</dd>
      </div>
      <div>
        <dt>Recorded state</dt>
        <dd>
          {LOCAL_EXECUTION_STEP_STATE_LABEL[step.recordedState]}
          {step.reasonCode === null ? '' : ` · ${LOCAL_EXECUTION_REASON_PHRASE[step.reasonCode]}`}
        </dd>
      </div>
      <div>
        <dt>Source checkpoint</dt>
        <dd>
          {step.sourceCheckpointId} · {step.sourceFingerprintSha256}
        </dd>
      </div>
      {step.operationId === null ? null : (
        <div>
          <dt>Recorded operation link</dt>
          <dd>{step.operationId}</dd>
        </div>
      )}
    </dl>
  )
}

interface VerificationDetailProps {
  inspection: UseLocalExecutionInspectionResult
  runId: string
  sequenceId: string
  step: LocalExecutionInspectionStep
  verification: LocalExecutionInspectionVerification
}

function VerificationDetail({ inspection, runId, sequenceId, step, verification }: VerificationDetailProps) {
  const { stream, selectStream, clearStream, stepKey } = inspection
  const window = useLocalExecutionInspectionWindow({
    stepKey,
    runId,
    sequenceId,
    stepId: step.stepId,
    executionId: verification.executionId,
    stream,
  })

  return (
    <div className="dc-local-inspection-verification">
      <p className="dc-card-evidence-caveat">{VERIFICATION_STEP_NOTE}</p>
      <dl>
        <div>
          <dt>Execution</dt>
          <dd>
            #{verification.executionNumber} · {verification.commandName} · {verification.executionId}
          </dd>
        </div>
        <div>
          <dt>Recorded status</dt>
          <dd>
            {VERIFICATION_STATUS_LABEL[verification.status]}
            {verification.outcome === null ? '' : ` · process outcome ${verification.outcome}`}
            {verification.exitCode === null ? '' : ` · exit code ${verification.exitCode}`}
          </dd>
        </div>
        <div>
          <dt>Claimed</dt>
          <dd>{verification.claimedAtUtc}</dd>
        </div>
        <div>
          <dt>Dispatched</dt>
          <dd>{verification.dispatchedAtUtc ?? NOT_RECORDED}</dd>
        </div>
        <div>
          <dt>Completed</dt>
          <dd>{verification.completedAtUtc ?? NOT_RECORDED}</dd>
        </div>
      </dl>
      <ul>
        {(['stdout', 'stderr'] as const).map((name) => {
          const descriptor = name === 'stdout' ? verification.standardOutput : verification.standardError
          const chosen = stream?.stream === name
          return (
            <li key={name}>
              <span>{describeStream(name, descriptor)}</span>{' '}
              {descriptor.status === 'Recorded' && (
                <button
                  type="button"
                  aria-pressed={chosen}
                  aria-label={`${chosen ? 'Hide' : 'View'} ${STREAM_LABELS[name].toLowerCase()} of this step`}
                  onClick={chosen ? clearStream : () => selectStream(name)}
                >
                  {chosen ? 'Hide' : 'View'}
                </button>
              )}
              {chosen && <VerificationStreamWindowView label={`${STREAM_LABELS[name]} of this step`} state={window.state} load={window.load} />}
            </li>
          )
        })}
      </ul>
    </div>
  )
}

interface LocalExecutionInspectionStepDetailProps {
  inspection: UseLocalExecutionInspectionResult
  runId: string
  sequenceId: string
  /** The recorded admission mode of the accepted index header, which a recorded release receipt must name. */
  admissionMode: LocalExecutionAdmissionMode
  step: LocalExecutionInspectionStep
}

/**
 * The selected step's recorded operation (ADR-0061). The step facts come from the accepted index; the operation was read once for this
 * step visit. An Agent step shows its immutable attempt header and mounts the existing selected-attempt detail (its evidence, recorded
 * inputs, outputs and verification, each an explicit read of its own) and the existing guarded message inspector for the report it
 * names; a verification step shows its proved facts and its two stream descriptors, with an explicit, bounded window per stream. A proved
 * linked operation also offers the collapsed, explicit recorded quarantine-release region (ADR-0067), owned by this step visit. Every
 * value is literal text and nothing here sends a command.
 */
export function LocalExecutionInspectionStepDetail({ inspection, runId, sequenceId, admissionMode, step }: LocalExecutionInspectionStepDetailProps) {
  const { stepKey, stepPending, stepAnswer, stepFailure, retryStep, clearStep } = inspection
  const releaseTarget = useMemo(
    () => releaseInspectionTargetOf(runId, { sequenceId, admissionMode }, step),
    [runId, sequenceId, admissionMode, step],
  )
  return (
    <section className="dc-local-inspection-step" aria-label="Selected local execution step">
      <p>
        <button type="button" onClick={clearStep}>
          Close step
        </button>
      </p>
      <StepFacts step={step} />
      {step.reportMessageId !== null && stepKey !== null && (
        <div className="dc-local-inspection-report">
          <p>Recorded report this step names: {step.reportMessageId}</p>
          <CollaborationEvidenceDrilldown key={`${stepKey}:report`} runId={runId} messageId={step.reportMessageId} cardsById={NO_CARDS} />
        </div>
      )}
      {stepPending && <p className="dc-empty-state">Loading this step&apos;s recorded operation…</p>}
      {stepFailure !== null && (
        <p className="dc-card-evidence-error" role="status">
          {STEP_FAILURES[stepFailure]}{' '}
          <button type="button" onClick={retryStep}>
            Try this step again
          </button>
        </p>
      )}
      {stepAnswer?.kind === 'no-operation' && (
        <p className="dc-empty-state" role="status">
          {STEP_NO_OPERATION}
        </p>
      )}
      {stepAnswer?.kind === 'unavailable' && (
        <p className="dc-empty-state" role="status">
          {STEP_UNAVAILABLE}
        </p>
      )}
      {stepAnswer?.kind === 'agent' && stepKey !== null && (
        <div>
          <p className="dc-card-evidence-caveat">{AGENT_STEP_NOTE}</p>
          <AgentAttemptDetail selectionKey={stepKey} runId={runId} expected={stepAnswer.agent} onClose={clearStep} />
        </div>
      )}
      {stepAnswer?.kind === 'verification' && (
        <VerificationDetail
          inspection={inspection}
          runId={runId}
          sequenceId={sequenceId}
          step={stepAnswer.step}
          verification={stepAnswer.verification}
        />
      )}
      {(stepAnswer?.kind === 'agent' || stepAnswer?.kind === 'verification') && stepKey !== null && (
        <LocalExecutionReleaseInspectionRegion key={`${stepKey}:release`} ownerKey={stepKey} target={releaseTarget} />
      )}
    </section>
  )
}
