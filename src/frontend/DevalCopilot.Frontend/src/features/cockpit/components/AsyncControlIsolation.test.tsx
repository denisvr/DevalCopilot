// @vitest-environment jsdom
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { useCallback } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  ApiException,
  ClaudeMutationTurnLimitResponse,
  CodexModelCatalogEntryResponse,
  CodexModelCatalogResponse,
} from '../../../api/generated/api-client'
import {
  authorizeReviewCorrectionWithGuidanceClient,
  codexModelCatalogClient,
  setClaudeModelPreferenceClient,
  setClaudeMutationTurnLimitClient,
  setCodexAssignmentPreferenceClient,
  setTokenStopThresholdClient,
} from '../../../api/clients'
import { useAuthorizeReviewCorrection } from '../hooks/useAuthorizeReviewCorrection'
import { ClaudeModelPreferenceControl } from './ClaudeModelPreferenceControl'
import { ClaudeMutationTurnLimitControl } from './ClaudeMutationTurnLimitControl'
import { CodexAssignmentPreferenceControl } from './CodexAssignmentPreferenceControl'
import { ReviewCorrectionGuidanceEntry } from './ReviewCorrectionGuidanceEntry'
import { TokenStopPanel } from './TokenStopPanel'

vi.mock('../../../api/clients', () => ({
  authorizeReviewCorrectionClient: vi.fn(),
  authorizeReviewCorrectionWithGuidanceClient: vi.fn(),
  codexModelCatalogClient: vi.fn(),
  setClaudeModelPreferenceClient: vi.fn(),
  setClaudeMutationTurnLimitClient: vi.fn(),
  setCodexAssignmentPreferenceClient: vi.fn(),
  setTokenStopThresholdClient: vi.fn(),
}))

/** These components are rendered WITHOUT the parent's run `key`, so a continuation that survived a
 * run switch would be visible: each control must satisfy its own ownership contract. */

function controllable<T = unknown>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

beforeEach(() => {
  vi.clearAllMocks()
})

describe('token stop (real hook) across a run switch', () => {
  const stops = [] as never[]
  const SYNC = /Saved, but the cockpit could not be refreshed/

  function setup() {
    const save = controllable()
    const setTokenStopThreshold = vi.fn().mockReturnValue(save.promise)
    vi.mocked(setTokenStopThresholdClient).mockReturnValue({ setTokenStopThreshold } as never)
    return { save, setTokenStopThreshold }
  }

  it('does not refresh, relabel, or warn when the save completes after the switch', async () => {
    const { save } = setup()
    const onSaved = vi.fn().mockResolvedValue(false)
    const { rerender } = render(<TokenStopPanel runId="run-A" tokenStops={stops} onSaved={onSaved} />)

    fireEvent.change(screen.getByLabelText('Codex stop threshold'), { target: { value: '500' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save Codex stop' }))
    rerender(<TokenStopPanel runId="run-B" tokenStops={stops} onSaved={onSaved} />)

    await act(async () => {
      save.resolve({})
    })

    expect(onSaved).not.toHaveBeenCalled()
    expect(screen.queryByText(SYNC)).toBeNull()
    expect(screen.getByRole('button', { name: 'Clear Codex stop' })).toBeDisabled()
  })

  it('does not show a sync warning when the refresh completes after the switch', async () => {
    const { save } = setup()
    const refresh = controllable<boolean>()
    const onSaved = vi.fn().mockReturnValue(refresh.promise)
    const { rerender } = render(<TokenStopPanel runId="run-A" tokenStops={stops} onSaved={onSaved} />)

    fireEvent.change(screen.getByLabelText('Codex stop threshold'), { target: { value: '500' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save Codex stop' }))
    await act(async () => {
      save.resolve({})
    })
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1))
    rerender(<TokenStopPanel runId="run-B" tokenStops={stops} onSaved={onSaved} />)

    await act(async () => {
      refresh.resolve(false)
    })
    expect(screen.queryByText(SYNC)).toBeNull()
  })

  it('still reports a truthful sync failure for the current interaction', async () => {
    const { save } = setup()
    const onSaved = vi.fn().mockResolvedValue(false)
    render(<TokenStopPanel runId="run-A" tokenStops={stops} onSaved={onSaved} />)

    fireEvent.change(screen.getByLabelText('Codex stop threshold'), { target: { value: '500' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save Codex stop' }))
    await act(async () => {
      save.resolve({})
    })

    expect(await screen.findByText(SYNC)).toBeTruthy()
  })
})

describe('Claude turn limit (real hook) across a run switch', () => {
  const notRequested = new ClaudeMutationTurnLimitResponse({ state: 'NotRequested' })
  const requested7 = new ClaudeMutationTurnLimitResponse({ state: 'Requested', maxTurns: 7 })
  const SYNC = /Saved, but the cockpit could not be refreshed/
  const draft = () => screen.getByLabelText('Requested Claude turn limit') as HTMLInputElement

  it('keeps the next run’s authoritative draft when a clear completes late, and never refreshes for it', async () => {
    const clear = controllable()
    vi.mocked(setClaudeMutationTurnLimitClient).mockReturnValue({
      setClaudeMutationTurnLimit: vi.fn().mockReturnValue(clear.promise),
    } as never)
    const onSaved = vi.fn().mockResolvedValue(true)
    const { rerender } = render(
      <ClaudeMutationTurnLimitControl runId="run-A" request={notRequested} editable onSaved={onSaved} />,
    )

    fireEvent.click(screen.getByRole('button', { name: 'Clear' }))
    rerender(<ClaudeMutationTurnLimitControl runId="run-B" request={requested7} editable onSaved={onSaved} />)

    await act(async () => {
      clear.resolve({})
    })

    expect(draft().value).toBe('7')
    expect(onSaved).not.toHaveBeenCalled()
  })

  it('keeps the next run’s authoritative draft and shows no warning when the refresh completes after the switch', async () => {
    vi.mocked(setClaudeMutationTurnLimitClient).mockReturnValue({
      setClaudeMutationTurnLimit: vi.fn().mockResolvedValue({}),
    } as never)
    const refresh = controllable<boolean>()
    const onSaved = vi.fn().mockReturnValue(refresh.promise)
    const { rerender } = render(
      <ClaudeMutationTurnLimitControl runId="run-A" request={notRequested} editable onSaved={onSaved} />,
    )

    fireEvent.click(screen.getByRole('button', { name: 'Clear' }))
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1))
    rerender(<ClaudeMutationTurnLimitControl runId="run-B" request={requested7} editable onSaved={onSaved} />)

    await act(async () => {
      refresh.resolve(false)
    })

    expect(screen.queryByText(SYNC)).toBeNull()
    expect(draft().value).toBe('7')
  })

  it('does not bring run A’s error back after A to B to A', async () => {
    vi.mocked(setClaudeMutationTurnLimitClient).mockReturnValue({
      setClaudeMutationTurnLimit: vi.fn().mockRejectedValue(new ApiException('x', 500, '{}', {}, null)),
    } as never)
    const { rerender } = render(<ClaudeMutationTurnLimitControl runId="run-A" request={notRequested} editable />)

    fireEvent.change(draft(), { target: { value: '5' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(await screen.findByRole('alert')).toBeTruthy()

    rerender(<ClaudeMutationTurnLimitControl runId="run-B" request={notRequested} editable />)
    rerender(<ClaudeMutationTurnLimitControl runId="run-A" request={notRequested} editable />)

    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('still shows a truthful sync failure for the current interaction', async () => {
    vi.mocked(setClaudeMutationTurnLimitClient).mockReturnValue({
      setClaudeMutationTurnLimit: vi.fn().mockResolvedValue({}),
    } as never)
    render(
      <ClaudeMutationTurnLimitControl
        runId="run-A"
        request={notRequested}
        editable
        onSaved={vi.fn().mockResolvedValue(false)}
      />,
    )

    fireEvent.change(draft(), { target: { value: '5' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText(SYNC)).toBeTruthy()
  })
})

describe('Claude model request (real hook) across a run switch', () => {
  const NONE = /No Claude model requested for future attempts/

  it('never adopts run A’s late save as run B’s saved label, and does not refresh', async () => {
    const save = controllable()
    vi.mocked(setClaudeModelPreferenceClient).mockReturnValue({
      setClaudeModelPreference: vi.fn().mockReturnValue(save.promise),
    } as never)
    const onSaved = vi.fn().mockResolvedValue(true)
    const { rerender } = render(
      <ClaudeModelPreferenceControl runId="run-A" requestedClaudeModel={null} requestedClaudeEffort={null} onSaved={onSaved} />,
    )

    fireEvent.change(screen.getByLabelText('Requested Claude model'), { target: { value: 'opus' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))
    rerender(
      <ClaudeModelPreferenceControl runId="run-B" requestedClaudeModel={null} requestedClaudeEffort={null} onSaved={onSaved} />,
    )

    await act(async () => {
      save.resolve({})
    })

    expect(screen.getAllByText(NONE).length).toBeGreaterThan(0)
    expect(screen.queryByText(/Requested Claude model for future attempts: opus/)).toBeNull()
    expect(onSaved).not.toHaveBeenCalled()
  })
})

describe('Codex model/effort (real hook, control not keyed by the cockpit) across a run switch', () => {
  it('never adopts run A’s late save as run B’s saved label', async () => {
    vi.mocked(codexModelCatalogClient).mockReturnValue({
      getCodexModelCatalog: vi.fn().mockResolvedValue(
        new CodexModelCatalogResponse({
          status: 'Observed',
          retrievedAtUtc: new Date() as never,
          models: [
            new CodexModelCatalogEntryResponse({
              id: 'gpt-6-sol',
              displayName: 'GPT-6 Sol',
              supportedReasoningEfforts: ['medium'],
              defaultReasoningEffort: 'medium',
            }),
          ],
        }),
      ),
    } as never)
    const save = controllable()
    vi.mocked(setCodexAssignmentPreferenceClient).mockReturnValue({
      setCodexAssignmentPreference: vi.fn().mockReturnValue(save.promise),
    } as never)
    const { rerender } = render(
      <CodexAssignmentPreferenceControl runId="run-A" requestedCodexModel={null} requestedCodexEffort={null} />,
    )

    await screen.findByRole('option', { name: /GPT-6 Sol/ })
    fireEvent.change(screen.getByLabelText('Requested Codex model'), { target: { value: 'gpt-6-sol' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))
    rerender(<CodexAssignmentPreferenceControl runId="run-B" requestedCodexModel={null} requestedCodexEffort={null} />)

    await act(async () => {
      save.resolve({})
    })

    expect(document.body.textContent ?? '').not.toMatch(/Requested Codex model for future attempts: gpt-6-sol/)
    expect(screen.getByRole('button', { name: 'Clear' })).toBeDisabled()
  })
})

describe('guided review-correction authorization (real hook) across a run switch', () => {
  function Harness({ runId, onAuthorized }: { runId: string; onAuthorized: () => void }) {
    const authorization = useAuthorizeReviewCorrection(runId, onAuthorized)
    const submit = useCallback(
      (guidance: string) => authorization.authorize(runId, `escalation-${runId}`, guidance),
      [authorization, runId],
    )
    return (
      <ReviewCorrectionGuidanceEntry
        escalationId={`escalation-${runId}`}
        authorizing={authorization.authorizing}
        statusLoading={false}
        onSubmit={submit}
      />
    )
  }
  const textarea = () => screen.getByLabelText('Optional guidance for the correction') as HTMLTextAreaElement

  it('keeps the next run’s draft and does not refresh when the old authorization completes', async () => {
    const pending = controllable()
    vi.mocked(authorizeReviewCorrectionWithGuidanceClient).mockReturnValue({
      authorizeReviewCorrectionWithGuidance: vi.fn().mockReturnValue(pending.promise),
    } as never)
    const onAuthorized = vi.fn()
    const { rerender } = render(<Harness runId="run-A" onAuthorized={onAuthorized} />)

    fireEvent.change(textarea(), { target: { value: 'first guidance' } })
    fireEvent.click(screen.getByRole('button', { name: 'Authorize with guidance' }))
    rerender(<Harness runId="run-B" onAuthorized={onAuthorized} />)
    expect(textarea().value).toBe('')
    fireEvent.change(textarea(), { target: { value: 'second guidance' } })

    await act(async () => {
      pending.resolve({})
    })

    expect(textarea().value).toBe('second guidance')
    expect(onAuthorized).not.toHaveBeenCalled()
  })

  it('clears the draft and refreshes once for the current interaction', async () => {
    vi.mocked(authorizeReviewCorrectionWithGuidanceClient).mockReturnValue({
      authorizeReviewCorrectionWithGuidance: vi.fn().mockResolvedValue({}),
    } as never)
    const onAuthorized = vi.fn()
    render(<Harness runId="run-A" onAuthorized={onAuthorized} />)

    fireEvent.change(textarea(), { target: { value: 'guidance' } })
    fireEvent.click(screen.getByRole('button', { name: 'Authorize with guidance' }))

    await waitFor(() => expect(textarea().value).toBe(''))
    expect(onAuthorized).toHaveBeenCalledTimes(1)
  })
})
