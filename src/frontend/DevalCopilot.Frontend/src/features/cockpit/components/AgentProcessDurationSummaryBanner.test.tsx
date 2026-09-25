import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { AgentProcessDurationSummaryBanner } from './AgentProcessDurationSummaryBanner'

describe('AgentProcessDurationSummaryBanner', () => {
  it('renders nothing when no Agent attempt has been dispatched', () => {
    const { container } = render(
      <AgentProcessDurationSummaryBanner
        status="NoDispatchedAttempts"
        totalMeasuredMilliseconds={null}
        dispatchedAttemptCount={0}
        pendingAttemptCount={0}
        validEvidenceCount={0}
        malformedEvidenceCount={0}
      />,
    )
    expect(container).toBeEmptyDOMElement()
  })

  it('shows the measured total for complete evidence, including a real zero-duration measurement', () => {
    render(
      <AgentProcessDurationSummaryBanner
        status="Complete"
        totalMeasuredMilliseconds={0}
        dispatchedAttemptCount={1}
        pendingAttemptCount={0}
        validEvidenceCount={1}
        malformedEvidenceCount={0}
      />,
    )
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.getByText(/0 ms/)).toBeInTheDocument()
    expect(screen.getByText(/measured agent process duration/i)).toBeInTheDocument()
  })

  it('shows a non-zero measured total distinctly from a zero one', () => {
    render(
      <AgentProcessDurationSummaryBanner
        status="Complete"
        totalMeasuredMilliseconds={4500}
        dispatchedAttemptCount={2}
        pendingAttemptCount={0}
        validEvidenceCount={2}
        malformedEvidenceCount={0}
      />,
    )
    expect(screen.getByText(/4\.5 s/)).toBeInTheDocument()
  })

  it('shows a positive sub-millisecond total as "< 1 ms", never as zero', () => {
    render(
      <AgentProcessDurationSummaryBanner
        status="Complete"
        totalMeasuredMilliseconds={0.05}
        dispatchedAttemptCount={1}
        pendingAttemptCount={0}
        validEvidenceCount={1}
        malformedEvidenceCount={0}
      />,
    )
    expect(screen.getByText(/< 1 ms/)).toBeInTheDocument()
    expect(screen.queryByText(/0 ms/)).not.toBeInTheDocument()
  })

  it('shows an exact 1 ms total as "1 ms", not rounded down to "0 s"', () => {
    render(
      <AgentProcessDurationSummaryBanner
        status="Complete"
        totalMeasuredMilliseconds={1}
        dispatchedAttemptCount={1}
        pendingAttemptCount={0}
        validEvidenceCount={1}
        malformedEvidenceCount={0}
      />,
    )
    expect(screen.getByText(/1 ms/)).toBeInTheDocument()
    expect(screen.queryByText(/0 s/)).not.toBeInTheDocument()
  })

  it('surfaces an unrepresentable total distinctly from malformed evidence', () => {
    render(
      <AgentProcessDurationSummaryBanner
        status="UnrepresentableTotal"
        totalMeasuredMilliseconds={null}
        dispatchedAttemptCount={2}
        pendingAttemptCount={0}
        validEvidenceCount={2}
        malformedEvidenceCount={0}
      />,
    )
    const alert = screen.getByRole('alert')
    expect(alert).toHaveTextContent(/too large to represent/i)
    expect(alert).toHaveTextContent(/every individual measurement is valid/i)
  })

  it('surfaces an unrepresentable total mixed with pending attempts', () => {
    render(
      <AgentProcessDurationSummaryBanner
        status="UnrepresentableTotal"
        totalMeasuredMilliseconds={null}
        dispatchedAttemptCount={3}
        pendingAttemptCount={1}
        validEvidenceCount={2}
        malformedEvidenceCount={0}
      />,
    )
    expect(screen.getByRole('alert')).toHaveTextContent(/still running/i)
  })

  it('reports pending evidence without ever showing a measured total', () => {
    render(
      <AgentProcessDurationSummaryBanner
        status="PendingEvidence"
        totalMeasuredMilliseconds={null}
        dispatchedAttemptCount={2}
        pendingAttemptCount={1}
        validEvidenceCount={1}
        malformedEvidenceCount={0}
      />,
    )
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.getByText(/still running/i)).toBeInTheDocument()
    expect(screen.queryByText(/measured agent process duration/i)).not.toBeInTheDocument()
  })

  it('surfaces partial evidence as an alert distinct from a complete total', () => {
    render(
      <AgentProcessDurationSummaryBanner
        status="PartialEvidence"
        totalMeasuredMilliseconds={null}
        dispatchedAttemptCount={3}
        pendingAttemptCount={0}
        validEvidenceCount={2}
        malformedEvidenceCount={1}
      />,
    )
    const alert = screen.getByRole('alert')
    expect(alert).toHaveTextContent(/partial host-measured process-duration evidence/i)
    expect(alert).toHaveTextContent(/not shown/i)
  })

  it('surfaces partial evidence mixed with pending attempts', () => {
    render(
      <AgentProcessDurationSummaryBanner
        status="PartialEvidence"
        totalMeasuredMilliseconds={null}
        dispatchedAttemptCount={4}
        pendingAttemptCount={1}
        validEvidenceCount={2}
        malformedEvidenceCount={1}
      />,
    )
    expect(screen.getByRole('alert')).toHaveTextContent(/still running/i)
  })

  it('surfaces fully malformed evidence as an alert, never as a zero-duration measurement', () => {
    render(
      <AgentProcessDurationSummaryBanner
        status="MalformedEvidence"
        totalMeasuredMilliseconds={null}
        dispatchedAttemptCount={1}
        pendingAttemptCount={0}
        validEvidenceCount={0}
        malformedEvidenceCount={1}
      />,
    )
    const alert = screen.getByRole('alert')
    expect(alert).toHaveTextContent(/none of this run's terminal agent attempts carries valid/i)
    expect(alert).toHaveTextContent(/not a zero-duration measurement/i)
  })

  it('never implies every attempt is terminal when malformed evidence coexists with a pending attempt', () => {
    render(
      <AgentProcessDurationSummaryBanner
        status="MalformedEvidence"
        totalMeasuredMilliseconds={null}
        dispatchedAttemptCount={2}
        pendingAttemptCount={1}
        validEvidenceCount={0}
        malformedEvidenceCount={1}
      />,
    )
    const alert = screen.getByRole('alert')
    expect(alert).toHaveTextContent(/still running/i)
    expect(alert).not.toHaveTextContent(/have reached a terminal result/i)
  })
})
