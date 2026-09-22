import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ImplementationAttemptStatusResponse } from '../../../api/generated/api-client'
import { ImplementationAction } from './ImplementationAction'

describe('ImplementationAction', () => {
  it('renders nothing when there is no eligible resolved plan yet', () => {
    const { container } = render(
      <ImplementationAction
        planProposalMessageId={null}
        status={null}
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
      />,
    )

    expect(container).toBeEmptyDOMElement()
  })

  it('offers the request action once an eligible plan exists and no implementation has been made yet', () => {
    const onRequest = vi.fn()
    render(
      <ImplementationAction
        planProposalMessageId="proposal-1"
        status={null}
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={onRequest}
      />,
    )

    fireEvent.click(screen.getByRole('button', { name: 'Implement the resolved plan with Claude' }))
    expect(onRequest).toHaveBeenCalledTimes(1)
  })

  it('shows a visibly-working pending state before dispatch, with no action button', () => {
    render(
      <ImplementationAction
        planProposalMessageId="proposal-1"
        status={
          new ImplementationAttemptStatusResponse({
            attemptId: 'attempt-1',
            attemptNumber: 2,
            status: 'Running',
            planProposalMessageId: 'proposal-1',
            changedRelativePaths: [],
          })
        }
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
      />,
    )

    expect(screen.getByText(/pending/i)).toHaveAttribute('aria-busy', 'true')
    expect(screen.queryByRole('button', { name: 'Implement the resolved plan with Claude' })).not.toBeInTheDocument()
  })

  it('withholds the request action once the current plan already reached a durable Implemented outcome, showing summary and changed files', () => {
    render(
      <ImplementationAction
        planProposalMessageId="proposal-1"
        status={
          new ImplementationAttemptStatusResponse({
            attemptId: 'attempt-1',
            attemptNumber: 2,
            status: 'Completed',
            outcome: 'Implemented',
            planProposalMessageId: 'proposal-1',
            startingCheckpointFingerprintSha256: 'a'.repeat(64),
            resultCheckpointFingerprintSha256: 'b'.repeat(64),
            executionReportSummary: 'Added the ledger table and its query.',
            changedRelativePaths: ['src/Foo.cs', 'tests/Foo.Tests.cs'],
          })
        }
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
      />,
    )

    expect(screen.getByText('Last attempt #2: Implemented.')).toBeInTheDocument()
    expect(screen.getByText('Added the ledger table and its query.')).toBeInTheDocument()
    expect(screen.getByText('src/Foo.cs')).toBeInTheDocument()
    expect(screen.getByText('tests/Foo.Tests.cs')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Implement the resolved plan with Claude' })).not.toBeInTheDocument()
  })

  it('shows bounded requested and observed assignment facts for an existing attempt', () => {
    render(
      <ImplementationAction
        planProposalMessageId="proposal-1"
        status={
          new ImplementationAttemptStatusResponse({
            hasAttempt: true,
            attemptId: 'attempt-1',
            attemptNumber: 1,
            status: 'Completed',
            outcome: 'NoChangesProduced',
            planProposalMessageId: 'proposal-1',
            provider: 'ClaudeCode',
            role: 'Implementer',
            requestedModel: 'claude-model-requested',
            observedModel: 'claude-model-observed',
            requestedEffort: 'high-requested',
            observedEffort: 'medium-observed',
            permissionProfile: 'WorkspaceEditOnly',
            adapterContractVersion: 'claude-implementation-v1',
            changedRelativePaths: [],
          })
        }
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
      />,
    )

    expect(screen.getByText(/Claude Code · Implementer · Model requested: claude-model-requested/)).toBeInTheDocument()
    expect(screen.getByText(/Model observed: claude-model-observed · Effort requested: high-requested · Effort observed: medium-observed/)).toBeInTheDocument()
    expect(screen.getByText(/Workspace edit only · Adapter contract: claude-implementation-v1/)).toBeInTheDocument()
    expect(screen.queryByText(/C:\\|credential|environment|prompt|transcript|raw output/i)).not.toBeInTheDocument()
  })

  it('shows truthful Unknown values for a historical assignment with nullable facts', () => {
    render(
      <ImplementationAction
        planProposalMessageId="proposal-1"
        status={new ImplementationAttemptStatusResponse({
          hasAttempt: true,
          attemptId: 'historical-attempt',
          attemptNumber: 1,
          status: 'Failed',
          outcome: 'NoChangesProduced',
          planProposalMessageId: 'proposal-1',
          provider: 'ClaudeCode',
          role: 'Implementer',
          permissionProfile: 'Unknown',
          changedRelativePaths: [],
        })}
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
      />,
    )

    expect(screen.getByText(/Model requested: Unknown · Model observed: Unknown · Effort requested: Unknown · Effort observed: Unknown/)).toBeInTheDocument()
    expect(screen.getByText(/Unknown · Adapter contract: Unknown/)).toBeInTheDocument()
  })

  it('does not render assignment or last-attempt identity when the backend says there is no attempt', () => {
    render(
      <ImplementationAction
        planProposalMessageId="proposal-1"
        status={new ImplementationAttemptStatusResponse({ hasAttempt: false, changedRelativePaths: [], artifacts: [] })}
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
      />,
    )

    expect(screen.queryByText(/Last attempt/)).not.toBeInTheDocument()
    expect(screen.queryByText(/Unknown/)).not.toBeInTheDocument()
    expect(screen.queryByText(/Last attempt #undefined/)).not.toBeInTheDocument()
  })

  it('does not display unknown backend assignment codes verbatim', () => {
    render(
      <ImplementationAction
        planProposalMessageId="proposal-1"
        status={new ImplementationAttemptStatusResponse({
          hasAttempt: true,
          attemptId: 'attempt-1',
          attemptNumber: 1,
          status: 'Failed',
          planProposalMessageId: 'proposal-1',
          provider: 'UnrecognizedProviderSentinel',
          role: 'UnrecognizedRoleSentinel',
          permissionProfile: 'UnrecognizedProfileSentinel',
          adapterContractVersion: 'unrecognized-contract-sentinel',
          changedRelativePaths: [],
        })}
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
      />,
    )

    expect(screen.getByText(/Unknown · Unknown · Model requested: Unknown/)).toBeInTheDocument()
    expect(screen.queryByText(/UnrecognizedProviderSentinel|UnrecognizedRoleSentinel|UnrecognizedProfileSentinel|unrecognized-contract-sentinel/)).not.toBeInTheDocument()
  })

  it('allows requesting a fresh implementation once a newer resolved plan supersedes an already-implemented one', () => {
    render(
      <ImplementationAction
        planProposalMessageId="proposal-2"
        status={
          new ImplementationAttemptStatusResponse({
            attemptId: 'attempt-1',
            attemptNumber: 2,
            status: 'Completed',
            outcome: 'Implemented',
            planProposalMessageId: 'proposal-1',
            changedRelativePaths: [],
          })
        }
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
      />,
    )

    expect(screen.getByRole('button', { name: 'Implement the resolved plan with Claude' })).toBeInTheDocument()
  })

  it.each([
    ['NoChangesProduced', 'Claude reported no changes'],
    ['InvalidStructuredOutput', 'Claude returned an invalid or untrustworthy response'],
    ['ProviderInvocationFailed', 'Claude could not be invoked'],
    ['CheckpointEvidenceUnavailable', 'Source evidence could not be captured'],
    ['WorkspaceNoLongerEligible', 'Workspace no longer eligible for dispatch'],
  ])('labels the %s terminal outcome safely and still allows a retry', (outcome, label) => {
    render(
      <ImplementationAction
        planProposalMessageId="proposal-1"
        status={
          new ImplementationAttemptStatusResponse({
            attemptId: 'attempt-1',
            attemptNumber: 3,
            status: 'Failed',
            outcome,
            planProposalMessageId: 'proposal-1',
            changedRelativePaths: [],
          })
        }
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
      />,
    )

    expect(screen.getByText(`Last attempt #3: ${label}.`)).toBeInTheDocument()
    expect(screen.queryByText(new RegExp(outcome))).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Implement the resolved plan with Claude' })).toBeInTheDocument()
  })

  it('disables the request button while a request is in flight', () => {
    render(
      <ImplementationAction
        planProposalMessageId="proposal-1"
        status={null}
        statusLoading={false}
        statusError={null}
        requesting={true}
        requestError={null}
        onRequest={vi.fn()}
      />,
    )

    expect(screen.getByRole('button', { name: 'Requesting…' })).toBeDisabled()
  })

  it('offers the only supported implementation action without a provider selector', () => {
    render(
      <ImplementationAction
        planProposalMessageId="proposal-1"
        status={null}
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
      />,
    )

    expect(screen.queryByRole('combobox')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Implement the resolved plan with Claude' })).toBeEnabled()
  })

  it('surfaces a safe request-failure message without discarding the last known status', () => {
    render(
      <ImplementationAction
        planProposalMessageId="proposal-1"
        status={
          new ImplementationAttemptStatusResponse({
            attemptId: 'attempt-1',
            attemptNumber: 3,
            status: 'Failed',
            outcome: 'ProviderInvocationFailed',
            planProposalMessageId: 'proposal-1',
            changedRelativePaths: [],
          })
        }
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError="This resolved plan already has a successful implementation."
        onRequest={vi.fn()}
      />,
    )

    expect(screen.getByRole('status')).toHaveTextContent('This resolved plan already has a successful implementation.')
    expect(screen.getByText(/Last attempt #3/)).toBeInTheDocument()
  })

  it('surfaces a safe status-read failure', () => {
    render(
      <ImplementationAction
        planProposalMessageId="proposal-1"
        status={null}
        statusLoading={false}
        statusError="Implementation attempt status is unavailable."
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
      />,
    )

    expect(screen.getByRole('status')).toHaveTextContent('Implementation attempt status is unavailable.')
  })
})
