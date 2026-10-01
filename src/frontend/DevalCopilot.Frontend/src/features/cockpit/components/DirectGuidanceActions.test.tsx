// @vitest-environment jsdom
import { act, fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiException, ReviewCorrectionAttemptStatusResponse } from '../../../api/generated/api-client'
import { requestImplementationClient, requestReviewCorrectionClient } from '../../../api/clients'
import { useRequestImplementation } from '../hooks/useRequestImplementation'
import { useRequestReviewCorrection } from '../hooks/useRequestReviewCorrection'
import { ImplementationAction } from './ImplementationAction'
import { ReviewCorrectionAction } from './ReviewCorrectionAction'

vi.mock('../../../api/clients', () => ({
  requestImplementationClient: vi.fn(),
  requestReviewCorrectionClient: vi.fn(),
}))

/** Real hooks and real actions, so ownership by run, source and committed lifetime is exercised end to end. */

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
})

function problem(status: number, code: string) {
  return new ApiException('x', status, JSON.stringify({ errors: [{ code, detail: 'SERVER-WORDING with SECRET-TEXT' }] }), {}, null)
}

function ImplementationHarness({ runId, source }: { runId: string; source: string }) {
  const hook = useRequestImplementation(runId, source, refresh)
  retained.push(hook.request)
  return (
    <ImplementationAction
      runId={runId}
      planProposalMessageId={source}
      status={null}
      statusLoading={false}
      statusError={null}
      requesting={hook.requesting}
      requestError={hook.error}
      onRequest={(guidance) => hook.request(runId, source, guidance)}
      globalClaimBlock={null}
      timeFit={{ reason: 'Fits' }}
    />
  )
}

function installImplementation() {
  vi.mocked(requestImplementationClient).mockReturnValue({ requestImplementation: operation } as never)
}

const impText = () => screen.getByLabelText('Direct guidance for this implementation request') as HTMLTextAreaElement
const impSubmit = () => screen.getByRole('button', { name: /Implement with guidance|Requesting with guidance…/ })
const type = (value: string) => fireEvent.change(impText(), { target: { value } })

describe('implementation guidance editor', () => {
  it('sends the plan and the text together, and the plain button still sends none', async () => {
    operation.mockResolvedValue({})
    installImplementation()
    render(<ImplementationHarness runId="run-1" source="plan-A" />)

    type('  Keep it small.  ')
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Implement the resolved plan with Claude' }))
    })
    expect(operation.mock.calls[0][1].toJSON()).toEqual({ planProposalMessageId: 'plan-A' })
    expect(impText().value).toBe('  Keep it small.  ')

    await act(async () => {
      fireEvent.click(impSubmit())
    })
    expect(operation.mock.calls[1][0]).toBe('run-1')
    expect(operation.mock.calls[1][1].toJSON()).toEqual({ planProposalMessageId: 'plan-A', guidance: '  Keep it small.  ' })
  })

  it('never sends blank, over-long or control-character guidance', () => {
    installImplementation()
    render(<ImplementationHarness runId="run-1" source="plan-A" />)

    for (const bad of ['', '   \n  ', 'x'.repeat(601), 'tab\there']) {
      type(bad)
      expect(impSubmit()).toBeDisabled()
      fireEvent.submit(impSubmit().closest('form')!)
    }
    expect(operation).not.toHaveBeenCalled()

    type('x'.repeat(600))
    expect(impSubmit()).toBeEnabled()
  })

  it('clears the draft after a current success that was not edited, and refreshes once', async () => {
    operation.mockResolvedValue({})
    installImplementation()
    render(<ImplementationHarness runId="run-1" source="plan-A" />)

    type('fresh')
    await act(async () => {
      fireEvent.click(impSubmit())
    })

    expect(impText().value).toBe('')
    expect(refresh).toHaveBeenCalledOnce()
  })

  it('keeps the draft and shows only fixed safe copy for a current refusal, never echoing the text', async () => {
    operation.mockRejectedValue(problem(400, 'agent_attempts.direct_guidance_invalid'))
    installImplementation()
    render(<ImplementationHarness runId="run-1" source="plan-A" />)

    type('SECRET-TEXT')
    await act(async () => {
      fireEvent.click(impSubmit())
    })

    expect(impText().value).toBe('SECRET-TEXT')
    expect(screen.getByRole('status')).toHaveTextContent(/non-blank text of at most 600 characters/)
    expect(screen.queryByText(/SERVER-WORDING/)).toBeNull()
    expect(refresh).not.toHaveBeenCalled()
    expect(impSubmit()).toBeEnabled()
  })

  it.each([
    ['different text', 'edited while waiting'],
    ['identical text', 'same text'],
  ])('keeps a draft edited during the API call (%s)', async (_name, finalText) => {
    const pending = controllable()
    operation.mockReturnValue(pending.promise)
    installImplementation()
    render(<ImplementationHarness runId="run-1" source="plan-A" />)

    type('same text')
    fireEvent.click(impSubmit())
    expect(operation).toHaveBeenCalledOnce()
    // Every edit bumps the version, including a round trip that ends on the submitted text.
    type('something else')
    type(finalText)
    await act(async () => {
      pending.resolve({})
    })

    expect(impText().value).toBe(finalText)
    expect(refresh).toHaveBeenCalledOnce()
  })

  it('keeps text typed after the success while the refresh is still pending', async () => {
    const pendingRefresh = controllable()
    refresh.mockReturnValue(pendingRefresh.promise)
    operation.mockResolvedValue({})
    installImplementation()
    render(<ImplementationHarness runId="run-1" source="plan-A" />)

    type('first')
    await act(async () => {
      fireEvent.click(impSubmit())
    })
    expect(impText().value).toBe('')
    type('second')
    await act(async () => {
      pendingRefresh.resolve()
    })

    expect(impText().value).toBe('second')
  })

  it('rejects a synchronous duplicate submission with one API call, and the first still owns its completion', async () => {
    const accepted = controllable()
    operation.mockReturnValue(accepted.promise)
    installImplementation()
    render(<ImplementationHarness runId="run-1" source="plan-A" />)

    type('once')
    const form = impSubmit().closest('form')!
    act(() => {
      fireEvent.submit(form)
      fireEvent.submit(form)
    })

    expect(operation).toHaveBeenCalledOnce()
    await act(async () => {
      accepted.resolve({})
    })

    // A rejected duplicate must not supersede the submission that was accepted.
    expect(impText().value).toBe('')
    expect(refresh).toHaveBeenCalledOnce()
  })

  it('same-run source replacement A to B to A: nothing carries over, the old accepted request is ignored but real', async () => {
    const accepted = controllable()
    operation.mockReturnValueOnce(accepted.promise).mockResolvedValue({})
    installImplementation()
    const view = render(<ImplementationHarness runId="run-1" source="plan-A" />)

    type('same text')
    fireEvent.click(impSubmit())
    expect(screen.getByRole('button', { name: 'Requesting…' })).toBeDisabled()

    view.rerender(<ImplementationHarness runId="run-1" source="plan-B" />)
    expect(impText().value).toBe('')
    expect(screen.queryByRole('button', { name: 'Requesting…' })).toBeNull()
    type('same text')
    view.rerender(<ImplementationHarness runId="run-1" source="plan-A" />)
    expect(impText().value).toBe('')
    type('same text')

    await act(async () => {
      accepted.resolve({})
    })

    expect(impText().value).toBe('same text')
    expect(refresh).not.toHaveBeenCalled()
    expect(screen.queryByRole('status')).toBeNull()
    expect(operation).toHaveBeenCalledOnce()
    expect(impSubmit()).toBeEnabled()

    // The returned-to identity is a new lifetime that can submit on its own terms.
    await act(async () => {
      fireEvent.click(impSubmit())
    })
    expect(operation).toHaveBeenCalledTimes(2)
    expect(refresh).toHaveBeenCalledOnce()
    expect(impText().value).toBe('')
  })

  it('run switch and return: the old failure is not shown and the old handler is rejected', async () => {
    const failing = controllable()
    operation.mockReturnValueOnce(failing.promise).mockResolvedValue({})
    installImplementation()
    const view = render(<ImplementationHarness runId="run-1" source="plan-A" />)

    type('draft one')
    fireEvent.click(impSubmit())
    const staleRequest = retained[0]

    view.rerender(<ImplementationHarness runId="run-2" source="plan-A" />)
    expect(impText().value).toBe('')
    view.rerender(<ImplementationHarness runId="run-1" source="plan-A" />)
    expect(impText().value).toBe('')
    type('draft two')

    await act(async () => {
      failing.reject(problem(409, 'agent_attempts.direct_guidance_unavailable'))
    })
    expect(screen.queryByRole('status')).toBeNull()
    expect(impText().value).toBe('draft two')

    await act(async () => {
      expect(await staleRequest('run-1', 'plan-A', 'late')).toBe(false)
    })
    expect(operation).toHaveBeenCalledOnce()
    expect(refresh).not.toHaveBeenCalled()
  })

  it('unmount and remount start clean; the old completion changes nothing', async () => {
    const accepted = controllable()
    operation.mockReturnValue(accepted.promise)
    installImplementation()
    const first = render(<ImplementationHarness runId="run-1" source="plan-A" />)

    type('same text')
    fireEvent.click(impSubmit())
    first.unmount()
    render(<ImplementationHarness runId="run-1" source="plan-A" />)
    expect(impText().value).toBe('')
    expect(impSubmit()).toBeDisabled()
    type('same text')

    await act(async () => {
      accepted.resolve({})
    })

    expect(impText().value).toBe('same text')
    expect(refresh).not.toHaveBeenCalled()
    expect(operation).toHaveBeenCalledOnce()
  })

  it('rejects a handler retained from a replaced source without a request', async () => {
    operation.mockResolvedValue({})
    installImplementation()
    const view = render(<ImplementationHarness runId="run-1" source="plan-A" />)
    const staleRequest = retained[0]
    view.rerender(<ImplementationHarness runId="run-1" source="plan-B" />)

    await act(async () => {
      expect(await staleRequest('run-1', 'plan-A', 'late')).toBe(false)
    })

    expect(operation).not.toHaveBeenCalled()
  })

  it('does not offer the editor while an implementation is running', () => {
    render(
      <ImplementationAction
        runId="run-1"
        planProposalMessageId="plan-A"
        status={{ planProposalMessageId: 'plan-A', status: 'Running', hasAttempt: true } as never}
        statusLoading={false}
        statusError={null}
        requesting={false}
        requestError={null}
        onRequest={vi.fn()}
        globalClaimBlock={null}
        timeFit={{ reason: 'Fits' }}
      />,
    )

    expect(screen.queryByLabelText('Direct guidance for this implementation request')).toBeNull()
  })
})

function CorrectionHarness({
  runId,
  source,
  status = null,
}: {
  runId: string
  source: string
  status?: ReviewCorrectionAttemptStatusResponse | null
}) {
  const hook = useRequestReviewCorrection(runId, source, refresh)
  retained.push(hook.request)
  return (
    <ReviewCorrectionAction
      runId={runId}
      reviewAttemptId={source}
      reviewOutcome="ReviewChangesRequested"
      status={status}
      statusLoading={false}
      statusError={null}
      requesting={hook.requesting}
      requestError={hook.error}
      onRequest={(guidance) => hook.request(runId, source, guidance)}
      onAuthorize={vi.fn()}
      onAuthorizeWithGuidance={vi.fn()}
      globalClaimBlock={null}
      timeFit={{ reason: 'Fits' }}
    />
  )
}

const exhausted = (overrides: Partial<ReviewCorrectionAttemptStatusResponse> = {}) =>
  new ReviewCorrectionAttemptStatusResponse({
    hasAttempt: true,
    attemptId: 'c-2',
    attemptNumber: 2,
    implementationReviewAttemptId: 'review-A',
    status: 'Failed',
    budgetExhausted: true,
    escalationId: 'escalation-1',
    hasAvailableHumanAuthorization: false,
    ...overrides,
  })

const corText = () => screen.getByLabelText('Direct guidance for this correction request') as HTMLTextAreaElement
const corSubmit = () => screen.getByRole('button', { name: /Request correction with guidance|Requesting with guidance…/ })

describe('review correction guidance editor', () => {
  beforeEach(() => {
    vi.mocked(requestReviewCorrectionClient).mockImplementation(() => ({ requestReviewCorrection: operation }) as never)
  })

  it('sends the review attempt and the text together, and the plain button sends none', async () => {
    operation.mockResolvedValue({})
    render(<CorrectionHarness runId="run-1" source="review-A" />)

    fireEvent.change(corText(), { target: { value: 'Fix only the failing check.' } })
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Request review correction' }))
    })
    await act(async () => {
      fireEvent.click(corSubmit())
    })

    expect(operation.mock.calls[0][1].toJSON()).toEqual({ implementationReviewAttemptId: 'review-A' })
    expect(operation.mock.calls[1][1].toJSON()).toEqual({
      implementationReviewAttemptId: 'review-A',
      guidance: 'Fix only the failing check.',
    })
    expect(corText().value).toBe('')
  })

  it('same-run review replacement A to B to A drops the draft and ignores the old completion', async () => {
    const accepted = controllable()
    operation.mockReturnValue(accepted.promise)
    const view = render(<CorrectionHarness runId="run-1" source="review-A" />)

    fireEvent.change(corText(), { target: { value: 'same text' } })
    fireEvent.click(corSubmit())
    view.rerender(<CorrectionHarness runId="run-1" source="review-B" />)
    expect(corText().value).toBe('')
    view.rerender(<CorrectionHarness runId="run-1" source="review-A" />)
    fireEvent.change(corText(), { target: { value: 'same text' } })

    await act(async () => {
      accepted.resolve({})
    })

    expect(corText().value).toBe('same text')
    expect(refresh).not.toHaveBeenCalled()
    expect(operation).toHaveBeenCalledOnce()
  })

  it('shows the fixed unavailable copy for a server refusal at exhaustion without echoing text', async () => {
    operation.mockRejectedValue(problem(409, 'agent_attempts.direct_guidance_unavailable'))
    render(<CorrectionHarness runId="run-1" source="review-A" />)

    fireEvent.change(corText(), { target: { value: 'SECRET-TEXT' } })
    await act(async () => {
      fireEvent.click(corSubmit())
    })

    expect(screen.getByRole('status')).toHaveTextContent(/only within the ordinary correction budget/)
    expect(screen.getByRole('status').textContent).not.toMatch(/SECRET-TEXT|SERVER-WORDING/)
    expect(corText().value).toBe('SECRET-TEXT')
  })

  it('offers the editor within the ordinary budget even with a previous failed attempt', () => {
    render(
      <CorrectionHarness
        runId="run-1"
        source="review-A"
        status={exhausted({ budgetExhausted: false, escalationId: undefined })}
      />,
    )

    expect(corText()).toBeTruthy()
    expect(screen.queryByText(/available only within the ordinary correction budget\./)).toBeNull()
  })

  it('withholds the editor at exhaustion and explains why, leaving the separate authorization UI unchanged', () => {
    render(<CorrectionHarness runId="run-1" source="review-A" status={exhausted()} />)

    expect(screen.queryByLabelText('Direct guidance for this correction request')).toBeNull()
    expect(screen.queryByRole('button', { name: /Request correction with guidance/ })).toBeNull()
    expect(screen.getByText('Direct guidance is available only within the ordinary correction budget.')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Authorize one additional correction' })).toBeEnabled()
    expect(screen.getByRole('form', { name: 'Authorize with guidance' })).toBeTruthy()
    expect(screen.getByLabelText('Optional guidance for the correction')).toBeTruthy()
  })

  it('withholds the editor, keeps the plain escalation button, and offers no guided request when an escalation must be created', () => {
    render(<CorrectionHarness runId="run-1" source="review-A" status={exhausted({ escalationId: undefined })} />)

    expect(screen.getByRole('button', { name: 'Create human escalation' })).toBeEnabled()
    expect(screen.queryByLabelText('Direct guidance for this correction request')).toBeNull()
  })

  it('withholds the editor while an extra authorization is available', () => {
    render(
      <CorrectionHarness
        runId="run-1"
        source="review-A"
        status={exhausted({ hasAvailableHumanAuthorization: true })}
      />,
    )

    expect(screen.queryByLabelText('Direct guidance for this correction request')).toBeNull()
    expect(screen.getByRole('button', { name: 'Request review correction' })).toBeEnabled()
  })
})
