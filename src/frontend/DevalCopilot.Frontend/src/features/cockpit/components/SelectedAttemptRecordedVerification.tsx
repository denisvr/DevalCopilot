import type { AgentAttemptIdentityExpectation } from '../agentAttemptHistory'
import { supportsVerificationEvidence } from '../agentAttemptVerification'
import type { VerificationMember, VerificationStream, VerificationStreamDescriptor } from '../agentAttemptVerification'
import { useAgentAttemptVerificationEvidence } from '../hooks/useAgentAttemptVerificationEvidence'
import type { AgentAttemptVerificationEvidenceFailure } from '../hooks/useAgentAttemptVerificationEvidence'
import { useAgentAttemptVerificationWindow } from '../hooks/useAgentAttemptVerificationWindow'
import { VerificationStreamWindowView } from './VerificationStreamWindowView'

interface SelectedAttemptRecordedVerificationProps {
  selectionKey: string
  runId: string
  expected: AgentAttemptIdentityExpectation
}

const FAILURES: Record<AgentAttemptVerificationEvidenceFailure, string> = {
  read: 'The recorded verification evidence could not be loaded.',
  'not-found': 'This attempt is no longer available.',
  invalid: 'The host answered with recorded verification evidence that is not coherent for this attempt, so none of it is shown.',
}

const RECORDED_VERIFICATION_CAVEAT =
  'These are the verification runs this attempt recorded as its evidence, shown as the records read now. They do not prove which bytes '
  + 'the Agent saw, that a diagnosis still matches the snapshot it was claimed against, or that any check is current or approved; a '
  + 'historical Passed is never a current approval.'

const STREAM_LABELS: Record<VerificationStream, string> = { stdout: 'Standard output', stderr: 'Standard error' }

function describeMember(member: VerificationMember): string {
  return `Check ${member.sequence + 1}: ${member.commandName} · ${member.status} (exit code ${member.exitCode})`
    + ` · execution #${member.executionNumber} · claimed ${member.claimedAtUtc}`
    + ` · dispatched ${member.dispatchedAtUtc} · completed ${member.completedAtUtc}`
}

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

/**
 * The collapsed "Recorded verification evidence" section of the selected attempt (ADR-0052), offered only for a CodeReviewer attempt of
 * the two supported contracts. The metadata is read only when it is opened and retried only on request, independent of the attempt's
 * evidence, collaboration inputs, Agent artifacts and any message it produced. Stream selection is explicit and offered only for a
 * Recorded descriptor; each output window is an explicit load. Everything is shown as plain text and links nothing.
 */
export function SelectedAttemptRecordedVerification({ selectionKey, runId, expected }: SelectedAttemptRecordedVerificationProps) {
  const supported = supportsVerificationEvidence(expected)
  const { open, openingKey, pending, evidence, failure, selection, show, close, retry, select, clearSelection } =
    useAgentAttemptVerificationEvidence({ selectionKey: supported ? selectionKey : null, runId, expected })
  const window = useAgentAttemptVerificationWindow({ openingKey, runId, attemptId: expected.attemptId, selection })

  if (!supported) {
    return null
  }

  return (
    <section className="dc-attempt-recorded-verification" aria-label="Recorded verification evidence">
      <button type="button" aria-expanded={open} onClick={open ? close : show}>
        Recorded verification evidence
      </button>
      {open ? (
        <div>
          <p className="dc-attempt-input-messages-caveat">{RECORDED_VERIFICATION_CAVEAT}</p>
          {pending ? <p className="dc-empty-state">Loading recorded verification evidence…</p> : null}
          {failure !== null ? (
            <p className="dc-card-evidence-error" role="status">
              {FAILURES[failure]}{' '}
              <button type="button" onClick={retry}>
                Try the recorded verification evidence again
              </button>
            </p>
          ) : null}
          {evidence?.status === 'Unavailable' ? (
            <p className="dc-empty-state" role="status">
              Recorded verification evidence unavailable: its membership could not be verified, or it is larger than this view inspects.
              This does not mean the attempt recorded no verification.
            </p>
          ) : null}
          {evidence?.status === 'Recorded' ? (
            <div>
              <p>
                {evidence.members.length === 1 ? '1 recorded verification run.' : `${evidence.members.length} recorded verification runs.`}
                {' '}Recorded checkpoint #{evidence.checkpoint.checkpointNumber} ({evidence.checkpoint.id}).
              </p>
              <ol className="dc-attempt-recorded-verification-items">
                {evidence.members.map((member) => (
                  <li key={member.executionId}>
                    <span>{describeMember(member)}</span>
                    <ul>
                      {(['stdout', 'stderr'] as const).map((stream) => {
                        const descriptor = stream === 'stdout' ? member.standardOutput : member.standardError
                        const chosen = selection?.executionId === member.executionId && selection.stream === stream
                        return (
                          <li key={stream}>
                            <span>{describeStream(stream, descriptor)}</span>{' '}
                            {descriptor.status === 'Recorded' && (
                              <button
                                type="button"
                                aria-pressed={chosen}
                                aria-label={`${chosen ? 'Hide' : 'View'} ${STREAM_LABELS[stream].toLowerCase()} of check ${member.sequence + 1}`}
                                onClick={chosen ? clearSelection : () => select(member.executionId, stream)}
                              >
                                {chosen ? 'Hide' : 'View'}
                              </button>
                            )}
                            {chosen && (
                              <VerificationStreamWindowView
                                label={`${STREAM_LABELS[stream]} of check ${member.sequence + 1}`}
                                state={window.state}
                                load={window.load}
                              />
                            )}
                          </li>
                        )
                      })}
                    </ul>
                  </li>
                ))}
              </ol>
            </div>
          ) : null}
        </div>
      ) : null}
    </section>
  )
}
