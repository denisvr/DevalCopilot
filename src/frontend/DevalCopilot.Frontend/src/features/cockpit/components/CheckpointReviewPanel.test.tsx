import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { CheckpointReviewPanel } from './CheckpointReviewPanel'

const record = vi.fn().mockResolvedValue(true)

vi.mock('../hooks/useProjectGitEvidence', () => ({
  useProjectGitEvidence: () => ({
    evidence: { checkpointId: 'checkpoint-1', fingerprintSha256: 'a'.repeat(64) },
  }),
}))

vi.mock('../hooks/useProjectVerificationExecutions', () => ({
  useProjectVerificationExecutions: () => ({
    executions: [
      {
        verificationExecutionId: 'execution-interrupted',
        gitCheckpointId: 'checkpoint-1',
        checkpointFingerprintSha256: 'a'.repeat(64),
        status: 'Interrupted',
        executionNumber: 5,
      },
      {
        verificationExecutionId: 'execution-source-changed',
        gitCheckpointId: 'checkpoint-1',
        checkpointFingerprintSha256: 'a'.repeat(64),
        status: 'SourceChanged',
        executionNumber: 6,
      },
      {
        verificationExecutionId: 'execution-1',
        gitCheckpointId: 'checkpoint-1',
        checkpointFingerprintSha256: 'a'.repeat(64),
        status: 'Passed',
        executionNumber: 4,
      },
    ],
  }),
}))

vi.mock('../hooks/useProjectCheckpointReviews', () => ({
  useProjectCheckpointReviews: () => ({
    reviews: [{
      reviewId: 'review-1',
      checkpointNumber: 2,
      decision: 'Approved',
      isApplicable: false,
      staleReasonCode: 'review.source_changed',
      verificationExecutionNumber: 4,
      actorKind: 'Human',
    }],
    error: null,
    saving: false,
    record,
  }),
}))

describe('CheckpointReviewPanel', () => {
  it('does not present stale approval as current and binds a new approval to current evidence', async () => {
    render(<CheckpointReviewPanel projectId="project-1" />)

    expect(screen.getByText('A previous approval is no longer applicable because the source checkpoint changed.')).toBeInTheDocument()
    expect(screen.getByText('Checkpoint #2 · verification #4 · Human')).toBeInTheDocument()
    expect(screen.getByText('Historical review · Source changed since this review')).toBeInTheDocument()

    expect(screen.getByRole('button', { name: 'Changes requested' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Escalate' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Approve' })).toBeDisabled()

    fireEvent.change(screen.getByRole('combobox', { name: 'Verification evidence' }), { target: { value: 'execution-1' } })
    fireEvent.click(screen.getByRole('button', { name: 'Approve' }))

    await waitFor(() => expect(record).toHaveBeenCalledWith('checkpoint-1', 'execution-1', 'Approved', 'Human'))
  })

  it('offers non-approval decisions for interrupted and source-changed terminal evidence', async () => {
    render(<CheckpointReviewPanel projectId="project-1" />)

    fireEvent.click(screen.getByRole('button', { name: 'Changes requested' }))
    await waitFor(() => expect(record).toHaveBeenCalledWith('checkpoint-1', 'execution-interrupted', 'ChangesRequested', 'Human'))

    fireEvent.change(screen.getByRole('combobox', { name: 'Verification evidence' }), { target: { value: 'execution-source-changed' } })
    fireEvent.click(screen.getByRole('button', { name: 'Escalate' }))
    await waitFor(() => expect(record).toHaveBeenCalledWith('checkpoint-1', 'execution-source-changed', 'Escalated', 'Human'))
  })
})
