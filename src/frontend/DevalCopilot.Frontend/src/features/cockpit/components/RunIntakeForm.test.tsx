// @vitest-environment jsdom
import { act, fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createManualRunClient, startSimulatedRunClient } from '../../../api/clients'
import { ApiException, CreateManualRunRequest, CreateManualRunResponse } from '../../../api/generated/api-client'
import { RunIntakeForm } from './RunIntakeForm'

vi.mock('../../../api/clients', () => ({
  createManualRunClient: vi.fn(),
  startSimulatedRunClient: vi.fn(),
}))

function controllable<T = unknown>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

function problem(status: number, code: string, detail = 'raw server detail that must never be displayed') {
  return new ApiException('x', status, JSON.stringify({ errors: [{ code, detail }] }), {}, null)
}

const BLOCKED_REASON = 'A new objective can be recorded only after every run of this project has finished.'
const textbox = () => screen.getByRole('textbox', { name: 'Objective' }) as HTMLTextAreaElement
const submitButton = () => screen.getByRole('button', { name: 'Record manual run' })
const type = (value: string) => fireEvent.change(textbox(), { target: { value } })

let createManualRun: ReturnType<typeof vi.fn>
let startSimulatedRun: ReturnType<typeof vi.fn>

beforeEach(() => {
  vi.clearAllMocks()
  createManualRun = vi.fn()
  startSimulatedRun = vi.fn()
  vi.mocked(createManualRunClient).mockReturnValue({ createManualRun } as never)
  vi.mocked(startSimulatedRunClient).mockReturnValue({ startSimulatedRun } as never)
})

function ui(projectId: string, onChanged = vi.fn(), canCreateRun = true) {
  return <RunIntakeForm projectId={projectId} canCreateRun={canCreateRun} onChanged={onChanged} />
}

describe('RunIntakeForm availability', () => {
  it('offers the objective textbox, submit, and a separately labelled demo where a run can be created', () => {
    render(ui('project-A'))
    expect(textbox()).toBeInTheDocument()
    expect(submitButton()).toBeDisabled() // blank draft
    expect(screen.getByRole('button', { name: 'Start simulated run' })).toBeEnabled()
    expect(screen.getByText(/demo only/i)).toBeInTheDocument()
  })

  it('shows a fixed reason and no enabled submit or demo action when a run cannot be created', () => {
    render(ui('project-A', vi.fn(), false))
    expect(screen.getByText(BLOCKED_REASON)).toBeInTheDocument()
    expect(screen.queryByRole('textbox')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Record manual run' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Start simulated run' })).toBeNull()
  })
})

describe('RunIntakeForm objective boundary', () => {
  it('rejects a whitespace-only objective without sending a request', () => {
    render(ui('project-A'))
    type('   \n  ')
    expect(submitButton()).toBeDisabled()
    fireEvent.submit(screen.getByRole('form', { name: 'Record a manual run' }))
    expect(createManualRun).not.toHaveBeenCalled()
    expect(screen.getByRole('alert')).toHaveTextContent('Enter an objective')
  })

  it('accepts exactly 2000 characters and rejects 2001 without sending', () => {
    render(ui('project-A'))
    type('x'.repeat(2001))
    expect(submitButton()).toBeDisabled()
    expect(screen.getByText(/at most 2000 characters/)).toBeInTheDocument()
    fireEvent.submit(screen.getByRole('form', { name: 'Record a manual run' }))
    expect(createManualRun).not.toHaveBeenCalled()

    type('x'.repeat(2000))
    expect(submitButton()).toBeEnabled()
  })

  it('sends the typed objective exactly, without trimming it', async () => {
    createManualRun.mockResolvedValue(new CreateManualRunResponse({ runId: 'run-1', executionNumber: 1 }))
    render(ui('project-A'))
    type('  Plan the change  ')
    await act(async () => {
      fireEvent.click(submitButton())
    })
    const request = createManualRun.mock.calls[0][0] as CreateManualRunRequest
    expect(request.projectId).toBe('project-A')
    expect(request.objective).toBe('  Plan the change  ')
  })
})

describe('RunIntakeForm current success', () => {
  it('refreshes once, clears the unchanged draft, and reports the recorded run', async () => {
    const pending = controllable<CreateManualRunResponse>()
    createManualRun.mockReturnValue(pending.promise)
    const onChanged = vi.fn()
    render(ui('project-A', onChanged))
    type('Plan it')
    fireEvent.click(submitButton())
    expect(screen.getByRole('button', { name: 'Recording…' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Start simulated run' })).toBeDisabled()

    await act(async () => {
      pending.resolve(new CreateManualRunResponse({ runId: 'run-1', executionNumber: 3 }))
    })

    expect(onChanged).toHaveBeenCalledTimes(1)
    expect(textbox().value).toBe('')
    expect(screen.getByText(/manual run recorded \(execution 3\)/i)).toBeInTheDocument()
  })

  it('preserves an edit made while the request was in flight and never overwrites it', async () => {
    const pending = controllable<CreateManualRunResponse>()
    createManualRun.mockReturnValue(pending.promise)
    const onChanged = vi.fn()
    render(ui('project-A', onChanged))
    type('First')
    fireEvent.click(submitButton())
    type('First, then more')

    await act(async () => {
      pending.resolve(new CreateManualRunResponse({ runId: 'run-1', executionNumber: 1 }))
    })

    expect(textbox().value).toBe('First, then more')
    expect(onChanged).toHaveBeenCalledTimes(1)
  })

  it('keeps a draft that was edited and then edited back to the identical submitted text', async () => {
    const pending = controllable<CreateManualRunResponse>()
    createManualRun.mockReturnValue(pending.promise)
    const onChanged = vi.fn()
    render(ui('project-A', onChanged))
    type('Plan it')
    fireEvent.click(submitButton())
    type('Plan it again')
    type('Plan it')

    await act(async () => {
      pending.resolve(new CreateManualRunResponse({ runId: 'run-1', executionNumber: 1 }))
    })

    // The accepted request is real and refreshes the list, but the edited draft is never cleared.
    expect(textbox().value).toBe('Plan it')
    expect(onChanged).toHaveBeenCalledTimes(1)
    expect(createManualRun).toHaveBeenCalledTimes(1)
  })

  it('keeps an edited-back draft when the project was left and returned to before the completion', async () => {
    const pending = controllable<CreateManualRunResponse>()
    createManualRun.mockReturnValue(pending.promise)
    const onChanged = vi.fn()
    const { rerender } = render(ui('project-A', onChanged))
    type('Plan it')
    fireEvent.click(submitButton())
    rerender(ui('project-B', onChanged))
    rerender(ui('project-A', onChanged))
    type('Plan it again')
    type('Plan it')

    await act(async () => {
      pending.resolve(new CreateManualRunResponse({ runId: 'run-1', executionNumber: 1 }))
    })

    expect(textbox().value).toBe('Plan it')
    expect(onChanged).not.toHaveBeenCalled() // an ended lifetime neither refreshes nor clears
    expect(createManualRun).toHaveBeenCalledTimes(1)
  })

  it('keeps text edited away and back during the post-request refresh continuation', async () => {
    const pending = controllable<CreateManualRunResponse>()
    createManualRun.mockReturnValue(pending.promise)
    const onChanged = vi.fn(() => {
      type('First, edited')
      type('First')
    })
    render(ui('project-A', onChanged))
    type('First')
    fireEvent.click(submitButton())

    await act(async () => {
      pending.resolve(new CreateManualRunResponse({ runId: 'run-1', executionNumber: 1 }))
    })

    expect(textbox().value).toBe('First')
  })

  it('preserves an edit made during the post-request refresh continuation', async () => {
    const pending = controllable<CreateManualRunResponse>()
    createManualRun.mockReturnValue(pending.promise)
    const onChanged = vi.fn(() => type('Typed while refreshing'))
    render(ui('project-A', onChanged))
    type('First')
    fireEvent.click(submitButton())

    await act(async () => {
      pending.resolve(new CreateManualRunResponse({ runId: 'run-1', executionNumber: 1 }))
    })

    expect(textbox().value).toBe('Typed while refreshing')
  })

  it('starts the labelled demo with its own fixed objective, never the typed one', async () => {
    startSimulatedRun.mockResolvedValue({})
    const onChanged = vi.fn()
    render(ui('project-A', onChanged))
    type('My real objective')
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Start simulated run' }))
    })
    expect(createManualRun).not.toHaveBeenCalled()
    expect(startSimulatedRun.mock.calls[0][0].objective).toBe('Prove the walking skeleton')
    expect(onChanged).toHaveBeenCalledTimes(1)
    expect(textbox().value).toBe('My real objective')
  })
})

describe('RunIntakeForm duplicate prevention', () => {
  it('sends one request for two submissions in the same tick', async () => {
    const pending = controllable<CreateManualRunResponse>()
    createManualRun.mockReturnValue(pending.promise)
    render(ui('project-A'))
    type('Plan it')
    const form = screen.getByRole('form', { name: 'Record a manual run' })
    act(() => {
      fireEvent.submit(form)
      fireEvent.submit(form)
    })
    expect(createManualRun).toHaveBeenCalledTimes(1)
    await act(async () => {
      pending.resolve(new CreateManualRunResponse({ runId: 'run-1', executionNumber: 1 }))
    })
  })

  it('sends one request when a manual submission and the demo are triggered in the same tick', async () => {
    const pending = controllable<CreateManualRunResponse>()
    createManualRun.mockReturnValue(pending.promise)
    startSimulatedRun.mockReturnValue(pending.promise)
    render(ui('project-A'))
    type('Plan it')
    const demo = screen.getByRole('button', { name: 'Start simulated run' })
    act(() => {
      fireEvent.submit(screen.getByRole('form', { name: 'Record a manual run' }))
      demo.click()
    })
    expect(createManualRun.mock.calls.length + startSimulatedRun.mock.calls.length).toBe(1)
    await act(async () => {
      pending.resolve(new CreateManualRunResponse({ runId: 'run-1', executionNumber: 1 }))
    })
  })
})

describe('RunIntakeForm current refusals', () => {
  async function submitRefused(error: unknown, onChanged = vi.fn()) {
    createManualRun.mockRejectedValue(error)
    render(ui('project-A', onChanged))
    type('Plan it')
    await act(async () => {
      fireEvent.click(submitButton())
    })
    return onChanged
  }

  it('explains a blocked refusal with fixed copy, keeps the draft, and refreshes the availability hint', async () => {
    const onChanged = await submitRefused(problem(409, 'runs.intent_blocked'))
    expect(screen.getByRole('alert')).toHaveTextContent(/already has an unfinished run/i)
    expect(screen.queryByText(/raw server detail/)).toBeNull()
    expect(textbox().value).toBe('Plan it')
    expect(onChanged).toHaveBeenCalledTimes(1)
    expect(submitButton()).toBeEnabled()
  })

  it('reports a retryable concurrent-intent conflict without refreshing', async () => {
    const onChanged = await submitRefused(problem(409, 'runs.intent_conflict'))
    expect(screen.getByRole('alert')).toHaveTextContent(/try again/i)
    expect(onChanged).not.toHaveBeenCalled()
  })

  it('reports a server validation refusal with fixed copy', async () => {
    await submitRefused(problem(400, 'validation.failed'))
    expect(screen.getByRole('alert')).toHaveTextContent(/objective was not accepted/i)
    expect(screen.queryByText(/raw server detail/)).toBeNull()
  })

  it('reports a missing project', async () => {
    await submitRefused(problem(404, 'projects.not_found'))
    expect(screen.getByRole('alert')).toHaveTextContent('This project could not be found.')
  })

  it('never shows raw exception text for an unexpected failure', async () => {
    await submitRefused(new Error('boom: secret stack'))
    expect(screen.getByRole('alert')).toHaveTextContent('The run could not be created.')
    expect(screen.queryByText(/secret/)).toBeNull()
  })
})

describe('RunIntakeForm project switching and unmount', () => {
  it('does not leak A pending, draft, or error into B, and restores A cleanly on return', async () => {
    const pending = controllable<CreateManualRunResponse>()
    createManualRun.mockReturnValue(pending.promise)
    const onChanged = vi.fn()
    const { rerender } = render(ui('project-A', onChanged))
    type('A objective')
    fireEvent.click(submitButton())
    expect(screen.getByRole('button', { name: 'Recording…' })).toBeDisabled()

    rerender(ui('project-B', onChanged))
    expect(textbox().value).toBe('')
    expect(submitButton()).toBeDisabled() // blank draft, not busy
    expect(screen.queryByRole('button', { name: 'Recording…' })).toBeNull()
    type('B objective')
    expect(submitButton()).toBeEnabled()

    // A's accepted request completes while B is selected: no refresh, no state written to B.
    await act(async () => {
      pending.resolve(new CreateManualRunResponse({ runId: 'run-A', executionNumber: 1 }))
    })
    expect(onChanged).not.toHaveBeenCalled()
    expect(screen.queryByText(/manual run recorded/i)).toBeNull()
    expect(textbox().value).toBe('B objective')

    // Returning to A: its own draft is back, nothing pending, no success or error carried over.
    rerender(ui('project-A', onChanged))
    expect(textbox().value).toBe('A objective')
    expect(submitButton()).toBeEnabled()
    expect(screen.queryByText(/manual run recorded/i)).toBeNull()
    expect(screen.queryByRole('alert')).toBeNull()
    expect(createManualRun).toHaveBeenCalledTimes(1) // never auto-resubmitted
  })

  it('does not show a late failure of A in B or on return to A', async () => {
    const pending = controllable<CreateManualRunResponse>()
    createManualRun.mockReturnValue(pending.promise)
    const onChanged = vi.fn()
    const { rerender } = render(ui('project-A', onChanged))
    type('A objective')
    fireEvent.click(submitButton())
    rerender(ui('project-B', onChanged))

    await act(async () => {
      pending.reject(problem(409, 'runs.intent_blocked'))
    })
    expect(screen.queryByRole('alert')).toBeNull()
    expect(onChanged).not.toHaveBeenCalled() // a stale blocked refusal never refreshes

    rerender(ui('project-A', onChanged))
    expect(screen.queryByRole('alert')).toBeNull()
    expect(textbox().value).toBe('A objective')
  })

  it('does not treat a stale completion as a success of a new lifetime of the same project', async () => {
    const first = controllable<CreateManualRunResponse>()
    createManualRun.mockReturnValue(first.promise)
    const onChanged = vi.fn()
    const { rerender } = render(ui('project-A', onChanged))
    type('Plan it')
    fireEvent.click(submitButton())
    rerender(ui('project-B', onChanged))
    rerender(ui('project-A', onChanged))

    await act(async () => {
      first.resolve(new CreateManualRunResponse({ runId: 'run-A', executionNumber: 1 }))
    })

    expect(onChanged).not.toHaveBeenCalled()
    expect(textbox().value).toBe('Plan it')
    expect(screen.queryByText(/manual run recorded/i)).toBeNull()
  })

  it('ignores a completion that arrives after unmount', async () => {
    const pending = controllable<CreateManualRunResponse>()
    createManualRun.mockReturnValue(pending.promise)
    const onChanged = vi.fn()
    const { unmount } = render(ui('project-A', onChanged))
    type('Plan it')
    fireEvent.click(submitButton())
    unmount()

    await act(async () => {
      pending.resolve(new CreateManualRunResponse({ runId: 'run-A', executionNumber: 1 }))
    })
    expect(onChanged).not.toHaveBeenCalled()
  })
})
