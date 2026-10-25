import { useLocalExecutionReleaseInspection } from '../hooks/useLocalExecutionReleaseInspection'
import type { LocalExecutionReleaseInspectionReceipt, LocalExecutionReleaseInspectionTarget } from '../localExecutionReleaseInspection'
import {
  RELEASE_INSPECTION_FAILURES,
  RELEASE_INSPECTION_HIDE_LABEL,
  RELEASE_INSPECTION_NOTE,
  RELEASE_INSPECTION_NOT_RECORDED,
  RELEASE_INSPECTION_OUTCOME_LABEL,
  RELEASE_INSPECTION_PHYSICAL_NOTE,
  RELEASE_INSPECTION_READING,
  RELEASE_INSPECTION_RECORDED_LEAD,
  RELEASE_INSPECTION_REGION_LABEL,
  RELEASE_INSPECTION_RELOAD_LABEL,
  RELEASE_INSPECTION_RESULT_NOTE,
  RELEASE_INSPECTION_UNAVAILABLE,
} from '../localExecutionReleaseInspectionCopy'
import { LOCAL_EXECUTION_STATE_LABEL, LOCAL_EXECUTION_STEP_KIND_LABEL } from '../localExecutionSequenceCopy'

function ReceiptView({ receipt }: { receipt: LocalExecutionReleaseInspectionReceipt }) {
  return (
    <div className="dc-local-inspection-release-receipt">
      <p>{RELEASE_INSPECTION_RECORDED_LEAD}</p>
      <dl>
        <div>
          <dt>Human release</dt>
          <dd>
            Released {receipt.releasedAtUtc} · recorded as Human run event #{receipt.eventSequence}
          </dd>
        </div>
        <div>
          <dt>Recorded result of the operation (unchanged)</dt>
          <dd>
            {receipt.operationStatus} · process outcome {RELEASE_INSPECTION_OUTCOME_LABEL[receipt.processOutcome]} · semantic exit code not
            recorded · process duration {receipt.processDurationTicks} ticks
          </dd>
        </div>
        <div>
          <dt>Physical termination observed by the original host</dt>
          <dd>
            Physical exit code {receipt.physicalExitCode} · observed elapsed {receipt.observedElapsedTicks} ticks · dispatched{' '}
            {receipt.dispatchedAtUtc} · observation recorded {receipt.recordedAtUtc}
          </dd>
        </div>
        <div>
          <dt>Operation</dt>
          <dd>
            {receipt.operationFamily === 'Verification' ? 'Verification run' : 'Agent attempt'} · step {receipt.ordinal + 1} ·{' '}
            {LOCAL_EXECUTION_STEP_KIND_LABEL[receipt.stepKind]} · pass {receipt.passNumber}
          </dd>
        </div>
        <div>
          <dt>Cycle at release</dt>
          <dd>
            {LOCAL_EXECUTION_STATE_LABEL[receipt.sequenceState]} · {receipt.admissionMode === 'WaitForAdmission' ? 'Queued (waits for admission)' : 'Immediate'}
          </dd>
        </div>
      </dl>
      <p className="dc-local-commit-note">{RELEASE_INSPECTION_RESULT_NOTE}</p>
      <p className="dc-local-commit-note">{RELEASE_INSPECTION_PHYSICAL_NOTE}</p>
      <details>
        <summary>Recorded identities</summary>
        <dl>
          <div>
            <dt>Release</dt>
            <dd>{receipt.releaseId}</dd>
          </div>
          <div>
            <dt>Operation</dt>
            <dd>{receipt.operationId}</dd>
          </div>
          <div>
            <dt>Cycle</dt>
            <dd>{receipt.sequenceId}</dd>
          </div>
          <div>
            <dt>Dispatch witness</dt>
            <dd>{receipt.witnessId}</dd>
          </div>
          <div>
            <dt>Termination observation</dt>
            <dd>{receipt.observationId}</dd>
          </div>
          <div>
            <dt>Pinned source</dt>
            <dd>
              checkpoint {receipt.sourceCheckpointId} · {receipt.sourceFingerprintSha256}
            </dd>
          </div>
        </dl>
      </details>
    </div>
  )
}

interface LocalExecutionReleaseInspectionRegionProps {
  /** The lifetime of the surrounding selected step visit: a region of another step visit never shares state with this one. */
  ownerKey: string
  target: LocalExecutionReleaseInspectionTarget | null
}

/**
 * The collapsed, explicit **Inspect recorded quarantine release** region of one proved linked step, inside the selected-step detail of
 * the current run and of a selected historical run alike (ADR-0067). Opening or reloading reads the recorded receipt once; selecting the
 * step or opening another inspector reads nothing, and nothing polls, retries or refreshes it. It explains a past Human decision, the
 * unchanged failed result and the invoked-process-only physical evidence, and promises neither a held nor a released owner for a
 * negative answer. It mounts no release control, candidate query or confirmation handler. Every host value is rendered as literal text.
 */
export function LocalExecutionReleaseInspectionRegion({ ownerKey, target }: LocalExecutionReleaseInspectionRegionProps) {
  const { open, pending, answer, failure, show, close, reload } = useLocalExecutionReleaseInspection(ownerKey, target)

  return (
    <section className="dc-local-inspection-release" aria-label={RELEASE_INSPECTION_REGION_LABEL}>
      <button type="button" aria-expanded={open} onClick={open ? close : show} disabled={target === null}>
        {open ? RELEASE_INSPECTION_HIDE_LABEL : RELEASE_INSPECTION_REGION_LABEL}
      </button>
      {open ? (
        <div className="dc-local-inspection-body">
          {RELEASE_INSPECTION_NOTE.map((line) => (
            <p key={line} className="dc-local-commit-note">
              {line}
            </p>
          ))}
          <p>
            <button type="button" onClick={reload}>
              {RELEASE_INSPECTION_RELOAD_LABEL}
            </button>
          </p>
          {pending ? <p className="dc-empty-state">{RELEASE_INSPECTION_READING}</p> : null}
          {failure !== null ? (
            <p className="dc-card-evidence-error" role="alert">
              {RELEASE_INSPECTION_FAILURES[failure]}
            </p>
          ) : null}
          {answer?.status === 'NotRecorded' ? (
            <p className="dc-empty-state" role="status">
              {RELEASE_INSPECTION_NOT_RECORDED}
            </p>
          ) : null}
          {answer?.status === 'Unavailable' ? (
            <p className="dc-empty-state" role="status">
              {RELEASE_INSPECTION_UNAVAILABLE}
            </p>
          ) : null}
          {answer?.status === 'Recorded' ? <ReceiptView receipt={answer.receipt} /> : null}
        </div>
      ) : null}
    </section>
  )
}
