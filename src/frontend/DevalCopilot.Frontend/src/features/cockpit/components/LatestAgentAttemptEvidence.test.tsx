// @vitest-environment jsdom
import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { AgentProcessExecutionResponse, AgentTokenUsageResponse, RunCockpitAgentAttemptResponse } from '../../../api/generated/api-client'
import { LatestAgentAttemptEvidence } from './LatestAgentAttemptEvidence'

function attempt(provider: string, requestedModel?: string) {
  return new RunCockpitAgentAttemptResponse({
    attemptId: 'attempt-1',
    attemptNumber: 3,
    role: provider === 'ClaudeCode' ? 'CriticalReviewer' : 'Planner',
    provider,
    status: 'Running',
    processExecution: new AgentProcessExecutionResponse({ status: 'Unknown' } as never),
    tokenUsage: new AgentTokenUsageResponse({ status: 'Unknown' } as never),
    requestedModel,
  })
}

describe('LatestAgentAttemptEvidence model request', () => {
  it('labels a Claude attempt\'s snapshot as a claim-time request that is not observed', () => {
    render(<LatestAgentAttemptEvidence attempt={attempt('ClaudeCode', 'haiku')} />)

    expect(
      screen.getByText('Model requested at claim: haiku (a request only; the model actually used is not observed).'),
    ).toBeTruthy()
  })

  it('states that no request is recorded, without inferring a default model, when the snapshot is null', () => {
    render(<LatestAgentAttemptEvidence attempt={attempt('ClaudeCode')} />)

    expect(
      screen.getByText('No model request recorded for this attempt (the model actually used is not observed).'),
    ).toBeTruthy()
  })

  it('does not add a model-request line for a non-Claude attempt', () => {
    render(<LatestAgentAttemptEvidence attempt={attempt('Codex', 'gpt-6-sol')} />)

    expect(screen.queryByText(/Model requested at claim/)).toBeNull()
    expect(screen.queryByText(/No model request recorded/)).toBeNull()
  })
})
