// @vitest-environment jsdom
import { act, fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiException, PlanningImplementationAuthorizationResponse } from '../../../api/generated/api-client'
import { authorizePlanningImplementationClient, planningImplementationAuthorizationClient } from '../../../api/clients'
import { useAuthorizePlanningImplementation } from '../hooks/useAuthorizePlanningImplementation'
import { usePlanningImplementationAuthorization } from '../hooks/usePlanningImplementationAuthorization'
import { PlanningAuthorizationForm } from './PlanningAuthorizationForm'
import { PlanningImplementationAuthorizationPanel } from './PlanningImplementationAuthorizationPanel'

vi.mock('../../../api/clients', () => ({
  authorizePlanningImplementationClient: vi.fn(),
  planningImplementationAuthorizationClient: vi.fn(),
}))

/** Real hooks and the real panel, so ownership by run, escalation, final plan and mounted lifetime is exercised end to end. */

function controllable<T = unknown>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

function facts(state: string, escalation: string, finalId: string, extra: object = {}) {
  return new PlanningImplementationAuthorizationResponse({
    runId: 'run-1',
    escalationMessageId: escalation,
    state,
    finalProposalMessageId: finalId,
    orderedDecisionMessageIds: ['decision-1'],
    ...extra,
  })
}

function problem(status: number, code: string) {
  return new ApiException('x', status, JSON.stringify({ errors: [{ code, detail: 'SERVER-WORDING with SECRET-TEXT' }] }), {}, null)
}

let read: ReturnType<typeof vi.fn>
let authorize: ReturnType<typeof vi.fn>

beforeEach(() => {
  vi.clearAllMocks()
  read = vi.fn()
  authorize = vi.fn()
  vi.mocked(planningImplementationAuthorizationClient).mockReturnValue({ getPlanningImplementationAuthorization: read } as never)
  vi.mocked(authorizePlanningImplementationClient).mockReturnValue({ authorizePlanningImplementation: authorize } as never)
})

function Harness({ runId, escalation, finalId }: { runId: string; escalation: string; finalId: string }) {
  const authorization = usePlanningImplementationAuthorization(runId, escalation, 1)
  const authorizing = useAuthorizePlanningImplementation(runId, escalation, authorization.refresh)
  return (
    <PlanningImplementationAuthorizationPanel
      runId={runId}
      escalationMessageId={escalation}
      timelineFinalProposalMessageId={finalId}
      authorization={authorization.authorization}
      loading={authorization.loading}
      readError={authorization.error}
      authorizing={authorizing.authorizing}
      authorizeError={authorizing.error}
      onAuthorize={(rationale) => authorizing.authorize(runId, escalation, rationale)}
    />
  )
}

const text = () => screen.getByLabelText(/Your reason for authorizing this final plan/) as HTMLTextAreaElement
const submit = () => screen.getByRole('button', { name: /Authorize one implementation claim|Authorizing…/ })
const type = (value: string) => fireEvent.change(text(), { target: { value } })

async function settle() {
  await act(async () => {
    await Promise.resolve()
  })
}

function readsAbsent(escalation = 'esc-A', finalId = 'plan-A') {
  read.mockImplementation(() => Promise.resolve(facts('Absent', escalation, finalId)))
}

describe('human authorization of the final plan: states and truthfulness', () => {
  it('reads the server facts first and shows no form while they are unread', async () => {
    const pending = controllable<PlanningImplementationAuthorizationResponse>()
    read.mockReturnValue(pending.promise)

    render(<Harness runId="run-1" escalation="esc-A" finalId="plan-A" />)

    expect(screen.getByText('Reading the authorization status…')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Authorize one implementation claim/ })).toBeNull()
    await act(async () => pending.resolve(facts('Absent', 'esc-A', 'plan-A')))
    expect(submit()).toBeEnabled()
  })

  it('sends the raw rationale for the exact escalation, clears the draft, and shows Available only after the server read says so', async () => {
    read.mockResolvedValueOnce(facts('Absent', 'esc-A', 'plan-A'))
    const refresh = controllable<PlanningImplementationAuthorizationResponse>()
    read.mockReturnValueOnce(refresh.promise)
    authorize.mockResolvedValue({})
    render(<Harness runId="run-1" escalation="esc-A" finalId="plan-A" />)
    await settle()

    type('  I accept the plan.  ')
    await act(async () => {
      fireEvent.click(submit())
    })

    expect(authorize).toHaveBeenCalledOnce()
    expect(authorize.mock.calls[0][0]).toBe('run-1')
    expect(authorize.mock.calls[0][1]).toBe('esc-A')
    expect(authorize.mock.calls[0][2].toJSON()).toEqual({ rationale: '  I accept the plan.  ' })
    // A successful click is not a state: the refresh is pending, so nothing says Available yet.
    expect(read).toHaveBeenCalledTimes(2)
    expect(screen.queryByText(/Authorized by a human/)).toBeNull()
    expect(text().value).toBe('')

    await act(async () => refresh.resolve(facts('Available', 'esc-A', 'plan-A', { rationale: 'I accept the plan.' })))
    expect(screen.getByText(/Authorized by a human/)).toBeInTheDocument()
    expect(screen.getByText(/Recorded reason: I accept the plan\./)).toBeInTheDocument()
  })

  it('shows a failed refresh honestly after an accepted request and infers no state from it', async () => {
    read.mockResolvedValueOnce(facts('Absent', 'esc-A', 'plan-A'))
    read.mockRejectedValueOnce(new Error('network down with SECRET-TEXT'))
    authorize.mockResolvedValue({})
    render(<Harness runId="run-1" escalation="esc-A" finalId="plan-A" />)
    await settle()

    type('reason')
    await act(async () => {
      fireEvent.click(submit())
    })
    await settle()

    expect(screen.getByText(/could not be read, so no decision is shown/)).toBeInTheDocument()
    expect(screen.queryByText(/Authorized by a human/)).toBeNull()
    expect(screen.queryByRole('button', { name: /Authorize one implementation claim/ })).toBeNull()
    expect(screen.queryByText(/SECRET-TEXT/)).toBeNull()
  })

  it.each([
    ['planning_authorizations.rationale_invalid', /must be non-blank text of at most 600 characters/],
    ['planning_authorizations.rationale_conflict', /different reason already exists/],
    ['planning_authorizations.already_consumed', /already used by an implementation claim/],
    ['planning_authorizations.source_stale', /no longer belongs to the run/],
  ])('keeps the draft and shows only fixed copy for %s, never the text or the server wording', async (code, expected) => {
    readsAbsent()
    authorize.mockRejectedValue(problem(409, code))
    render(<Harness runId="run-1" escalation="esc-A" finalId="plan-A" />)
    await settle()

    type('SECRET-TEXT')
    await act(async () => {
      fireEvent.click(submit())
    })

    expect(text().value).toBe('SECRET-TEXT')
    expect(screen.getByText(expected)).toBeInTheDocument()
    expect(screen.queryByText(/SERVER-WORDING/)).toBeNull()
    expect(screen.queryByText(/SECRET-TEXT/, { selector: 'p' })).toBeNull()
    expect(read).toHaveBeenCalledTimes(1)
    expect(submit()).toBeEnabled()
  })

  it('shows generic fixed copy for an unknown failure', async () => {
    readsAbsent()
    authorize.mockRejectedValue(new Error('boom with SECRET-TEXT'))
    render(<Harness runId="run-1" escalation="esc-A" finalId="plan-A" />)
    await settle()

    type('x')
    await act(async () => {
      fireEvent.click(submit())
    })

    expect(screen.getByText(/could not be recorded for this escalation/)).toBeInTheDocument()
    expect(screen.queryByText(/SECRET-TEXT/)).toBeNull()
  })

  it('never sends a blank, over-long, or control-character reason', async () => {
    readsAbsent()
    render(<Harness runId="run-1" escalation="esc-A" finalId="plan-A" />)
    await settle()

    for (const bad of ['', '   \n  ', 'x'.repeat(601), 'tab\there']) {
      type(bad)
      fireEvent.submit(submit().closest('form')!)
    }
    expect(authorize).not.toHaveBeenCalled()

    type('x'.repeat(600))
    authorize.mockResolvedValue({})
    await act(async () => {
      fireEvent.click(submit())
    })
    expect(authorize).toHaveBeenCalledOnce()
  })
})

describe('human authorization of the final plan: ownership of drafts and continuations', () => {
  it('ignores a synchronous duplicate submission while one is in flight', async () => {
    readsAbsent()
    const pending = controllable()
    authorize.mockReturnValue(pending.promise)
    render(<Harness runId="run-1" escalation="esc-A" finalId="plan-A" />)
    await settle()

    type('once')
    const form = submit().closest('form')!
    act(() => {
      fireEvent.submit(form)
      fireEvent.submit(form)
    })

    expect(authorize).toHaveBeenCalledOnce()
    await act(async () => pending.resolve({}))
  })

  it.each([
    ['different text', 'edited while waiting'],
    ['identical text', 'same text'],
  ])('keeps a draft edited during the API call (%s)', async (_name, finalText) => {
    readsAbsent()
    const pending = controllable()
    authorize.mockReturnValue(pending.promise)
    render(<Harness runId="run-1" escalation="esc-A" finalId="plan-A" />)
    await settle()

    type('same text')
    await act(async () => {
      fireEvent.click(submit())
    })
    // An edit that yields identical text still counts: React skips an unchanged value, so route through an intermediate edit.
    type('intermediate edit')
    type(finalText)
    await act(async () => pending.resolve({}))

    expect(text().value).toBe(finalText)
  })

  it('keeps a draft typed while the accepted request is being refreshed', async () => {
    read.mockResolvedValueOnce(facts('Absent', 'esc-A', 'plan-A'))
    const refresh = controllable<PlanningImplementationAuthorizationResponse>()
    read.mockReturnValueOnce(refresh.promise)
    authorize.mockResolvedValue({})
    render(<Harness runId="run-1" escalation="esc-A" finalId="plan-A" />)
    await settle()

    type('first')
    await act(async () => {
      fireEvent.click(submit())
    })
    // The refresh is still pending and the facts still say Absent, so the form remains: a new edit is a new draft.
    type('typed during the refresh')
    await act(async () => refresh.resolve(facts('Absent', 'esc-A', 'plan-A')))

    expect(text().value).toBe('typed during the refresh')
  })

  it('resets the draft for another escalation and never lets a late completion of the old one touch the new interaction', async () => {
    read.mockImplementation((_run: string, escalation: string) =>
      Promise.resolve(facts('Absent', escalation, escalation === 'esc-A' ? 'plan-A' : 'plan-B')))
    const lateA = controllable()
    authorize.mockReturnValueOnce(lateA.promise)
    const view = render(<Harness runId="run-1" escalation="esc-A" finalId="plan-A" />)
    await settle()

    type('draft for A')
    await act(async () => {
      fireEvent.click(submit())
    })
    view.rerender(<Harness runId="run-1" escalation="esc-B" finalId="plan-B" />)
    await settle()
    expect(text().value).toBe('')
    type('draft for B')
    const readsBefore = read.mock.calls.length

    await act(async () => lateA.resolve({}))
    await settle()

    // The accepted request for A stays a real server operation (it was made once), but it neither cleared B's draft nor
    // refreshed or marked B busy.
    expect(authorize).toHaveBeenCalledOnce()
    expect(text().value).toBe('draft for B')
    expect(read.mock.calls.length).toBe(readsBefore)
    expect(submit()).toBeEnabled()
  })

  it('treats a return to an earlier escalation as a new interaction (A→B→A)', async () => {
    read.mockImplementation((_run: string, escalation: string) =>
      Promise.resolve(facts('Absent', escalation, escalation === 'esc-A' ? 'plan-A' : 'plan-B')))
    const lateA = controllable()
    authorize.mockReturnValueOnce(lateA.promise)
    const view = render(<Harness runId="run-1" escalation="esc-A" finalId="plan-A" />)
    await settle()
    type('first A draft')
    await act(async () => {
      fireEvent.click(submit())
    })

    view.rerender(<Harness runId="run-1" escalation="esc-B" finalId="plan-B" />)
    await settle()
    view.rerender(<Harness runId="run-1" escalation="esc-A" finalId="plan-A" />)
    await settle()
    expect(text().value).toBe('')
    type('second A draft')
    const readsBefore = read.mock.calls.length

    await act(async () => lateA.resolve({}))
    await settle()

    expect(text().value).toBe('second A draft')
    expect(read.mock.calls.length).toBe(readsBefore)
    expect(submit()).toBeEnabled()
  })

  it('does not let a late rejection of an ended interaction set an error on the current one', async () => {
    read.mockImplementation((_run: string, escalation: string) =>
      Promise.resolve(facts('Absent', escalation, escalation === 'esc-A' ? 'plan-A' : 'plan-B')))
    const lateA = controllable()
    authorize.mockReturnValueOnce(lateA.promise)
    const view = render(<Harness runId="run-1" escalation="esc-A" finalId="plan-A" />)
    await settle()
    type('A')
    await act(async () => {
      fireEvent.click(submit())
    })
    view.rerender(<Harness runId="run-1" escalation="esc-B" finalId="plan-B" />)
    await settle()

    await act(async () => lateA.reject(problem(409, 'planning_authorizations.rationale_conflict')))
    await settle()

    expect(screen.queryByText(/different reason already exists/)).toBeNull()
    expect(submit()).toBeEnabled()
  })

  it('resets the draft when only the final plan under the same escalation changes', async () => {
    read.mockImplementation((_run: string, escalation: string) => Promise.resolve(facts('Absent', escalation, 'plan-A')))
    const view = render(<Harness runId="run-1" escalation="esc-A" finalId="plan-A" />)
    await settle()
    type('draft')

    view.rerender(<Harness runId="run-1" escalation="esc-A" finalId="plan-A2" />)
    await settle()

    // The server names plan-A while the timeline now shows plan-A2: nothing is offered for the mismatch.
    expect(screen.queryByLabelText(/Your reason for authorizing this final plan/)).toBeNull()
    expect(screen.getByText(/names a different final plan/)).toBeInTheDocument()
  })

  it('is isolated per run: a draft never carries to another run', async () => {
    read.mockImplementation((run: string, escalation: string) => Promise.resolve(facts('Absent', escalation, 'plan-A', { runId: run })))
    const view = render(<Harness runId="run-1" escalation="esc-A" finalId="plan-A" />)
    await settle()
    type('draft for run 1')

    view.rerender(<Harness runId="run-2" escalation="esc-A" finalId="plan-A" />)
    await settle()

    expect(text().value).toBe('')
  })

  it('does not touch state, refresh, or throw when the component unmounts before the request completes', async () => {
    readsAbsent()
    const pending = controllable()
    authorize.mockReturnValue(pending.promise)
    const errors = vi.spyOn(console, 'error').mockImplementation(() => {})
    const view = render(<Harness runId="run-1" escalation="esc-A" finalId="plan-A" />)
    await settle()
    type('draft')
    await act(async () => {
      fireEvent.click(submit())
    })
    const readsBefore = read.mock.calls.length

    view.unmount()
    await act(async () => pending.resolve({}))
    await settle()

    expect(authorize).toHaveBeenCalledOnce()
    expect(read.mock.calls.length).toBe(readsBefore)
    expect(errors).not.toHaveBeenCalled()
    errors.mockRestore()
  })

  it('ignores a stale read of an earlier escalation after a switch', async () => {
    const lateA = controllable<PlanningImplementationAuthorizationResponse>()
    read.mockImplementation((_run: string, escalation: string) =>
      escalation === 'esc-A' ? lateA.promise : Promise.resolve(facts('Stale', 'esc-B', 'plan-B')))
    const view = render(<Harness runId="run-1" escalation="esc-A" finalId="plan-A" />)
    view.rerender(<Harness runId="run-1" escalation="esc-B" finalId="plan-B" />)
    await settle()

    await act(async () => lateA.resolve(facts('Available', 'esc-A', 'plan-A')))

    expect(screen.getByLabelText('Human decision on the final plan')).toHaveTextContent(/no longer matches the run.s current plan/)
    expect(screen.queryByText(/Authorized by a human/)).toBeNull()
  })
})

describe('the decision form on its own: identity resets without an unmount', () => {
  function FormOnly({ runId, escalation, finalId, onSubmit }: { runId: string; escalation: string; finalId: string; onSubmit: (rationale: string) => Promise<boolean> }) {
    return (
      <PlanningAuthorizationForm
        runId={runId}
        escalationMessageId={escalation}
        finalProposalMessageId={finalId}
        authorizing={false}
        statusLoading={false}
        onSubmit={onSubmit}
      />
    )
  }

  it.each([
    ['run', { runId: 'run-2', escalation: 'esc-A', finalId: 'plan-A' }],
    ['escalation', { runId: 'run-1', escalation: 'esc-B', finalId: 'plan-A' }],
    ['final plan', { runId: 'run-1', escalation: 'esc-A', finalId: 'plan-B' }],
  ])('discards the draft when only the %s changes, in the same render, and a return is a new interaction', (_name, next) => {
    const onSubmit = vi.fn().mockResolvedValue(true)
    const view = render(<FormOnly runId="run-1" escalation="esc-A" finalId="plan-A" onSubmit={onSubmit} />)
    type('draft for A')

    view.rerender(<FormOnly {...next} onSubmit={onSubmit} />)
    expect(text().value).toBe('')

    view.rerender(<FormOnly runId="run-1" escalation="esc-A" finalId="plan-A" onSubmit={onSubmit} />)
    expect(text().value).toBe('')
  })

  it('does not let a completion that began under an earlier identity clear a newer draft', async () => {
    const lateA = controllable<boolean>()
    const onSubmit = vi.fn().mockReturnValueOnce(lateA.promise)
    const view = render(<FormOnly runId="run-1" escalation="esc-A" finalId="plan-A" onSubmit={onSubmit} />)
    type('A draft')
    await act(async () => {
      fireEvent.click(submit())
    })

    view.rerender(<FormOnly runId="run-1" escalation="esc-B" finalId="plan-A" onSubmit={onSubmit} />)
    type('B draft')
    await act(async () => lateA.resolve(true))

    expect(text().value).toBe('B draft')
  })
})
