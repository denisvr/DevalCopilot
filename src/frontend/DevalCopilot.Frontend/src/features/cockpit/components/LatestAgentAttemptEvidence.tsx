import type { RunCockpitAgentAttemptResponse } from '../../../api/clients'
import { ProcessEvidenceLine } from './ProcessEvidenceLine'
import { TokenUsageLine } from './TokenUsageLine'

interface LatestAgentAttemptEvidenceProps {
  attempt: RunCockpitAgentAttemptResponse | null | undefined
}

const ROLE_LABEL: Record<string, string> = {
  Planner: 'Planner',
  CriticalReviewer: 'Critical reviewer',
  Resolver: 'Resolver',
  Implementer: 'Implementer',
  CodeReviewer: 'Code reviewer',
}

const PROVIDER_LABEL: Record<string, string> = {
  Codex: 'Codex',
  ClaudeCode: 'Claude Code',
}

/**
 * The run's most recent Agent attempt, as the cockpit projection reports it: the semantic
 * outcome, the host-measured process evidence, and the provider-reported token usage are rendered
 * as distinct facts, so a clean process exit is never read as a workflow success and a timeout is
 * never read as a semantic classification.
 */
export function LatestAgentAttemptEvidence({ attempt }: LatestAgentAttemptEvidenceProps) {
  if (!attempt) {
    return null
  }

  const role = ROLE_LABEL[attempt.role ?? ''] ?? 'Unknown role'
  const provider = PROVIDER_LABEL[attempt.provider ?? ''] ?? 'Unknown provider'
  const semanticResult = attempt.outcome ?? (attempt.status === 'Running' ? 'In progress' : attempt.status ?? 'Unknown')

  return (
    <section className="dc-latest-agent-attempt" aria-label="Latest agent attempt">
      <p className="dc-latest-agent-attempt-summary">
        Latest agent attempt #{attempt.attemptNumber} · {role} · {provider} · Result: {semanticResult}
      </p>
      <ProcessEvidenceLine
        processExecution={attempt.processExecution}
        dispatchedAtUtc={attempt.dispatchedAtUtc}
        status={attempt.status}
      />
      <TokenUsageLine tokenUsage={attempt.tokenUsage} dispatchedAtUtc={attempt.dispatchedAtUtc} status={attempt.status} />
    </section>
  )
}
