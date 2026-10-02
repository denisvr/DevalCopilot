import { fireEvent, render, screen } from '@testing-library/react'
import type { ComponentProps } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { VerificationDiagnosisMemberResponse, VerificationDiagnosisStatusResponse } from '../../../api/generated/api-client'
import { VerificationDiagnosisAction } from './VerificationDiagnosisAction'

function status(overrides: Partial<ConstructorParameters<typeof VerificationDiagnosisStatusResponse>[0]> = {}) {
  return new VerificationDiagnosisStatusResponse({
    hasAttempt: false,
    artifacts: [],
    verification: [],
    maximumReviewCorrectionAttempts: 3,
    reviewCorrectionAttemptsUsed: 0,
    ...overrides,
  })
}

function findingsStatus(overrides: Partial<ConstructorParameters<typeof VerificationDiagnosisStatusResponse>[0]> = {}) {
  return status({
    hasAttempt: true,
    attemptId: 'diagnosis-1',
    attemptNumber: 1,
    executionReportMessageId: 'report-1',
    status: 'Completed',
    outcome: 'DiagnosisFindingsRecorded',
    findingCount: 2,
    correctionApplicable: true,
    verification: [
      new VerificationDiagnosisMemberResponse({ position: 1, commandName: 'unit', executionNumber: 3, status: 'Failed', exitCode: 1 }),
      new VerificationDiagnosisMemberResponse({ position: 2, commandName: 'lint', executionNumber: 2, status: 'Passed', exitCode: 0 }),
    ],
    ...overrides,
  })
}

function renderAction(overrides: Partial<ComponentProps<typeof VerificationDiagnosisAction>> = {}) {
  return render(
    <VerificationDiagnosisAction
      status={status()}
      statusLoading={false}
      statusError={null}
      requesting={false}
      requestError={null}
      onRequest={vi.fn()}
      correctionRequesting={false}
      correctionError={null}
      onRequestCorrection={vi.fn()}
      globalClaimBlock={null}
      timeFit={{ reason: 'Fits' }}
      correctionTimeFit={{ reason: 'Fits' }}
      {...overrides}
    />,
  )
}

const diagnoseButton = () => screen.queryByRole('button', { name: 'Diagnose failed verification with Codex' })
const correctButton = () => screen.queryByRole('button', { name: 'Correct the diagnosed findings with Claude' })

describe('VerificationDiagnosisAction', () => {
  it('renders nothing before any status or status error is known', () => {
    const { container } = renderAction({ status: null })
    expect(container).toBeEmptyDOMElement()
  })

  it('shows only the safe status error when the status could not be read', () => {
    renderAction({ status: null, statusError: 'Verification diagnosis status is unavailable.' })
    expect(screen.getByText('Verification diagnosis status is unavailable.')).toBeInTheDocument()
    expect(diagnoseButton()).not.toBeInTheDocument()
  })

  it('labels itself as a read-only diagnosis, distinct from the ordinary code review, and never as an approval', () => {
    renderAction({ status: findingsStatus() })
    const section = screen.getByRole('region', { name: 'Verification diagnosis' })
    expect(section).toHaveTextContent('Diagnosis of failed local verification (Codex, read-only)')
    expect(section).toHaveTextContent('This is not a code review')
    expect(section.textContent).not.toMatch(/approv|review passed/i)
    expect(screen.queryByRole('region', { name: 'Code review' })).not.toBeInTheDocument()
  })

  it('offers the diagnosis request when the host names a diagnosable report', () => {
    const onRequest = vi.fn()
    renderAction({ status: status({ diagnosableExecutionReportMessageId: 'report-1' }), onRequest })
    fireEvent.click(diagnoseButton()!)
    expect(onRequest).toHaveBeenCalledTimes(1)
  })

  it('disables the request while requesting or while the status is loading', () => {
    const { rerender } = renderAction({ status: status({ diagnosableExecutionReportMessageId: 'report-1' }), requesting: true })
    expect(screen.getByRole('button', { name: 'Requesting…' })).toBeDisabled()
    rerender(
      <VerificationDiagnosisAction
        status={status({ diagnosableExecutionReportMessageId: 'report-1' })}
        statusLoading
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
        correctionRequesting={false}
        correctionError={null}
        onRequestCorrection={vi.fn()}
        globalClaimBlock={null}
        timeFit={{ reason: 'Fits' }}
        correctionTimeFit={{ reason: 'Fits' }}
      />,
    )
    expect(diagnoseButton()).toBeDisabled()
  })

  it.each([
    ['verification_diagnosis.no_failed_verification', /Request the ordinary code review instead/],
    ['verification_diagnosis.evidence_running', /Verification is still running/],
    ['verification_diagnosis.evidence_missing', /Verification has not been run/],
    ['verification_diagnosis.workspace_not_ready', /workspace is not ready/],
    ['verification_diagnosis.no_current_execution_report', /no current implementation report/],
    ['verification_diagnosis.no_verification_commands_enabled', /No verification command is enabled/],
    ['verification_diagnosis.evidence_not_diagnosable', /cannot be diagnosed/],
    ['verification_diagnosis.failed_output_unavailable', /output of the failed verification is unavailable/],
    ['agent_attempts.already_diagnosed', /already diagnosed/],
  ])('explains why nothing can be diagnosed for %s and offers no request', (code, copy) => {
    renderAction({ status: status({ diagnosisUnavailableCode: code }) })
    expect(screen.getByText(copy)).toBeInTheDocument()
    expect(diagnoseButton()).not.toBeInTheDocument()
  })

  it('falls back to a generic reason for an unknown unavailable code, never echoing it', () => {
    renderAction({ status: status({ diagnosisUnavailableCode: 'verification_diagnosis.something_new' }) })
    expect(screen.getByText('No failed local verification can be diagnosed right now.')).toBeInTheDocument()
    expect(screen.queryByText(/something_new/)).not.toBeInTheDocument()
  })

  it('shows a busy pending state while a diagnosis runs, with neither control available', () => {
    renderAction({
      status: findingsStatus({ status: 'Running', outcome: undefined, correctionApplicable: false, diagnosableExecutionReportMessageId: 'report-1' }),
    })
    expect(screen.getByText(/Verification diagnosis pending…/)).toHaveAttribute('aria-busy', 'true')
    expect(diagnoseButton()).not.toBeInTheDocument()
    expect(correctButton()).not.toBeInTheDocument()
  })

  it('shows Running once the diagnosis was dispatched', () => {
    renderAction({
      status: findingsStatus({ status: 'Running', outcome: undefined, dispatchedAtUtc: new Date('2026-09-19T00:00:00Z') }),
    })
    expect(screen.getByText(/Verification diagnosis running…/)).toBeInTheDocument()
  })

  it.each([
    ['InvalidStructuredOutput', 'Codex returned an invalid structured diagnosis'],
    ['ProviderInvocationFailed', 'Codex could not be invoked for the diagnosis'],
    ['CheckpointEvidenceUnavailable', 'Source evidence could not be captured'],
    ['SourceChanged', 'Source changed before the diagnosis completed'],
    ['WorkspaceNoLongerEligible', 'Workspace no longer eligible for dispatch'],
  ])('reports the failed outcome %s truthfully, with no repair control, and allows an explicit new request', (outcome, label) => {
    renderAction({
      status: status({
        hasAttempt: true,
        attemptNumber: 2,
        status: 'Failed',
        outcome,
        diagnosableExecutionReportMessageId: 'report-1',
      }),
    })
    expect(screen.getByText(`Last diagnosis #2: ${label}.`)).toBeInTheDocument()
    expect(screen.getByText(/has no repair attempt/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /repair/i })).not.toBeInTheDocument()
    expect(diagnoseButton()).toBeEnabled()
  })

  it('says the verification evidence changed and to request a new diagnosis', () => {
    renderAction({
      status: status({ hasAttempt: true, attemptNumber: 1, status: 'Failed', outcome: 'VerificationEvidenceChanged' }),
    })
    expect(screen.getByText('Last diagnosis #1: The verification evidence changed before the diagnosis completed.')).toBeInTheDocument()
    expect(screen.getByText(/Run or inspect verification again and request a new diagnosis/)).toBeInTheDocument()
  })

  it('says an already-diagnosed input recorded nothing new', () => {
    renderAction({
      status: status({ hasAttempt: true, attemptNumber: 4, status: 'Completed', outcome: 'InputAlreadyDiagnosed' }),
    })
    expect(screen.getByText('Last diagnosis #4: This failed verification was already diagnosed.')).toBeInTheDocument()
    expect(screen.getByText(/nothing new was recorded/)).toBeInTheDocument()
  })

  it('shows the finding count, the pinned verification list with status and exit code, and the shared allowance', () => {
    renderAction({
      status: findingsStatus({ maximumReviewCorrectionAttempts: 3, reviewCorrectionAttemptsUsed: 1 }),
    })
    expect(screen.getByText(/Codex recorded 2 findings/)).toBeInTheDocument()
    const members = screen.getByRole('list', { name: 'Verification evidence pinned by this diagnosis' })
    expect(members).toHaveTextContent('#1 unit · execution 3 · Failed · exit code 1')
    expect(members).toHaveTextContent('#2 lint · execution 2 · Passed · exit code 0')
    expect(screen.getByText(/shared with ordinary review corrections: 1 of 3 used/)).toBeInTheDocument()
  })

  it('uses the singular for one finding', () => {
    renderAction({ status: findingsStatus({ findingCount: 1 }) })
    expect(screen.getByText(/Codex recorded 1 finding from/)).toBeInTheDocument()
  })

  it('enables the correction control only while the findings still apply and nothing runs', () => {
    const onRequestCorrection = vi.fn()
    renderAction({ status: findingsStatus(), onRequestCorrection })
    fireEvent.click(correctButton()!)
    expect(onRequestCorrection).toHaveBeenCalledTimes(1)
  })

  it('disables the correction control, with the reason, when the findings no longer apply', () => {
    renderAction({ status: findingsStatus({ correctionApplicable: false }) })
    expect(correctButton()).toBeDisabled()
    expect(screen.getByText(/no longer apply exactly to the current source/)).toBeInTheDocument()
  })

  it('disables the correction control while a request is pending or the status is loading', () => {
    renderAction({ status: findingsStatus(), correctionRequesting: true })
    expect(screen.getByRole('button', { name: 'Requesting…' })).toBeDisabled()
  })

  it('shows a running correction as busy with no request control', () => {
    renderAction({
      status: findingsStatus({
        correctionAttemptId: 'correction-1',
        correctionAttemptNumber: 1,
        correctionStatus: 'Running',
        correctionApplicable: true,
      }),
    })
    expect(screen.getByText(/Diagnosis correction running…/)).toHaveAttribute('aria-busy', 'true')
    expect(correctButton()).not.toBeInTheDocument()
    expect(diagnoseButton()).not.toBeInTheDocument()
  })

  it('reports an applied correction and that verification must be run again before an ordinary review', () => {
    renderAction({
      status: findingsStatus({
        correctionApplicable: false,
        correctionAttemptId: 'correction-1',
        correctionAttemptNumber: 1,
        correctionStatus: 'Completed',
        correctionOutcome: 'CorrectionApplied',
        reviewableExecutionReportMessageId: 'report-2',
      }),
    })
    expect(screen.getByText('Last correction #1: Correction applied.')).toBeInTheDocument()
    expect(screen.getByText(/Run local verification again explicitly/)).toBeInTheDocument()
    expect(screen.getByText(/requires every enabled verification command to pass for the new checkpoint/)).toBeInTheDocument()
    expect(screen.getByText(/code review below now targets the corrected implementation report/)).toBeInTheDocument()
    expect(screen.queryByText(/no longer apply exactly/)).not.toBeInTheDocument()
  })

  it('reports a failed correction and still says verification must be run again explicitly', () => {
    renderAction({
      status: findingsStatus({
        correctionApplicable: false,
        correctionAttemptId: 'correction-1',
        correctionAttemptNumber: 2,
        correctionStatus: 'Failed',
        correctionOutcome: 'InvalidStructuredOutput',
      }),
    })
    expect(screen.getByText('Last correction #2: The Implementer returned an invalid correction response.')).toBeInTheDocument()
    expect(screen.getByText(/Run local verification again explicitly/)).toBeInTheDocument()
    expect(screen.queryByText(/code review below now targets/)).not.toBeInTheDocument()
  })

  it('shows the diagnosis escalation as needing a human decision without any authority or correction control', () => {
    renderAction({
      status: status({
        hasAttempt: true,
        attemptNumber: 1,
        status: 'Completed',
        outcome: 'DiagnosisEscalated',
        diagnosisEscalationMessageId: 'escalation-message-1',
      }),
    })
    expect(screen.getByText('Last diagnosis #1: Diagnosis escalated for a human decision.')).toBeInTheDocument()
    expect(screen.getByText(/needs a human decision/)).toBeInTheDocument()
    expect(screen.getByText(/grants no authority to change recipes, tools, permissions, or scope/)).toBeInTheDocument()
    expect(correctButton()).not.toBeInTheDocument()
  })

  it('shows the recorded human escalation at exhaustion and never an authorize control', () => {
    renderAction({
      status: findingsStatus({
        reviewCorrectionAttemptsUsed: 3,
        maximumReviewCorrectionAttempts: 3,
        correctionBudgetExhausted: true,
        correctionEscalationId: 'escalation-1',
        correctionEscalationMessageId: 'escalation-message-1',
      }),
    })
    expect(screen.getByText(/shared correction allowance is exhausted/)).toBeInTheDocument()
    expect(screen.getByText(/A human escalation was recorded for this run/)).toBeInTheDocument()
    expect(screen.getByText(/grants no authority to continue/)).toBeInTheDocument()
    expect(correctButton()).toBeDisabled()
    expect(screen.queryByRole('button', { name: /authoriz/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /escalation/i })).not.toBeInTheDocument()
  })

  it('offers the human-escalation record when the allowance is exhausted and none exists yet, keeping the correction disabled', () => {
    const onRequestCorrection = vi.fn()
    renderAction({
      status: findingsStatus({ reviewCorrectionAttemptsUsed: 3, maximumReviewCorrectionAttempts: 3, correctionBudgetExhausted: true }),
      onRequestCorrection,
    })
    expect(correctButton()).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Record human escalation' }))
    expect(onRequestCorrection).toHaveBeenCalledTimes(1)
    expect(screen.getByText(/A human escalation has not been recorded yet/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /authoriz/i })).not.toBeInTheDocument()
  })

  it('withholds the diagnosis request and explains a global claim block', () => {
    renderAction({
      status: status({ diagnosableExecutionReportMessageId: 'report-1' }),
      globalClaimBlock: { reason: 'CountBudgetExhausted' },
    })
    expect(screen.getByRole('status')).toBeInTheDocument()
    expect(diagnoseButton()).not.toBeInTheDocument()
  })

  it('withholds the diagnosis request on a blocking time fit, and withholds the correction on its own time fit', () => {
    const blocking = { reason: 'DoesNotFit' } as const
    const { unmount } = renderAction({ status: status({ diagnosableExecutionReportMessageId: 'report-1' }), timeFit: blocking })
    expect(diagnoseButton()).not.toBeInTheDocument()
    unmount()
    renderAction({ status: findingsStatus(), correctionTimeFit: blocking })
    expect(correctButton()).not.toBeInTheDocument()
  })

  it('shows the configured sandbox and rollout persistence only for a coherent attempt, and never an unknown value verbatim', () => {
    renderAction({
      status: findingsStatus({ configuredCommandSandbox: 'read-only', configuredRolloutPersistence: 'Disabled' }),
    })
    expect(screen.getByText(/Configured command sandbox: read-only · Configured rollout persistence: Disabled/)).toBeInTheDocument()
  })

  it('renders process and token evidence lines for an attempt', () => {
    const { container } = renderAction({ status: findingsStatus() })
    expect(container.querySelector('.dc-process-evidence')).not.toBeNull()
  })

  it('shows the ambiguous-persistence feedback from a request error and never claims success or failure', () => {
    renderAction({
      status: status({ diagnosableExecutionReportMessageId: 'report-1' }),
      requestError: 'The host could not confirm whether the request was recorded. Refresh the run before trying again.',
    })
    expect(screen.getByRole('status')).toHaveTextContent('could not confirm whether the request was recorded')
  })

  it('prefers the request error over the correction error over the status error', () => {
    renderAction({
      status: findingsStatus(),
      requestError: null,
      correctionError: 'correction failed safely',
      statusError: 'status failed safely',
    })
    expect(screen.getByText('correction failed safely')).toBeInTheDocument()
    expect(screen.queryByText('status failed safely')).not.toBeInTheDocument()
  })
})
