import type { VerificationStream, VerificationStreamDescriptor } from '../agentAttemptVerification'
import type { UseProjectVerificationInspectionResult } from '../hooks/useProjectVerificationInspection'
import type { ProjectVerificationExecution, ProjectVerificationHistoryItem } from '../projectVerificationInspection'
import {
  PROJECT_VERIFICATION_EXECUTION_FAILURES,
  PROJECT_VERIFICATION_EXECUTION_READING,
  PROJECT_VERIFICATION_EXECUTION_UNAVAILABLE,
  PROJECT_VERIFICATION_NOT_RECORDED,
  PROJECT_VERIFICATION_OUTCOME_LABEL,
  PROJECT_VERIFICATION_STATUS_LABEL,
  PROJECT_VERIFICATION_STATUS_NOTE,
} from '../projectVerificationInspectionCopy'
import { VerificationStreamWindowView } from './VerificationStreamWindowView'

const STREAM_LABELS: Record<VerificationStream, string> = { stdout: 'Standard output', stderr: 'Standard error' }

function describeCapture(descriptor: Extract<VerificationStreamDescriptor, { status: 'Recorded' }>): string {
  if (descriptor.truncated === null) {
    return 'truncation unknown (recovered after a host interruption)'
  }

  return descriptor.truncated ? 'truncated at capture' : 'not truncated at capture'
}

function describeStream(stream: VerificationStream, descriptor: VerificationStreamDescriptor): string {
  const name = STREAM_LABELS[stream]
  switch (descriptor.status) {
    case 'NotRecorded':
      return `${name}: not recorded.`
    case 'Unavailable':
      return `${name}: recorded metadata unavailable.`
    default:
      return `${name}: ${descriptor.byteLength} bytes · ${describeCapture(descriptor)}`
  }
}

function describeResult(execution: ProjectVerificationExecution): string {
  const outcome = execution.outcome === null ? 'no process outcome recorded' : PROJECT_VERIFICATION_OUTCOME_LABEL[execution.outcome]
  const exitCode = execution.exitCode === null ? 'no exit code recorded' : `exit code ${execution.exitCode}`
  return `${PROJECT_VERIFICATION_STATUS_LABEL[execution.status]} · ${outcome} · ${exitCode}`
}

interface ProjectVerificationInspectionDetailProps {
  inspection: UseProjectVerificationInspectionResult
  item: ProjectVerificationHistoryItem
}

/**
 * The explicitly selected recorded verification of the project (ADR-0069): its metadata as read now and its two sealed streams. The
 * metadata is one read of exactly this execution; each output window is a separate, explicit Load, and every host value is rendered as
 * literal text. Nothing here attributes the execution to a run, says the source is current, or controls an execution.
 */
export function ProjectVerificationInspectionDetail({ inspection: state, item }: ProjectVerificationInspectionDetailProps) {
  const { executionPending, inspection, executionFailure, retryExecution, selection, selectStream, clearStream, window } = state
  const execution = inspection?.status === 'Recorded' ? inspection.execution : null

  return (
    <div className="dc-project-verification-detail" aria-label={`Recorded verification #${item.executionNumber}`}>
      {executionPending ? <p className="dc-empty-state">{PROJECT_VERIFICATION_EXECUTION_READING}</p> : null}
      {executionFailure !== null ? (
        <p className="dc-card-evidence-error" role="alert">
          {PROJECT_VERIFICATION_EXECUTION_FAILURES[executionFailure]}{' '}
          <button type="button" onClick={retryExecution}>
            Try the recorded verification again
          </button>
        </p>
      ) : null}
      {inspection?.status === 'Unavailable' ? (
        <p className="dc-empty-state" role="status">
          {PROJECT_VERIFICATION_EXECUTION_UNAVAILABLE}
        </p>
      ) : null}
      {execution !== null ? (
        <div>
          <dl>
            <div>
              <dt>Verification</dt>
              <dd>
                #{execution.executionNumber} · {execution.commandName} (recorded snapshot of the command)
              </dd>
            </div>
            <div>
              <dt>Recorded result</dt>
              <dd>
                {describeResult(execution)}. {PROJECT_VERIFICATION_STATUS_NOTE}
              </dd>
            </div>
            <div>
              <dt>Workspace</dt>
              <dd>{execution.workspaceId}</dd>
            </div>
            <div>
              <dt>Checkpoint</dt>
              <dd>
                #{execution.checkpointNumber} ({execution.checkpointId})
              </dd>
            </div>
            <div>
              <dt>Claim fingerprint</dt>
              <dd>{execution.claimFingerprintSha256}</dd>
            </div>
            <div>
              <dt>Completion fingerprint</dt>
              <dd>{execution.completionFingerprintSha256 ?? PROJECT_VERIFICATION_NOT_RECORDED}</dd>
            </div>
            <div>
              <dt>Claimed (UTC)</dt>
              <dd>{execution.claimedAtUtc}</dd>
            </div>
            <div>
              <dt>Dispatched (UTC)</dt>
              <dd>{execution.dispatchedAtUtc ?? PROJECT_VERIFICATION_NOT_RECORDED}</dd>
            </div>
            <div>
              <dt>Completed (UTC)</dt>
              <dd>{execution.completedAtUtc ?? PROJECT_VERIFICATION_NOT_RECORDED}</dd>
            </div>
          </dl>
          <ul className="dc-project-verification-streams">
            {(['stdout', 'stderr'] as const).map((stream) => {
              const descriptor = stream === 'stdout' ? execution.standardOutput : execution.standardError
              const chosen = selection?.stream === stream
              const label = `${STREAM_LABELS[stream]} of verification #${execution.executionNumber}`
              return (
                <li key={stream}>
                  <span>{describeStream(stream, descriptor)}</span>{' '}
                  {descriptor.status === 'Recorded' && (
                    <button
                      type="button"
                      aria-pressed={chosen}
                      aria-label={`${chosen ? 'Hide' : 'View'} ${label.toLowerCase()}`}
                      onClick={chosen ? clearStream : () => selectStream(stream)}
                    >
                      {chosen ? 'Hide' : 'View'}
                    </button>
                  )}
                  {chosen && <VerificationStreamWindowView label={label} state={window.state} load={window.load} />}
                </li>
              )
            })}
          </ul>
        </div>
      ) : null}
    </div>
  )
}
