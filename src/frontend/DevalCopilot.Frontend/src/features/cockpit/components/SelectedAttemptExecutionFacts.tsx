import type { AgentAttemptEvidenceResponse } from '../../../api/clients'
import { toAgentAttemptExecutionFacts } from '../agentAttemptExecutionFacts'
import type { AttemptInstantFact, AttemptTimeoutFact } from '../agentAttemptExecutionFacts'
import { formatMilliseconds } from '../describeProcessEvidence'
import { ProcessEvidenceLine } from './ProcessEvidenceLine'
import { TokenUsageLine } from './TokenUsageLine'

const formatUtc = (at: Date) => `${at.toISOString().slice(0, 19).replace('T', ' ')} UTC`

function describeTimes(claimed: AttemptInstantFact, dispatched: AttemptInstantFact, completed: AttemptInstantFact): string {
  const parts = [claimed.status === 'recorded' ? `Claimed ${formatUtc(claimed.at)}` : 'Claim time unavailable']
  parts.push(
    dispatched.status === 'recorded'
      ? `dispatched ${formatUtc(dispatched.at)}`
      : dispatched.status === 'absent'
        ? 'not dispatched'
        : 'dispatch time unavailable',
  )
  if (completed.status === 'recorded') {
    parts.push(`ended ${formatUtc(completed.at)}`)
  } else if (completed.status === 'unavailable') {
    parts.push('end time unavailable')
  }
  return `${parts.join(', ')}.`
}

function describeSemanticResult(evidence: AgentAttemptEvidenceResponse): string {
  if (typeof evidence.outcome === 'string' && evidence.outcome.length > 0) {
    return `Semantic result: ${evidence.outcome}`
  }
  return evidence.attemptStatus === 'Running' ? 'Semantic result: not yet recorded (the attempt is still running)' : 'Semantic result: not recorded'
}

function describeTimeout(timeout: AttemptTimeoutFact): string {
  switch (timeout.status) {
    case 'known':
      return `Configured timeout: ${formatMilliseconds(timeout.milliseconds)} (set when the attempt was claimed; not a measurement)`
    case 'unknown':
      return 'Configured timeout: not recorded'
    default:
      return 'Configured timeout unavailable: the recorded value is not coherent, so none is shown'
  }
}

/**
 * The selected attempt's recorded execution facts, each admitted on its own before it reaches the shared process and token lines: the
 * response contract, the claim, dispatch and end times, the semantic result, the host-measured process result, the configured timeout and
 * the provider-reported token counts. A still-running or undispatched attempt shows no measured result or known usage even if its answer
 * carries numbers; a concluded attempt with nothing recorded says so and never shows zero; a malformed or contradictory section is
 * unavailable without hiding a valid sibling. Everything is derived from the one evidence answer already read: no request, storage or log.
 */
export function SelectedAttemptExecutionFacts({ evidence }: { evidence: AgentAttemptEvidenceResponse }) {
  const facts = toAgentAttemptExecutionFacts(evidence)
  const dispatchedAtUtc = facts.dispatched.status === 'recorded' ? facts.dispatched.at : null

  return (
    <section className="dc-attempt-history-facts" aria-label="Recorded execution facts">
      <p>Response contract: {evidence.responseContract}</p>
      <p>{describeTimes(facts.claimed, facts.dispatched, facts.completed)}</p>
      <p className="dc-attempt-history-semantic-result">{describeSemanticResult(evidence)}</p>
      {facts.process.status === 'unavailable' ? (
        <p className="dc-process-evidence" data-process-outcome="Unavailable">
          Process evidence unavailable: the recorded values are not coherent, so no measured result is shown
        </p>
      ) : (
        <ProcessEvidenceLine
          processExecution={facts.process.status === 'known' ? facts.process.value : null}
          dispatchedAtUtc={dispatchedAtUtc}
          status={evidence.attemptStatus}
        />
      )}
      <p className="dc-attempt-history-timeout">{describeTimeout(facts.timeout)}</p>
      {facts.tokenUsage.status === 'unavailable' ? (
        <p className="dc-token-usage" data-token-usage="Unavailable">
          Token usage unavailable: the recorded counts are not coherent, so none is shown
        </p>
      ) : (
        <TokenUsageLine
          tokenUsage={facts.tokenUsage.status === 'known' ? facts.tokenUsage.value : null}
          dispatchedAtUtc={dispatchedAtUtc}
          status={evidence.attemptStatus}
        />
      )}
      <p className="dc-card-evidence-caveat">
        The process result is measured by the host and the token counts are reported by the provider; neither is the semantic result. A
        clean exit does not mean the attempt succeeded, and token counts are not cost, quota, remaining capacity, readiness or approval.
      </p>
    </section>
  )
}
