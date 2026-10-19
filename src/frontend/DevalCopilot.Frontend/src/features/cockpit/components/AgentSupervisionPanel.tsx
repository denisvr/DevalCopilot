import { SUPERVISION_CLOSURE_LABELS, SUPERVISION_PHASE_LABELS, formatSupervisionTime, reportAgeSeconds, secondsToDeadline } from '../agentSupervision'
import type { AgentSupervisionAttempt, AgentSupervisionAttention, AgentSupervisionObservation, AgentSupervisionReading } from '../agentSupervision'
import {
  AGENT_SUPERVISION_DISCLAIMER,
  AGENT_SUPERVISION_FAILURE_TEXT,
  AGENT_SUPERVISION_LOADING,
  AGENT_SUPERVISION_NOT_OBSERVED_NOTE,
  AGENT_SUPERVISION_NO_ACTION_NOTE,
  AGENT_SUPERVISION_READ_AGAIN,
  AGENT_SUPERVISION_STALE_NOTE,
  AGENT_SUPERVISION_STATUS_LABEL,
} from '../agentSupervisionCopy'
import type { UseAgentSupervisionResult } from '../hooks/useAgentSupervision'

interface AgentSupervisionPanelProps {
  /** The run's supervision read (ADR-0056); everything shown derives from this one validated reading. */
  supervision: UseAgentSupervisionResult
  /** Whole seconds since this window received the reading (the browser's clock); null without a reading. Never a heartbeat. */
  readingAgeSeconds: number | null
  /** Whether the displayed reading is older than the bounded refresh allows, so it is shown as stale instead of current. */
  stale: boolean
}

const ROLE_LABEL: Readonly<Record<string, string>> = {
  Planner: 'Planner',
  CriticalReviewer: 'Critical reviewer',
  Resolver: 'Resolver',
  Implementer: 'Implementer',
  CodeReviewer: 'Code reviewer',
}

const PROVIDER_LABEL: Readonly<Record<string, string>> = { Codex: 'Codex', ClaudeCode: 'Claude Code' }

function describeAttempt(attempt: AgentSupervisionAttempt): string[] {
  const outcome = attempt.outcome === null ? '' : ` · outcome ${attempt.outcome}`
  return [
    `Attempt #${attempt.attemptNumber} · ${ROLE_LABEL[attempt.role]} · ${PROVIDER_LABEL[attempt.provider]} · ${attempt.responseContract}`,
    `Recorded status ${attempt.status}${outcome} · dispatch marker ${attempt.dispatched ? 'recorded' : 'not recorded'}`,
  ]
}

function describeObservation(observation: AgentSupervisionObservation, observedAtUtc: string): string[] {
  const lines = [SUPERVISION_PHASE_LABELS[observation.phase]]
  const age = reportAgeSeconds(observation, observedAtUtc)
  lines.push(
    `Last host report ${formatSupervisionTime(observation.lastHeartbeatAtUtc)}, ${age} s before this reading (host clock ${formatSupervisionTime(observedAtUtc)}).`,
  )
  if (observation.open) {
    const remaining = secondsToDeadline(observation, observedAtUtc)
    lines.push(
      remaining > 0
        ? `Absolute deadline of this phase ${formatSupervisionTime(observation.phaseDeadlineUtc)} (in ${remaining} s at the host clock).`
        : `Absolute deadline of this phase ${formatSupervisionTime(observation.phaseDeadlineUtc)} has elapsed.`,
    )
  } else if (observation.closureReason !== null && observation.closedAtUtc !== null) {
    lines.push(`Observation ${SUPERVISION_CLOSURE_LABELS[observation.closureReason]} at ${formatSupervisionTime(observation.closedAtUtc)}.`)
  }

  lines.push(
    `Observation revision ${observation.revision} · ${observation.ownedByCurrentHost ? 'written by this host launch' : 'written by a previous host launch'}.`,
  )
  return lines
}

function describeAttention(attention: AgentSupervisionAttention): string[] {
  const when = formatSupervisionTime(attention.effectiveAtUtc)
  return [
    attention.effectiveReasonText,
    attention.recordedReasonCode !== null
      ? `First attention recorded at ${when}.`
      : `First attention at ${when}, derived by this reading and not yet recorded.`,
  ]
}

function ReadingBody({ reading, stale, readingAgeSeconds }: { reading: AgentSupervisionReading; stale: boolean; readingAgeSeconds: number | null }) {
  const terminal = reading.status === 'Ended'
  return (
    <>
      <p className="dc-agent-supervision-status" data-testid="agent-supervision-status">
        {stale && !terminal && reading.status !== 'NoAgentAttempt' ? 'Reading is stale' : AGENT_SUPERVISION_STATUS_LABEL[reading.status]}
      </p>
      {stale && reading.status !== 'NoAgentAttempt' && !terminal && (
        <p className="dc-agent-supervision-stale" role="status">
          {AGENT_SUPERVISION_STALE_NOTE} Last answer {readingAgeSeconds} s ago.
        </p>
      )}
      <p className="dc-agent-supervision-disclaimer">{AGENT_SUPERVISION_DISCLAIMER}</p>
      {reading.attempt !== null && (
        <ul className="dc-agent-supervision-attempt" aria-label="Selected Agent attempt">
          {terminal && (
            <li>
              The attempt ended as recorded: {reading.attempt.status}
              {reading.attempt.outcome === null ? '' : ` · ${reading.attempt.outcome}`}.
            </li>
          )}
          {describeAttempt(reading.attempt).map((line) => (
            <li key={line}>{line}</li>
          ))}
        </ul>
      )}
      {reading.status === 'NotObserved' && <p className="dc-agent-supervision-not-observed">{AGENT_SUPERVISION_NOT_OBSERVED_NOTE}</p>}
      {reading.observation !== null && (
        <ul className="dc-agent-supervision-observation" aria-label="Host observation">
          {describeObservation(reading.observation, reading.observedAtUtc).map((line) => (
            <li key={line}>{line}</li>
          ))}
        </ul>
      )}
      {reading.attention !== null && (
        <div className="dc-agent-supervision-attention" role="status" aria-label={terminal ? 'Attention history' : 'Attention'}>
          {terminal && <p>Attention history, kept as recorded:</p>}
          {describeAttention(reading.attention).map((line) => (
            <p key={line}>{line}</p>
          ))}
        </div>
      )}
      {reading.status === 'Unavailable' && reading.reasonText !== null && <p className="dc-agent-supervision-unavailable">{reading.reasonText}</p>}
      <p className="dc-agent-supervision-next">{reading.nextActionText}</p>
      {reading.status !== 'NoAgentAttempt' && !terminal && <p className="dc-agent-supervision-no-action">{AGENT_SUPERVISION_NO_ACTION_NOTE}</p>}
    </>
  )
}

/**
 * The current host supervision of the run's selected Agent attempt (ADR-0056), read-only. Everything it says is the one validated
 * reading of what the host's own observer last recorded: the phase, its last report and absolute deadline, a first attention with its
 * reason, and the attempt's real recorded status, which governs once it is terminal. It never says that an Agent is working, never
 * renders a heartbeat the browser produced, treats a failed, timed-out or refused read as unavailable instead of keeping an earlier
 * success, and shows an answer that is no longer refreshed as stale. It has no retry, resume, cancel or takeover control, and no
 * transcript, path or artifact body; every host text is literal text.
 */
export function AgentSupervisionPanel({ supervision, readingAgeSeconds, stale }: AgentSupervisionPanelProps) {
  const { reading, loading, failure } = supervision
  return (
    <section
      className="dc-agent-supervision"
      aria-label="Host observation of the current Agent attempt"
      data-testid="agent-supervision-panel"
      data-status={reading?.status ?? (loading ? 'loading' : 'unavailable')}
      data-stale={stale ? 'true' : undefined}
    >
      <h2>Host observation</h2>
      {loading && reading === null && <p className="dc-agent-supervision-loading">{AGENT_SUPERVISION_LOADING}</p>}
      {!loading && reading === null && (
        <p className="dc-agent-supervision-failure" role="alert">
          {AGENT_SUPERVISION_FAILURE_TEXT[failure ?? 'read']}
        </p>
      )}
      {reading !== null && <ReadingBody reading={reading} stale={stale} readingAgeSeconds={readingAgeSeconds} />}
      <button type="button" className="dc-button" onClick={supervision.refresh}>
        {AGENT_SUPERVISION_READ_AGAIN}
      </button>
    </section>
  )
}
