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

describe('LatestAgentAttemptEvidence effort request', () => {
  function withEffort(provider: string, requestedModel?: string, requestedEffort?: string) {
    return new RunCockpitAgentAttemptResponse({
      attemptId: 'attempt-1',
      attemptNumber: 3,
      role: provider === 'ClaudeCode' ? 'CriticalReviewer' : 'Planner',
      provider,
      status: 'Running',
      processExecution: new AgentProcessExecutionResponse({ status: 'Unknown' } as never),
      tokenUsage: new AgentTokenUsageResponse({ status: 'Unknown' } as never),
      requestedModel,
      requestedEffort,
    })
  }

  it('labels a Claude attempt\'s claim-time effort as a request that is neither observed nor guaranteed', () => {
    render(<LatestAgentAttemptEvidence attempt={withEffort('ClaudeCode', 'opus', 'high')} />)

    expect(
      screen.getByText(
        'Effort requested at claim: high (a request only; the effort actually applied is not observed and may be adjusted by the provider).',
      ),
    ).toBeTruthy()
  })

  it('states that no effort request is recorded when the snapshot is null', () => {
    render(<LatestAgentAttemptEvidence attempt={withEffort('ClaudeCode', 'haiku')} />)

    expect(
      screen.getByText('No effort request recorded for this attempt (the effort actually applied is not observed).'),
    ).toBeTruthy()
  })

  it('adds no effort line for a non-Claude attempt even when a Codex effort is stored', () => {
    render(<LatestAgentAttemptEvidence attempt={withEffort('Codex', 'gpt-6-sol', 'high')} />)

    expect(screen.queryByText(/Effort requested at claim/)).toBeNull()
    expect(screen.queryByText(/No effort request recorded/)).toBeNull()
  })
})
