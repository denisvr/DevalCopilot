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

function problem(status: number, code: string) {
  return new ApiException('x', status, JSON.stringify({ errors: [{ code, detail: 'raw detail' }] }), {}, null)
}

const textbox = () => screen.getByRole('textbox', { name: 'Objective' }) as HTMLTextAreaElement
const submitButton = () => screen.getByRole('button', { name: 'Record manual run' })
const claimsInput = () => screen.getByRole('spinbutton', { name: 'Agent claim ceiling' }) as HTMLInputElement
const minutesInput = () => screen.getByRole('spinbutton', { name: 'Reserved invocation minutes' }) as HTMLInputElement
const type = (value: string) => fireEvent.change(textbox(), { target: { value } })
const setClaims = (value: string) => fireEvent.change(claimsInput(), { target: { value } })
const setMinutes = (value: string) => fireEvent.change(minutesInput(), { target: { value } })
const recorded = () => new CreateManualRunResponse({ runId: 'run-1', executionNumber: 1 })

let createManualRun: ReturnType<typeof vi.fn>
let startSimulatedRun: ReturnType<typeof vi.fn>

beforeEach(() => {
  vi.clearAllMocks()
  createManualRun = vi.fn()
  startSimulatedRun = vi.fn()
  vi.mocked(createManualRunClient).mockReturnValue({ createManualRun } as never)
  vi.mocked(startSimulatedRunClient).mockReturnValue({ startSimulatedRun } as never)
})

function ui(projectId: string, onChanged = vi.fn()) {
  return <RunIntakeForm projectId={projectId} canCreateRun={true} onChanged={onChanged} />
}

describe('RunIntakeForm immutable budget drafts', () => {
  it('offers the two numeric drafts initially at 16 claims and 120 minutes with the honest explanation', () => {
    render(ui('project-A'))
    expect(claimsInput().value).toBe('16')
    expect(minutesInput().value).toBe('120')
    const note = screen.getByTestId('run-intake-budget-note')
    expect(note).toHaveTextContent(/claims stay consumed after a failure or interruption/i)
    expect(note).toHaveTextContent(/reserved from each attempt.s configured timeout, not measured elapsed time/i)
    expect(note).toHaveTextContent(/cannot be changed after the run is recorded/i)
    expect(note).toHaveTextContent(/smaller than a role.s configured timeout prevents that claim/i)
    expect(screen.getByText(/the objective and budget fields above are ignored/i)).toBeInTheDocument()
  })

  it('sends the default budgets as explicit numbers when they are left alone', async () => {
    createManualRun.mockResolvedValue(recorded())
    render(ui('project-A'))
    type('Plan it')
    await act(async () => {
      fireEvent.click(submitButton())
    })
    const request = createManualRun.mock.calls[0][0] as CreateManualRunRequest
    expect(request.maximumAgentAttempts).toBe(16)
    expect(request.maximumAgentInvocationMinutes).toBe(120)
  })

  it('sends the chosen smaller budgets as numbers, never as text', async () => {
    createManualRun.mockResolvedValue(recorded())
    render(ui('project-A'))
    type('Plan it')
    setClaims('4')
    setMinutes('40')
    await act(async () => {
      fireEvent.click(submitButton())
    })
    const request = createManualRun.mock.calls[0][0] as CreateManualRunRequest
    expect(request.maximumAgentAttempts).toBe(4)
    expect(request.maximumAgentInvocationMinutes).toBe(40)
  })

  it.each([
    ['1', '1'],
    ['16', '120'],
  ])('accepts the inclusive boundaries %s claims and %s minutes', (claims, minutes) => {
    render(ui('project-A'))
    type('Plan it')
    setClaims(claims)
    setMinutes(minutes)
    expect(submitButton()).toBeEnabled()
  })

  it.each([
    ['claims', '', /enter a whole number of claims/i],
    ['claims', '0', /from 1 through 16/i],
    ['claims', '17', /from 1 through 16/i],
    ['claims', '-1', /from 1 through 16/i],
    ['claims', '3.5', /from 1 through 16/i],
    ['claims', '1e1', /from 1 through 16/i],
    ['claims', '99999999999999999999', /from 1 through 16/i],
    ['minutes', '', /enter a whole number of minutes/i],
    ['minutes', '0', /from 1 through 120/i],
    ['minutes', '121', /from 1 through 120/i],
    ['minutes', '-5', /from 1 through 120/i],
    ['minutes', '1.5', /from 1 through 120/i],
    ['minutes', '2.0', /from 1 through 120/i],
  ])('refuses an invalid %s draft %j without sending, rounding or clamping', (field, value, message) => {
    render(ui('project-A'))
    type('Plan it')
    if (field === 'claims') {
      setClaims(value)
    } else {
      setMinutes(value)
    }
    expect(submitButton()).toBeDisabled()
    expect(screen.getByTestId(`run-intake-${field}-validation`)).toHaveTextContent(message)
    fireEvent.submit(screen.getByRole('form', { name: 'Record a manual run' }))
    expect(createManualRun).not.toHaveBeenCalled()
    expect(screen.getByRole('alert')).toHaveTextContent(message)
  })

  it('resets every draft to its initial value only after an unchanged submission is accepted', async () => {
    const pending = controllable<CreateManualRunResponse>()
    createManualRun.mockReturnValue(pending.promise)
    render(ui('project-A'))
    type('Plan it')
    setClaims('4')
    setMinutes('40')
    fireEvent.click(submitButton())

    await act(async () => {
      pending.resolve(recorded())
    })

    expect(textbox().value).toBe('')
    expect(claimsInput().value).toBe('16')
    expect(minutesInput().value).toBe('120')
  })

  it.each([
    ['claims', () => setClaims('8')],
    ['minutes', () => setMinutes('60')],
  ])('keeps the whole snapshot when only the %s draft changes while the request is in flight', async (name, edit) => {
    const pending = controllable<CreateManualRunResponse>()
    createManualRun.mockReturnValue(pending.promise)
    const onChanged = vi.fn()
    render(ui('project-A', onChanged))
    type('Plan it')
    setClaims('4')
    setMinutes('40')
    fireEvent.click(submitButton())
    edit()

    await act(async () => {
      pending.resolve(recorded())
    })

    expect(textbox().value).toBe('Plan it')
    expect(claimsInput().value).toBe(name === 'claims' ? '8' : '4')
    expect(minutesInput().value).toBe(name === 'minutes' ? '60' : '40')
    expect(onChanged).toHaveBeenCalledTimes(1)
  })

  it.each([
    ['claims', () => setClaims('5'), () => setClaims('4')],
    ['minutes', () => setMinutes('50'), () => setMinutes('40')],
  ])('keeps the snapshot when the %s draft is edited away and back to the identical submitted value', async (_name, away, back) => {
    const pending = controllable<CreateManualRunResponse>()
    createManualRun.mockReturnValue(pending.promise)
    render(ui('project-A'))
    type('Plan it')
    setClaims('4')
    setMinutes('40')
    fireEvent.click(submitButton())
    away()
    back()

    await act(async () => {
      pending.resolve(recorded())
    })

    expect(textbox().value).toBe('Plan it')
    expect(claimsInput().value).toBe('4')
    expect(minutesInput().value).toBe('40')
    expect(createManualRun).toHaveBeenCalledTimes(1)
  })

  it('keeps each project its own budgets, and an old completion never resets them on A to B to A', async () => {
    const pending = controllable<CreateManualRunResponse>()
    createManualRun.mockReturnValue(pending.promise)
    const onChanged = vi.fn()
    const { rerender } = render(ui('project-A', onChanged))
    type('A objective')
    setClaims('4')
    setMinutes('40')
    fireEvent.click(submitButton())

    rerender(ui('project-B', onChanged))
    expect(claimsInput().value).toBe('16')
    expect(minutesInput().value).toBe('120')
    setClaims('2')
    rerender(ui('project-A', onChanged))
    expect(textbox().value).toBe('A objective')
    expect(claimsInput().value).toBe('4')
    expect(minutesInput().value).toBe('40')

    await act(async () => {
      pending.resolve(recorded())
    })

    expect(onChanged).not.toHaveBeenCalled()
    expect(claimsInput().value).toBe('4')
    expect(minutesInput().value).toBe('40')
    rerender(ui('project-B', onChanged))
    expect(claimsInput().value).toBe('2')
  })

  it('ignores a completion that arrives after unmount without touching anything', async () => {
    const pending = controllable<CreateManualRunResponse>()
    createManualRun.mockReturnValue(pending.promise)
    const onChanged = vi.fn()
    const { unmount } = render(ui('project-A', onChanged))
    type('Plan it')
    setClaims('3')
    fireEvent.click(submitButton())
    unmount()

    await act(async () => {
      pending.resolve(recorded())
    })

    expect(onChanged).not.toHaveBeenCalled()
  })

  it('starts the labelled demo without the manual drafts and leaves them untouched', async () => {
    startSimulatedRun.mockResolvedValue({})
    render(ui('project-A'))
    type('My real objective')
    setClaims('3')
    setMinutes('30')

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Start simulated run' }))
    })

    expect(createManualRun).not.toHaveBeenCalled()
    const request = startSimulatedRun.mock.calls[0][0] as Record<string, unknown>
    expect(request.objective).toBe('Prove the walking skeleton')
    expect(request).not.toHaveProperty('maximumAgentAttempts')
    expect(request).not.toHaveProperty('maximumAgentInvocationMinutes')
    expect(claimsInput().value).toBe('3')
    expect(minutesInput().value).toBe('30')
  })

  it('sends one request when the demo and a manual submission with chosen budgets are triggered in the same tick', async () => {
    const pending = controllable<CreateManualRunResponse>()
    createManualRun.mockReturnValue(pending.promise)
    startSimulatedRun.mockReturnValue(pending.promise)
    render(ui('project-A'))
    type('Plan it')
    setClaims('2')
    const demo = screen.getByRole('button', { name: 'Start simulated run' })
    act(() => {
      demo.click()
      fireEvent.submit(screen.getByRole('form', { name: 'Record a manual run' }))
    })
    expect(createManualRun.mock.calls.length + startSimulatedRun.mock.calls.length).toBe(1)
    await act(async () => {
      pending.resolve(recorded())
    })
  })

  it('names the budget choices in a server validation refusal and keeps every draft', async () => {
    createManualRun.mockRejectedValue(problem(400, 'validation.invalid'))
    render(ui('project-A'))
    type('Plan it')
    setClaims('4')
    setMinutes('40')
    await act(async () => {
      fireEvent.click(submitButton())
    })
    expect(screen.getByRole('alert')).toHaveTextContent(/claims must be a whole number from 1 through 16/i)
    expect(textbox().value).toBe('Plan it')
    expect(claimsInput().value).toBe('4')
    expect(minutesInput().value).toBe('40')
  })
})
