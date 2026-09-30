// @vitest-environment jsdom
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiException, ClaudeMutationTurnLimitResponse } from '../../../api/generated/api-client'
import { setClaudeMutationTurnLimitClient } from '../../../api/clients'
import { ClaudeMutationTurnLimitControl } from './ClaudeMutationTurnLimitControl'

vi.mock('../../../api/clients', () => ({
  setClaudeMutationTurnLimitClient: vi.fn(),
}))

const requested = (maxTurns: number) => new ClaudeMutationTurnLimitResponse({ state: 'Requested', maxTurns })
const notRequested = new ClaudeMutationTurnLimitResponse({ state: 'NotRequested' })

function mockClient(impl: (...args: unknown[]) => Promise<unknown>) {
  const setClaudeMutationTurnLimit = vi.fn(impl)
  vi.mocked(setClaudeMutationTurnLimitClient).mockReturnValue({ setClaudeMutationTurnLimit } as never)
  return setClaudeMutationTurnLimit
}

const input = () => screen.getByLabelText('Requested Claude turn limit') as HTMLInputElement

beforeEach(() => {
  vi.mocked(setClaudeMutationTurnLimitClient).mockReset()
})

describe('ClaudeMutationTurnLimitControl', () => {
  it('is labelled and states it is a request for future attempts, not an enforced resource limit', () => {
    render(<ClaudeMutationTurnLimitControl runId="run-1" request={notRequested} editable />)

    expect(screen.getByRole('group', { name: 'Claude turn limit' })).toBeTruthy()
    const text = document.body.textContent ?? ''
    expect(text).toMatch(/A request applied to future Claude implementation and review-correction attempts only/)
    expect(text).toMatch(/not an account or host-enforced resource limit/)
    expect(text).not.toMatch(/unlimited/i)
  })

  it.each([
    [notRequested, 'Current run request: Not requested', ''],
    [requested(12), 'Current run request: 12 turns', '12'],
    [new ClaudeMutationTurnLimitResponse({ state: 'Unknown' }), 'Current run request: Unknown', ''],
    [undefined, 'Current run request: Unknown', ''],
  ])('renders the saved request %j', (request, text, draft) => {
    render(<ClaudeMutationTurnLimitControl runId="run-1" request={request} editable />)

    expect(screen.getByText(text)).toBeTruthy()
    expect(input().value).toBe(draft)
  })

  it('saves a valid number and refreshes the cockpit once', async () => {
    const client = mockClient(async () => ({ maxTurns: 20 }))
    const onSaved = vi.fn().mockResolvedValue(true)
    render(<ClaudeMutationTurnLimitControl runId="run-1" request={notRequested} editable onSaved={onSaved} />)

    fireEvent.change(input(), { target: { value: '20' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1))
    expect(JSON.stringify(client.mock.calls[0][1])).toBe('{"maxTurns":20}')
  })

  it('clears with an explicit null and refreshes', async () => {
    const client = mockClient(async () => ({}))
    const onSaved = vi.fn().mockResolvedValue(true)
    render(<ClaudeMutationTurnLimitControl runId="run-1" request={requested(9)} editable onSaved={onSaved} />)

    fireEvent.click(screen.getByRole('button', { name: 'Clear' }))

    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1))
    expect(JSON.stringify(client.mock.calls[0][1])).toBe('{"maxTurns":null}')
    expect(input().value).toBe('')
  })

  it.each(['0', '101', '3.5', '1e2', '+5', '  ', 'abc'])('shows a validation error for %j and does not call the server', (value) => {
    const client = mockClient(async () => ({}))
    render(<ClaudeMutationTurnLimitControl runId="run-1" request={notRequested} editable />)

    fireEvent.change(input(), { target: { value } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(screen.getByRole('alert').textContent).toBe('Enter a whole number from 1 to 100.')
    expect(client).not.toHaveBeenCalled()
  })

  it('does not treat an empty Save as a silent clear', () => {
    const client = mockClient(async () => ({}))
    render(<ClaudeMutationTurnLimitControl runId="run-1" request={requested(4)} editable />)

    fireEvent.change(input(), { target: { value: '' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(screen.getByRole('alert').textContent).toBe('Enter a whole number from 1 to 100, or use Clear.')
    expect(client).not.toHaveBeenCalled()
  })

  it('disables the input and both buttons while pending', async () => {
    let release: (value: unknown) => void = () => undefined
    mockClient(() => new Promise((resolve) => (release = resolve)))
    render(<ClaudeMutationTurnLimitControl runId="run-1" request={notRequested} editable />)

    fireEvent.change(input(), { target: { value: '5' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(input().disabled).toBe(true))
    expect((screen.getByRole('button', { name: 'Save' }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: 'Clear' }) as HTMLButtonElement).disabled).toBe(true)

    release({})
    await waitFor(() => expect(input().disabled).toBe(false))
  })

  it('shows a fixed reload message on a conflict without echoing server text and does not refresh', async () => {
    mockClient(async () => {
      throw new ApiException('sensitive', 409, '{"errors":[{"detail":"sensitive"}]}', {}, null)
    })
    const onSaved = vi.fn()
    render(<ClaudeMutationTurnLimitControl runId="run-1" request={notRequested} editable onSaved={onSaved} />)

    fireEvent.change(input(), { target: { value: '5' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('The Claude turn limit request changed concurrently. Reload the run and retry.')).toBeTruthy()
    expect(screen.queryByText(/sensitive/)).toBeNull()
    expect(onSaved).not.toHaveBeenCalled()
    expect(screen.getByText('Current run request: Not requested')).toBeTruthy()
  })

  it('shows a fixed message when the refresh after a save fails', async () => {
    mockClient(async () => ({}))
    render(<ClaudeMutationTurnLimitControl runId="run-1" request={notRequested} editable onSaved={vi.fn().mockResolvedValue(false)} />)

    fireEvent.change(input(), { target: { value: '5' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(
      await screen.findByText('Saved, but the cockpit could not be refreshed; the displayed request may be out of date.'),
    ).toBeTruthy()
  })

  it('offers no editing controls when the run can no longer be changed', () => {
    render(<ClaudeMutationTurnLimitControl runId="run-1" request={requested(8)} editable={false} />)

    expect(screen.getByText('Current run request: 8 turns')).toBeTruthy()
    expect(screen.queryByLabelText('Requested Claude turn limit')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Save' })).toBeNull()
    expect(screen.getByText(/can no longer be changed/)).toBeTruthy()
  })

  it('drops an error when the run switches', async () => {
    mockClient(async () => {
      throw new Error('boom')
    })
    const { rerender } = render(<ClaudeMutationTurnLimitControl runId="run-1" request={notRequested} editable />)
    fireEvent.change(input(), { target: { value: '5' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))
    await screen.findByRole('alert')

    rerender(<ClaudeMutationTurnLimitControl runId="run-2" request={notRequested} editable />)

    expect(screen.queryByRole('alert')).toBeNull()
  })
})
