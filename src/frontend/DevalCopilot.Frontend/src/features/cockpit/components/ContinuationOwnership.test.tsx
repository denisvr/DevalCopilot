// @vitest-environment jsdom
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import type { ComponentType } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  ClaudeMutationTurnLimitResponse,
  CodexModelCatalogEntryResponse,
  CodexModelCatalogResponse,
} from '../../../api/generated/api-client'
import {
  codexModelCatalogClient,
  setClaudeModelPreferenceClient,
  setClaudeMutationTurnLimitClient,
  setCodexAssignmentPreferenceClient,
  setTokenStopThresholdClient,
  setTokenWarningThresholdClient,
} from '../../../api/clients'
import { ClaudeModelPreferenceControl } from './ClaudeModelPreferenceControl'
import { ClaudeMutationTurnLimitControl } from './ClaudeMutationTurnLimitControl'
import { CodexAssignmentPreferenceControl } from './CodexAssignmentPreferenceControl'
import { ReviewCorrectionGuidanceEntry } from './ReviewCorrectionGuidanceEntry'
import { TokenStopPanel } from './TokenStopPanel'
import { TokenWarningPanel } from './TokenWarningPanel'

vi.mock('../../../api/clients', () => ({
  codexModelCatalogClient: vi.fn(),
  setCodexAssignmentPreferenceClient: vi.fn(),
  setClaudeModelPreferenceClient: vi.fn(),
  setClaudeMutationTurnLimitClient: vi.fn(),
  setTokenStopThresholdClient: vi.fn(),
  setTokenWarningThresholdClient: vi.fn(),
}))

/** Continuations must be owned by the committed identity that owns the component's local state, not
 * only by the run lifetime. These controls are rendered without the parent's key on purpose. */

function controllable<T = unknown>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}

const SYNC = /Saved, but the cockpit could not be refreshed/
const requested = (maxTurns: number) => new ClaudeMutationTurnLimitResponse({ state: 'Requested', maxTurns })

beforeEach(() => {
  vi.clearAllMocks()
})

describe('guidance entry completion ownership', () => {
  const text = () => screen.getByLabelText('Optional guidance for the correction') as HTMLTextAreaElement
  const submit = () => fireEvent.click(screen.getByRole('button', { name: 'Authorize with guidance' }))
  const entry = (escalationId: string, onSubmit: () => Promise<boolean>) => (
    <ReviewCorrectionGuidanceEntry escalationId={escalationId} authorizing={false} statusLoading={false} onSubmit={onSubmit} />
  )

  it('never erases escalation B draft when A accepted result completes, even for identical text', async () => {
    const accepted = controllable<boolean>()
    const onSubmit = vi.fn().mockReturnValue(accepted.promise)
    const { rerender } = render(entry('escalation-A', onSubmit))

    fireEvent.change(text(), { target: { value: 'same text' } })
    submit()
    rerender(entry('escalation-B', onSubmit))
    fireEvent.change(text(), { target: { value: 'same text' } })

    await act(async () => {
      accepted.resolve(true)
    })

    expect(text().value).toBe('same text')
  })

  it('does not erase the draft typed after returning A to B to A, nor after unmount and remount', async () => {
    const accepted = controllable<boolean>()
    const onSubmit = vi.fn().mockReturnValue(accepted.promise)
    const { rerender } = render(entry('escalation-A', onSubmit))

    fireEvent.change(text(), { target: { value: 'same text' } })
    submit()
    rerender(entry('escalation-B', onSubmit))
    rerender(entry('escalation-A', onSubmit))
    fireEvent.change(text(), { target: { value: 'same text' } })

    await act(async () => {
      accepted.resolve(true)
    })
    expect(text().value).toBe('same text')
  })

  it('does not write to an unmounted entry', async () => {
    const accepted = controllable<boolean>()
    const onSubmit = vi.fn().mockReturnValue(accepted.promise)
    const first = render(entry('escalation-A', onSubmit))
    fireEvent.change(text(), { target: { value: 'same text' } })
    submit()
    first.unmount()
    render(entry('escalation-A', onSubmit))
    fireEvent.change(text(), { target: { value: 'same text' } })

    await act(async () => {
      accepted.resolve(true)
    })
    expect(text().value).toBe('same text')
  })

  it('keeps an edit made after submission even when it yields identical text, and clears on a clean success', async () => {
    const accepted = controllable<boolean>()
    const onSubmit = vi.fn().mockReturnValue(accepted.promise)
    render(entry('escalation-A', onSubmit))

    fireEvent.change(text(), { target: { value: 'same text' } })
    submit()
    fireEvent.change(text(), { target: { value: 'same text plus' } })
    fireEvent.change(text(), { target: { value: 'same text' } })
    await act(async () => {
      accepted.resolve(true)
    })
    expect(text().value).toBe('same text')

  })

  it('clears the draft after a clean, current, unedited success', async () => {
    const clean = vi.fn().mockResolvedValue(true)
    render(entry('escalation-C', clean))

    fireEvent.change(text(), { target: { value: 'fresh' } })
    submit()

    await waitFor(() => expect(text().value).toBe(''))
    expect(clean).toHaveBeenCalledWith('fresh')
  })
})

describe('setting flows follow their authoritative identity', () => {
  const draft = () => screen.getByLabelText('Requested Claude turn limit') as HTMLInputElement

  it('turn limit: an authoritative 3 to 7 change during the refresh drops the old refresh warning', async () => {
    vi.mocked(setClaudeMutationTurnLimitClient).mockReturnValue({
      setClaudeMutationTurnLimit: vi.fn().mockResolvedValue({}),
    } as never)
    const refresh = controllable<boolean>()
    const onSaved = vi.fn().mockReturnValue(refresh.promise)
    const { rerender } = render(<ClaudeMutationTurnLimitControl runId="run-A" request={requested(3)} editable onSaved={onSaved} />)

    fireEvent.click(screen.getByRole('button', { name: 'Clear' }))
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1))
    rerender(<ClaudeMutationTurnLimitControl runId="run-A" request={requested(7)} editable onSaved={onSaved} />)
    expect(draft().value).toBe('7')

    await act(async () => {
      refresh.resolve(false)
    })
    expect(screen.queryByText(SYNC)).toBeNull()
    expect(draft().value).toBe('7')
  })

  it('turn limit: returning to an earlier identity does not revive the old flow', async () => {
    vi.mocked(setClaudeMutationTurnLimitClient).mockReturnValue({
      setClaudeMutationTurnLimit: vi.fn().mockResolvedValue({}),
    } as never)
    const refresh = controllable<boolean>()
    const onSaved = vi.fn().mockReturnValue(refresh.promise)
    const { rerender } = render(<ClaudeMutationTurnLimitControl runId="run-A" request={requested(3)} editable onSaved={onSaved} />)

    fireEvent.click(screen.getByRole('button', { name: 'Clear' }))
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1))
    rerender(<ClaudeMutationTurnLimitControl runId="run-A" request={requested(7)} editable onSaved={onSaved} />)
    rerender(<ClaudeMutationTurnLimitControl runId="run-A" request={requested(3)} editable onSaved={onSaved} />)

    await act(async () => {
      refresh.resolve(false)
    })
    expect(screen.queryByText(SYNC)).toBeNull()
  })

  it('turn limit: an identity change during the API phase keeps the server mutation but skips local writes and refresh', async () => {
    const api = controllable()
    vi.mocked(setClaudeMutationTurnLimitClient).mockReturnValue({
      setClaudeMutationTurnLimit: vi.fn().mockReturnValue(api.promise),
    } as never)
    const onSaved = vi.fn().mockResolvedValue(false)
    const { rerender } = render(<ClaudeMutationTurnLimitControl runId="run-A" request={requested(3)} editable onSaved={onSaved} />)

    fireEvent.click(screen.getByRole('button', { name: 'Clear' }))
    rerender(<ClaudeMutationTurnLimitControl runId="run-A" request={requested(7)} editable onSaved={onSaved} />)
    await act(async () => {
      api.resolve({})
    })

    expect(draft().value).toBe('7')
    expect(onSaved).not.toHaveBeenCalled()
    expect(screen.queryByText(SYNC)).toBeNull()
  })

  it('turn limit: a same-run rerender with the same authoritative value keeps the flow and its truthful failure', async () => {
    vi.mocked(setClaudeMutationTurnLimitClient).mockReturnValue({
      setClaudeMutationTurnLimit: vi.fn().mockResolvedValue({}),
    } as never)
    const refresh = controllable<boolean>()
    const onSaved = vi.fn().mockReturnValue(refresh.promise)
    const { rerender } = render(<ClaudeMutationTurnLimitControl runId="run-A" request={requested(3)} editable onSaved={onSaved} />)

    fireEvent.click(screen.getByRole('button', { name: 'Clear' }))
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1))
    rerender(<ClaudeMutationTurnLimitControl runId="run-A" request={requested(3)} editable onSaved={onSaved} />)
    await act(async () => {
      refresh.resolve(false)
    })

    expect(await screen.findByText(SYNC)).toBeTruthy()
  })

  it('Claude model: an authoritative pair change during the refresh drops the old refresh warning', async () => {
    vi.mocked(setClaudeModelPreferenceClient).mockReturnValue({ setClaudeModelPreference: vi.fn().mockResolvedValue({}) } as never)
    const refresh = controllable<boolean>()
    const onSaved = vi.fn().mockReturnValue(refresh.promise)
    const { rerender } = render(
      <ClaudeModelPreferenceControl runId="run-A" requestedClaudeModel="opus" requestedClaudeEffort={null} onSaved={onSaved} />,
    )

    fireEvent.click(screen.getByRole('button', { name: 'Clear' }))
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1))
    rerender(<ClaudeModelPreferenceControl runId="run-A" requestedClaudeModel="sonnet" requestedClaudeEffort={null} onSaved={onSaved} />)
    await act(async () => {
      refresh.resolve(false)
    })

    expect(screen.queryByText(SYNC)).toBeNull()
    expect((screen.getByLabelText('Requested Claude model') as HTMLSelectElement).value).toBe('sonnet')
  })

  const panels = [
    {
      name: 'stop',
      Panel: TokenStopPanel as unknown as ComponentType<Record<string, unknown>>,
      client: setTokenStopThresholdClient as unknown as () => unknown,
      method: 'setTokenStopThreshold',
      prop: 'tokenStops',
      inputLabel: 'Codex stop threshold',
      saveLabel: 'Save Codex stop',
    },
    {
      name: 'warning',
      Panel: TokenWarningPanel as unknown as ComponentType<Record<string, unknown>>,
      client: setTokenWarningThresholdClient as unknown as () => unknown,
      method: 'setTokenWarningThreshold',
      prop: 'tokenWarnings',
      inputLabel: 'Codex warning threshold',
      saveLabel: 'Save Codex threshold',
    },
  ]

  it.each(panels)('token $name: an authoritative threshold change during the refresh drops the old refresh warning', async (c) => {
    vi.mocked(c.client).mockReturnValue({ [c.method]: vi.fn().mockResolvedValue({}) })
    const refresh = controllable<boolean>()
    const onSaved = vi.fn().mockReturnValue(refresh.promise)
    const at = (tokens: number) => [{ provider: 'Codex', thresholdTokens: tokens }]
    const { rerender } = render(<c.Panel runId="run-A" {...{ [c.prop]: at(100) }} onSaved={onSaved} />)

    fireEvent.change(screen.getByLabelText(c.inputLabel), { target: { value: '150' } })
    fireEvent.click(screen.getByRole('button', { name: c.saveLabel }))
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1))
    rerender(<c.Panel runId="run-A" {...{ [c.prop]: at(900) }} onSaved={onSaved} />)
    await act(async () => {
      refresh.resolve(false)
    })

    expect(screen.queryByText(SYNC)).toBeNull()
    expect((screen.getByLabelText(c.inputLabel) as HTMLInputElement).value).toBe('900')
  })
})

describe('Codex model/effort flow follows its authoritative identity (real setter hook)', () => {
  const LABEL = /Requested for future claims: /
  const label = () => document.body.textContent ?? ''

  function stubCatalogAndSetter(setter: Promise<unknown>) {
    vi.mocked(codexModelCatalogClient).mockReturnValue({
      getCodexModelCatalog: vi.fn().mockResolvedValue(
        new CodexModelCatalogResponse({
          status: 'Observed',
          retrievedAtUtc: new Date() as never,
          models: [
            new CodexModelCatalogEntryResponse({ id: 'gpt-A', displayName: 'A', supportedReasoningEfforts: ['high', 'low'] }),
            new CodexModelCatalogEntryResponse({ id: 'gpt-B', displayName: 'B', supportedReasoningEfforts: ['high', 'low'] }),
          ],
        }),
      ),
    } as never)
    const setCodexAssignmentPreference = vi.fn().mockReturnValue(setter)
    vi.mocked(setCodexAssignmentPreferenceClient).mockReturnValue({ setCodexAssignmentPreference } as never)
    return setCodexAssignmentPreference
  }
  const control = (model: string | null, effort: string | null, runId = 'run-A') => (
    <CodexAssignmentPreferenceControl runId={runId} requestedCodexModel={model} requestedCodexEffort={effort} />
  )

  it('Save: an old accepted request completing after the authoritative pair changed does not revert the label', async () => {
    const api = controllable()
    stubCatalogAndSetter(api.promise)
    const { rerender } = render(control(null, null))
    await screen.findByRole('option', { name: 'A' })

    fireEvent.change(screen.getByLabelText('Requested Codex model'), { target: { value: 'gpt-A' } })
    fireEvent.change(screen.getByLabelText('Requested Codex reasoning effort'), { target: { value: 'high' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))
    rerender(control('gpt-B', 'low'))
    expect(label()).toContain('gpt-B (low)')

    await act(async () => {
      api.resolve({})
    })

    expect(label()).toContain('gpt-B (low)')
    expect(label()).not.toContain('gpt-A (high)')
    expect((screen.getByLabelText('Requested Codex model') as HTMLSelectElement).value).toBe('gpt-B')
  })

  it('Clear: an old accepted request completing after the authoritative pair changed keeps B selections and label', async () => {
    const api = controllable()
    stubCatalogAndSetter(api.promise)
    const { rerender } = render(control('gpt-A', 'high'))
    await screen.findByRole('option', { name: 'A' })

    fireEvent.click(screen.getByRole('button', { name: 'Clear' }))
    rerender(control('gpt-B', 'low'))

    await act(async () => {
      api.resolve({})
    })

    expect(label()).toContain('gpt-B (low)')
    expect((screen.getByLabelText('Requested Codex model') as HTMLSelectElement).value).toBe('gpt-B')
    expect((screen.getByLabelText('Requested Codex reasoning effort') as HTMLSelectElement).value).toBe('low')
  })

  it('returning to the earlier identity does not revive the old flow, while a same-identity rerender keeps it', async () => {
    const api = controllable()
    stubCatalogAndSetter(api.promise)
    const { rerender } = render(control('gpt-A', 'high'))
    await screen.findByRole('option', { name: 'A' })

    fireEvent.click(screen.getByRole('button', { name: 'Clear' }))
    rerender(control('gpt-A', 'high'))
    expect(screen.getByRole('button', { name: 'Clear' })).toBeDisabled()
    rerender(control('gpt-B', 'low'))
    rerender(control('gpt-A', 'high'))

    await act(async () => {
      api.resolve({})
    })

    expect(label()).toContain('gpt-A (high)')
    expect((screen.getByLabelText('Requested Codex model') as HTMLSelectElement).value).toBe('gpt-A')
  })

  it('a current unchanged identity still updates the saved label and clears the selections', async () => {
    const api = controllable()
    stubCatalogAndSetter(api.promise)
    render(control('gpt-A', 'high'))
    await screen.findByRole('option', { name: 'A' })

    fireEvent.click(screen.getByRole('button', { name: 'Clear' }))
    await act(async () => {
      api.resolve({})
    })

    expect(label()).toContain('No requested Codex model/effort for future claims.')
    expect((screen.getByLabelText('Requested Codex model') as HTMLSelectElement).value).toBe('')
    expect(LABEL.test(label())).toBe(false)
  })
})
