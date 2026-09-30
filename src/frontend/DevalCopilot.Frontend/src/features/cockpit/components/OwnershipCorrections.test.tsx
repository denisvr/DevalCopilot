// @vitest-environment jsdom
import { act, fireEvent, render, renderHook, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ClaudeMutationTurnLimitResponse } from '../../../api/generated/api-client'
import {
  requestCodexPlanningAttemptClient,
  setClaudeModelPreferenceClient,
  setClaudeMutationTurnLimitClient,
  setTokenStopThresholdClient,
} from '../../../api/clients'
import { useRequestCodexPlanningAttempt } from '../hooks/useRequestCodexPlanningAttempt'
import { useSetClaudeMutationTurnLimit } from '../hooks/useSetClaudeMutationTurnLimit'
import { ClaudeModelPreferenceControl } from './ClaudeModelPreferenceControl'
import { ClaudeMutationTurnLimitControl } from './ClaudeMutationTurnLimitControl'
import { ReviewCorrectionGuidanceEntry } from './ReviewCorrectionGuidanceEntry'
import { TokenStopPanel } from './TokenStopPanel'

vi.mock('../../../api/clients', () => ({
  requestCodexPlanningAttemptClient: vi.fn(),
  setClaudeModelPreferenceClient: vi.fn(),
  setClaudeMutationTurnLimitClient: vi.fn(),
  setTokenStopThresholdClient: vi.fn(),
}))

function controllable<T = unknown>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}

const SYNC = /Saved, but the cockpit could not be refreshed/

beforeEach(() => {
  vi.clearAllMocks()
})

describe('handlers are bound to the lifetime that committed them', () => {
  it('rejects a retained run A planning handler after A to B to A: no API call, no callback', async () => {
    const operation = vi.fn().mockResolvedValue({})
    vi.mocked(requestCodexPlanningAttemptClient).mockReturnValue({ requestCodexPlanningAttempt: operation } as never)
    const done = vi.fn()
    const { result, rerender } = renderHook(({ runId }) => useRequestCodexPlanningAttempt(runId, done), {
      initialProps: { runId: 'run-A' },
    })
    const retained = result.current.request

    rerender({ runId: 'run-B' })
    rerender({ runId: 'run-A' })

    await act(async () => {
      expect(await retained('run-A')).toBe(false)
    })
    expect(operation).not.toHaveBeenCalled()
    expect(done).not.toHaveBeenCalled()

    await act(async () => {
      expect(await result.current.request('run-A')).toBe(true)
    })
    expect(operation).toHaveBeenCalledTimes(1)
  })

  it('keeps run B pending save when a retained run A handler reports a validation error', async () => {
    const pending = controllable()
    vi.mocked(setClaudeMutationTurnLimitClient).mockReturnValue({
      setClaudeMutationTurnLimit: vi.fn().mockReturnValue(pending.promise),
    } as never)
    const { result, rerender } = renderHook(({ runId }) => useSetClaudeMutationTurnLimit(runId), {
      initialProps: { runId: 'run-A' },
    })
    const retainedSave = result.current.save

    rerender({ runId: 'run-B' })
    let saveB!: Promise<boolean>
    act(() => {
      saveB = result.current.save('5')
    })
    expect(result.current.saving).toBe(true)

    await act(async () => {
      expect(await retainedSave('not a number')).toBe(false)
    })
    expect(result.current.saving).toBe(true)
    expect(result.current.error).toBeNull()

    await act(async () => {
      pending.resolve({})
      expect(await saveB).toBe(true)
    })
  })
})

describe('controls reset run-owned state when their identity changes (no parent key)', () => {
  it('Claude model control follows run B authoritative null request instead of keeping A selection', () => {
    const { rerender } = render(
      <ClaudeModelPreferenceControl runId="run-A" requestedClaudeModel="opus" requestedClaudeEffort="high" />,
    )
    expect((screen.getByLabelText('Requested Claude model') as HTMLSelectElement).value).toBe('opus')
    expect((screen.getByLabelText('Requested Claude effort') as HTMLSelectElement).value).toBe('high')

    rerender(<ClaudeModelPreferenceControl runId="run-B" requestedClaudeModel={null} requestedClaudeEffort={null} />)

    expect((screen.getByLabelText('Requested Claude model') as HTMLSelectElement).value).toBe('')
    expect((screen.getByLabelText('Requested Claude effort') as HTMLSelectElement).value).toBe('')
    expect(screen.getAllByText(/No Claude model requested for future attempts/).length).toBeGreaterThan(0)
  })

  it('turn-limit control shows run B authoritative 7 instead of A draft 5 and drops A warnings', () => {
    const requested = (maxTurns: number) => new ClaudeMutationTurnLimitResponse({ state: 'Requested', maxTurns })
    const { rerender } = render(<ClaudeMutationTurnLimitControl runId="run-A" request={requested(3)} editable />)
    const draft = () => screen.getByLabelText('Requested Claude turn limit') as HTMLInputElement
    fireEvent.change(draft(), { target: { value: '' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(screen.getByRole('alert')).toBeTruthy()
    fireEvent.change(draft(), { target: { value: '5' } })

    rerender(<ClaudeMutationTurnLimitControl runId="run-B" request={requested(7)} editable />)

    expect(draft().value).toBe('7')
    expect(screen.queryByRole('alert')).toBeNull()
  })
})

describe('turn-limit save/clear/refresh flow keeps operation ownership', () => {
  it('an older clear deferred refresh does not wipe a draft entered after a newer save', async () => {
    vi.mocked(setClaudeMutationTurnLimitClient).mockReturnValue({
      setClaudeMutationTurnLimit: vi.fn().mockResolvedValue({}),
    } as never)
    const clearRefresh = controllable<boolean>()
    const saveRefresh = controllable<boolean>()
    const onSaved = vi.fn().mockReturnValueOnce(clearRefresh.promise).mockReturnValueOnce(saveRefresh.promise)
    const request = new ClaudeMutationTurnLimitResponse({ state: 'NotRequested' })
    render(<ClaudeMutationTurnLimitControl runId="run-A" request={request} editable onSaved={onSaved} />)
    const draft = () => screen.getByLabelText('Requested Claude turn limit') as HTMLInputElement

    fireEvent.click(screen.getByRole('button', { name: 'Clear' }))
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1))

    fireEvent.change(draft(), { target: { value: '6' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(2))

    fireEvent.change(draft(), { target: { value: '8' } })
    await act(async () => {
      clearRefresh.resolve(false)
    })

    expect(draft().value).toBe('8')
    expect(screen.queryByText(SYNC)).toBeNull()

    await act(async () => {
      saveRefresh.resolve(false)
    })
    expect(await screen.findByText(SYNC)).toBeTruthy()
    expect(draft().value).toBe('8')
  })

  it('a typed draft survives the deferred refresh of the clear that just succeeded', async () => {
    vi.mocked(setClaudeMutationTurnLimitClient).mockReturnValue({
      setClaudeMutationTurnLimit: vi.fn().mockResolvedValue({}),
    } as never)
    const refresh = controllable<boolean>()
    const onSaved = vi.fn().mockReturnValue(refresh.promise)
    render(
      <ClaudeMutationTurnLimitControl
        runId="run-A"
        request={new ClaudeMutationTurnLimitResponse({ state: 'NotRequested' })}
        editable
        onSaved={onSaved}
      />,
    )
    const draft = () => screen.getByLabelText('Requested Claude turn limit') as HTMLInputElement

    fireEvent.click(screen.getByRole('button', { name: 'Clear' }))
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1))
    fireEvent.change(draft(), { target: { value: '9' } })
    await act(async () => {
      refresh.resolve(true)
    })

    expect(draft().value).toBe('9')
  })
})

describe('other control flows keep the same ownership', () => {
  it('token stop: an older save refresh cannot warn after a newer save, and run B shows its own threshold', async () => {
    vi.mocked(setTokenStopThresholdClient).mockReturnValue({ setTokenStopThreshold: vi.fn().mockResolvedValue({}) } as never)
    const first = controllable<boolean>()
    const second = controllable<boolean>()
    const onSaved = vi.fn().mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise)
    const { rerender } = render(<TokenStopPanel runId="run-A" tokenStops={[]} onSaved={onSaved} />)
    const input = () => screen.getByLabelText('Codex stop threshold') as HTMLInputElement

    fireEvent.change(input(), { target: { value: '100' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save Codex stop' }))
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1))
    fireEvent.change(input(), { target: { value: '200' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save Codex stop' }))
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(2))

    await act(async () => {
      first.resolve(false)
    })
    expect(screen.queryByText(SYNC)).toBeNull()
    await act(async () => {
      second.resolve(false)
    })
    expect(await screen.findByText(SYNC)).toBeTruthy()

    const stopB = [{ provider: 'Codex', thresholdTokens: 900 }] as never
    rerender(<TokenStopPanel runId="run-B" tokenStops={stopB} onSaved={onSaved} />)
    expect(input().value).toBe('900')
    expect(screen.queryByText(SYNC)).toBeNull()
  })

  it('Claude model: an older flow refresh cannot warn after a newer save', async () => {
    vi.mocked(setClaudeModelPreferenceClient).mockReturnValue({ setClaudeModelPreference: vi.fn().mockResolvedValue({}) } as never)
    const first = controllable<boolean>()
    const second = controllable<boolean>()
    const onSaved = vi.fn().mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise)
    render(<ClaudeModelPreferenceControl runId="run-A" requestedClaudeModel={null} requestedClaudeEffort={null} onSaved={onSaved} />)

    fireEvent.change(screen.getByLabelText('Requested Claude model'), { target: { value: 'opus' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1))
    fireEvent.change(screen.getByLabelText('Requested Claude model'), { target: { value: 'sonnet' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(2))

    await act(async () => {
      first.resolve(false)
    })
    expect(screen.queryByText(SYNC)).toBeNull()
    expect((screen.getByLabelText('Requested Claude model') as HTMLSelectElement).value).toBe('sonnet')
  })

  it('guidance: starts empty for a different escalation and keeps text typed after the accepted submission', async () => {
    const accepted = controllable<boolean>()
    const onSubmit = vi.fn().mockReturnValue(accepted.promise)
    const { rerender } = render(
      <ReviewCorrectionGuidanceEntry escalationId="escalation-1" authorizing={false} statusLoading={false} onSubmit={onSubmit} />,
    )
    const text = () => screen.getByLabelText('Optional guidance for the correction') as HTMLTextAreaElement

    fireEvent.change(text(), { target: { value: 'first' } })
    fireEvent.click(screen.getByRole('button', { name: 'Authorize with guidance' }))
    fireEvent.change(text(), { target: { value: 'first plus more' } })
    await act(async () => {
      accepted.resolve(true)
    })
    expect(text().value).toBe('first plus more')

    rerender(
      <ReviewCorrectionGuidanceEntry escalationId="escalation-2" authorizing={false} statusLoading={false} onSubmit={onSubmit} />,
    )
    expect(text().value).toBe('')
  })
})
