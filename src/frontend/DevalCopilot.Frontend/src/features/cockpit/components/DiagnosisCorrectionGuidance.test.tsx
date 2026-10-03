// @vitest-environment jsdom
import { act, fireEvent, render, screen, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  ApiException,
  DirectHumanGuidanceResponse,
  VerificationDiagnosisMemberResponse,
  VerificationDiagnosisStatusResponse,
} from '../../../api/generated/api-client'
import { requestDiagnosisCorrectionClient } from '../../../api/clients'
import { useRequestDiagnosisCorrection } from '../hooks/useRequestDiagnosisCorrection'
import { VerificationDiagnosisAction } from './VerificationDiagnosisAction'

vi.mock('../../../api/clients', () => ({
  requestDiagnosisCorrectionClient: vi.fn(),
}))

/** Real hook and real action, so ownership by run, diagnosis source and committed lifetime is exercised end to end. */

function controllable() {
  let resolve!: (value?: unknown) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<unknown>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

const refresh = vi.fn()
const retained: ((runId: string, source: string, guidance?: string) => Promise<boolean>)[] = []
let operation: ReturnType<typeof vi.fn>

beforeEach(() => {
  vi.clearAllMocks()
  retained.length = 0
  operation = vi.fn()
  vi.mocked(requestDiagnosisCorrectionClient).mockImplementation(() => ({ requestDiagnosisCorrection: operation }) as never)
})

function problem(status: number, code: string) {
  return new ApiException('x', status, JSON.stringify({ errors: [{ code, detail: 'SERVER-WORDING with SECRET-TEXT' }] }), {}, null)
}

type StatusOverrides = Partial<ConstructorParameters<typeof VerificationDiagnosisStatusResponse>[0]>

const findings = (source: string, overrides: StatusOverrides = {}) =>
  new VerificationDiagnosisStatusResponse({
    hasAttempt: true,
    attemptId: source,
    attemptNumber: 1,
    executionReportMessageId: 'report-1',
    status: 'Completed',
    outcome: 'DiagnosisFindingsRecorded',
    findingCount: 2,
    correctionApplicable: true,
    artifacts: [],
    verification: [new VerificationDiagnosisMemberResponse({ position: 1, commandName: 'unit', executionNumber: 3, status: 'Failed', exitCode: 1 })],
    maximumReviewCorrectionAttempts: 3,
    reviewCorrectionAttemptsUsed: 0,
    ...overrides,
  })

const exhausted = (source: string, overrides: StatusOverrides = {}) =>
  findings(source, { reviewCorrectionAttemptsUsed: 3, correctionBudgetExhausted: true, ...overrides })

function Harness({
  runId,
  source,
  status,
  statusLoading = false,
}: {
  runId: string
  source: string
  status?: VerificationDiagnosisStatusResponse | null
  statusLoading?: boolean
}) {
  const hook = useRequestDiagnosisCorrection(runId, source, refresh)
  retained.push(hook.request)
  return (
    <VerificationDiagnosisAction
      runId={runId}
      status={status === undefined ? findings(source) : status}
      statusLoading={statusLoading}
      statusError={null}
      requesting={false}
      requestError={null}
      onRequest={vi.fn()}
      correctionRequesting={hook.requesting}
      correctionError={hook.error}
      onRequestCorrection={() => void hook.request(runId, source)}
      onRequestCorrectionWithGuidance={(guidance) => hook.request(runId, source, guidance)}
      globalClaimBlock={null}
      timeFit={{ reason: 'Fits' }}
      correctionTimeFit={{ reason: 'Fits' }}
    />
  )
}

const text = () => screen.getByLabelText('Direct guidance for this diagnosis correction') as HTMLTextAreaElement
const submit = () => screen.getByRole('button', { name: /Correct the diagnosed findings with guidance|Requesting with guidance…/ })
const plainButton = () => screen.queryByRole('button', { name: 'Correct the diagnosed findings with Claude' })
const type = (value: string) => fireEvent.change(text(), { target: { value } })

describe('diagnosis correction guidance editor', () => {
  it('sends the diagnosis and the text together, and the plain button still sends none', async () => {
    operation.mockResolvedValue({})
    render(<Harness runId="run-1" source="diagnosis-A" />)

    type('Fix only the failing check.')
    await act(async () => {
      fireEvent.click(plainButton()!)
    })
    await act(async () => {
      fireEvent.click(submit())
    })

    expect(operation.mock.calls[0][0]).toBe('run-1')
    expect(operation.mock.calls[0][1].toJSON()).toEqual({ verificationDiagnosisAttemptId: 'diagnosis-A' })
    expect(operation.mock.calls[1][1].toJSON()).toEqual({
      verificationDiagnosisAttemptId: 'diagnosis-A',
      guidance: 'Fix only the failing check.',
    })
    expect(text().value).toBe('')
    expect(refresh).toHaveBeenCalledTimes(2)
  })

  it('never sends blank, over-long or control-character guidance', () => {
    render(<Harness runId="run-1" source="diagnosis-A" />)

    expect(submit()).toBeDisabled()
    type('   \n ')
    expect(submit()).toBeDisabled()
    type('x'.repeat(601))
    expect(submit()).toBeDisabled()
    type('bell\u0007')
    expect(submit()).toBeDisabled()
    fireEvent.click(submit())

    expect(operation).not.toHaveBeenCalled()
  })

  it('keeps the draft and shows only fixed safe copy for a current refusal, never echoing the text', async () => {
    operation.mockRejectedValueOnce(problem(400, 'agent_attempts.direct_guidance_invalid'))
    render(<Harness runId="run-1" source="diagnosis-A" />)

    type('SECRET-TEXT')
    await act(async () => {
      fireEvent.click(submit())
    })

    expect(screen.getByRole('status')).toHaveTextContent(/The guidance was not accepted/)
    expect(screen.getByRole('status').textContent).not.toMatch(/SECRET-TEXT|SERVER-WORDING/)
    expect(text().value).toBe('SECRET-TEXT')
    expect(refresh).not.toHaveBeenCalled()
  })

  it('shows the fixed unavailable copy for a server refusal at exhaustion without echoing text', async () => {
    operation.mockRejectedValue(problem(409, 'agent_attempts.direct_guidance_unavailable'))
    render(<Harness runId="run-1" source="diagnosis-A" />)

    type('SECRET-TEXT')
    await act(async () => {
      fireEvent.click(submit())
    })

    expect(screen.getByRole('status')).toHaveTextContent(/only within the ordinary correction budget/)
    expect(screen.getByRole('status').textContent).not.toMatch(/SECRET-TEXT|SERVER-WORDING/)
    expect(text().value).toBe('SECRET-TEXT')
  })

  it('rejects a synchronous duplicate submission with one API call, and the first still owns its completion', async () => {
    const accepted = controllable()
    operation.mockReturnValue(accepted.promise)
    render(<Harness runId="run-1" source="diagnosis-A" />)

    type('same text')
    fireEvent.click(submit())
    fireEvent.click(submit())
    fireEvent.submit(screen.getByRole('form', { name: 'Correct the diagnosed findings with guidance' }))

    expect(operation).toHaveBeenCalledOnce()
    await act(async () => {
      accepted.resolve({})
    })
    expect(text().value).toBe('')
    expect(refresh).toHaveBeenCalledOnce()
  })

  it('keeps text typed after the success while the refresh is pending, and restores identical text without losing it', async () => {
    const accepted = controllable()
    operation.mockReturnValue(accepted.promise)
    render(<Harness runId="run-1" source="diagnosis-A" />)

    type('same text')
    fireEvent.click(submit())
    type('different')
    type('same text') // an edit that restores identical text is still a newer draft
    await act(async () => {
      accepted.resolve({})
    })

    expect(text().value).toBe('same text')
    expect(refresh).toHaveBeenCalledOnce()
  })

  it('same-run diagnosis replacement A to B to A: nothing carries over, the old accepted request is ignored but real', async () => {
    const accepted = controllable()
    operation.mockReturnValueOnce(accepted.promise).mockResolvedValue({})
    const view = render(<Harness runId="run-1" source="diagnosis-A" />)

    type('same text')
    fireEvent.click(submit())
    expect(screen.getByRole('button', { name: 'Requesting with guidance…' })).toBeDisabled()

    view.rerender(<Harness runId="run-1" source="diagnosis-B" />)
    expect(text().value).toBe('')
    expect(screen.queryByRole('button', { name: 'Requesting with guidance…' })).toBeNull()
    type('same text')
    view.rerender(<Harness runId="run-1" source="diagnosis-A" />)
    expect(text().value).toBe('')
    type('same text')

    await act(async () => {
      accepted.resolve({})
    })

    expect(text().value).toBe('same text')
    expect(refresh).not.toHaveBeenCalled()
    expect(operation).toHaveBeenCalledOnce()
    expect(submit()).toBeEnabled()

    await act(async () => {
      fireEvent.click(submit())
    })
    expect(operation).toHaveBeenCalledTimes(2)
    expect(refresh).toHaveBeenCalledOnce()
    expect(text().value).toBe('')
  })

  it('run switch and return: the old failure is not shown and the old handler is rejected', async () => {
    const failing = controllable()
    operation.mockReturnValueOnce(failing.promise).mockResolvedValue({})
    const view = render(<Harness runId="run-1" source="diagnosis-A" />)

    type('draft one')
    fireEvent.click(submit())
    const staleRequest = retained[0]

    view.rerender(<Harness runId="run-2" source="diagnosis-A" />)
    expect(text().value).toBe('')
    view.rerender(<Harness runId="run-1" source="diagnosis-A" />)
    expect(text().value).toBe('')
    type('draft two')

    await act(async () => {
      failing.reject(problem(409, 'agent_attempts.direct_guidance_unavailable'))
    })
    expect(screen.queryByRole('status')).toBeNull()
    expect(text().value).toBe('draft two')

    await act(async () => {
      expect(await staleRequest('run-1', 'diagnosis-A', 'late')).toBe(false)
    })
    expect(operation).toHaveBeenCalledOnce()
    expect(refresh).not.toHaveBeenCalled()
  })

  it('unmount and remount start clean; the old completion changes nothing', async () => {
    const accepted = controllable()
    operation.mockReturnValue(accepted.promise)
    const first = render(<Harness runId="run-1" source="diagnosis-A" />)

    type('same text')
    fireEvent.click(submit())
    first.unmount()
    render(<Harness runId="run-1" source="diagnosis-A" />)
    expect(text().value).toBe('')
    expect(submit()).toBeDisabled()
    type('same text')

    await act(async () => {
      accepted.resolve({})
    })

    expect(text().value).toBe('same text')
    expect(refresh).not.toHaveBeenCalled()
    expect(operation).toHaveBeenCalledOnce()
  })

  it('rejects a handler retained from a replaced diagnosis without a request', async () => {
    operation.mockResolvedValue({})
    const view = render(<Harness runId="run-1" source="diagnosis-A" />)
    const staleRequest = retained[0]
    view.rerender(<Harness runId="run-1" source="diagnosis-B" />)

    await act(async () => {
      expect(await staleRequest('run-1', 'diagnosis-A', 'late')).toBe(false)
    })

    expect(operation).not.toHaveBeenCalled()
  })

  it('does not let an overlapping guided request of the replacement be cleared or refreshed by the old one', async () => {
    const first = controllable()
    const second = controllable()
    operation.mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise)
    const view = render(<Harness runId="run-1" source="diagnosis-A" />)
    type('old text')
    fireEvent.click(submit())

    view.rerender(<Harness runId="run-1" source="diagnosis-B" />)
    type('new text')
    fireEvent.click(submit())
    expect(operation).toHaveBeenCalledTimes(2)

    await act(async () => {
      first.resolve({})
    })
    expect(text().value).toBe('new text')
    expect(refresh).not.toHaveBeenCalled()

    await act(async () => {
      second.resolve({})
    })
    expect(text().value).toBe('')
    expect(refresh).toHaveBeenCalledOnce()
  })
})

describe('diagnosis correction guidance availability', () => {
  it('offers the editor beside the plain button within the shared allowance, even after a previous failed correction', () => {
    render(<Harness runId="run-1" source="diagnosis-A" status={findings('diagnosis-A', { reviewCorrectionAttemptsUsed: 1 })} />)

    expect(text()).toBeTruthy()
    expect(plainButton()).toBeEnabled()
  })

  it('withholds the editor while the status is loading or has no applicable correction, running, or blocked', () => {
    const view = render(<Harness runId="run-1" source="diagnosis-A" statusLoading />)
    expect(submit()).toBeDisabled()
    expect(plainButton()).toBeDisabled()

    view.rerender(<Harness runId="run-1" source="diagnosis-A" status={findings('diagnosis-A', { correctionApplicable: false })} />)
    expect(screen.queryByLabelText('Direct guidance for this diagnosis correction')).toBeNull()

    view.rerender(<Harness runId="run-1" source="diagnosis-A" status={findings('diagnosis-A', { correctionStatus: 'Running', correctionAttemptId: 'c-1' })} />)
    expect(screen.queryByLabelText('Direct guidance for this diagnosis correction')).toBeNull()
  })

  it('withholds the editor at exhaustion, explains why, and keeps the unguided escalation action', async () => {
    operation.mockResolvedValue({})
    render(<Harness runId="run-1" source="diagnosis-A" status={exhausted('diagnosis-A')} />)

    expect(screen.queryByLabelText('Direct guidance for this diagnosis correction')).toBeNull()
    expect(screen.queryByRole('button', { name: /Correct the diagnosed findings with guidance/ })).toBeNull()
    expect(screen.getByText('Direct guidance is available only within the shared correction allowance.')).toBeTruthy()
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Record human escalation' }))
    })
    expect(operation).toHaveBeenCalledOnce()
    expect(operation.mock.calls[0][1].toJSON()).toEqual({ verificationDiagnosisAttemptId: 'diagnosis-A' })
  })

  it('withholds the editor for a diagnosis that is not a findings diagnosis', () => {
    render(<Harness runId="run-1" source="diagnosis-A" status={findings('diagnosis-A', { outcome: 'DiagnosisEscalated', findingCount: 0 })} />)

    expect(screen.queryByLabelText('Direct guidance for this diagnosis correction')).toBeNull()
  })
})

describe('diagnosis correction guidance fact', () => {
  const fact = (state: string, textValue?: string) => new DirectHumanGuidanceResponse({ state, text: textValue })
  const withCorrection = (correctionDirectGuidance: DirectHumanGuidanceResponse | undefined, source = 'diagnosis-A') =>
    findings(source, { correctionAttemptId: 'c-1', correctionAttemptNumber: 1, correctionStatus: 'Completed', correctionOutcome: 'CorrectionApplied', correctionDirectGuidance })

  it('shows the recorded text as supplied context, never as proof that the provider followed it', () => {
    render(<Harness runId="run-1" source="diagnosis-A" status={withCorrection(fact('Provided', 'Keep the fix small.\nReuse the helper.'))} />)

    const region = screen.getByRole('region', { name: 'Verification diagnosis' })
    expect(within(region).getByText(/Keep the fix small\./)).toBeInTheDocument()
    expect(region).toHaveTextContent('whether the provider followed it is not observed')
    expect(region.textContent).not.toMatch(/followed the guidance|complied/i)
  })

  it('states NotRecorded without implying that none was submitted, and Unknown without any text', () => {
    const view = render(<Harness runId="run-1" source="diagnosis-A" status={withCorrection(fact('NotRecorded'))} />)
    expect(screen.getByText('Direct human guidance for this attempt: none recorded.')).toBeInTheDocument()

    view.rerender(<Harness runId="run-1" source="diagnosis-A" status={withCorrection(fact('Unknown', 'SECRET-TEXT'))} />)
    expect(screen.getByText(/unknown \(the recorded facts disagree\); no text is shown/)).toBeInTheDocument()
    expect(screen.queryByText(/SECRET-TEXT/)).toBeNull()
  })

  it('shows no fact when no correction exists', () => {
    render(<Harness runId="run-1" source="diagnosis-A" status={findings('diagnosis-A')} />)

    expect(screen.queryByText(/Direct human guidance for this attempt/)).toBeNull()
    expect(screen.queryByText(/Direct human guidance supplied to this attempt/)).toBeNull()
  })

  it('keeps the diagnosis, the recorded escalation and the guidance fact visibly separate', () => {
    render(
      <Harness
        runId="run-1"
        source="diagnosis-A"
        status={withCorrection(fact('Provided', 'Keep the fix small.'), 'diagnosis-A')}
      />,
    )

    expect(screen.getByText(/Last correction #1: Correction applied\./)).toBeInTheDocument()
    expect(screen.getByText(/Direct human guidance supplied to this attempt/)).toBeInTheDocument()
    expect(screen.queryByText(/authoriz/i)).toBeNull()
  })
})
