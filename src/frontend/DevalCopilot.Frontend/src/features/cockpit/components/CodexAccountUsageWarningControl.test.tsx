// @vitest-environment jsdom
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  ApiException,
  CodexAccountUsageWarningSettingResponse,
  CodexAccountUsageWarningWindowResponse,
  GetCodexAccountUsageWarningResponse,
} from '../../../api/generated/api-client'
import { getCodexAccountUsageWarningClient, setCodexAccountUsageWarningClient } from '../../../api/clients'
import { CodexAccountUsageWarningControl } from './CodexAccountUsageWarningControl'

vi.mock('../../../api/clients', () => ({
  setCodexAccountUsageWarningClient: vi.fn(),
  getCodexAccountUsageWarningClient: vi.fn(),
}))

const configured = (percent: number) => new CodexAccountUsageWarningSettingResponse({ state: 'Configured', percent })
const notConfigured = new CodexAccountUsageWarningSettingResponse({ state: 'NotConfigured' })

const OBSERVED = new Date('2026-10-05T12:00:00.000Z')

function checkResult(state: 'Below' | 'Reached', threshold: number, used: number) {
  return new GetCodexAccountUsageWarningResponse({
    state,
    reason: state === 'Reached' ? 'ThresholdReached' : undefined,
    thresholdPercent: threshold,
    observedAtUtc: OBSERVED,
    windows: [
      new CodexAccountUsageWarningWindowResponse({
        bucketId: 'codex',
        window: 'Primary',
        usedPercent: used,
        reachedThreshold: used >= threshold,
      }),
    ],
    providerReportedLimitReached: false,
  })
}

function mockSet(impl: (...args: unknown[]) => Promise<unknown>) {
  const setCodexAccountUsageWarning = vi.fn(impl)
  vi.mocked(setCodexAccountUsageWarningClient).mockReturnValue({ setCodexAccountUsageWarning } as never)
  return setCodexAccountUsageWarning
}

function mockGet(impl: (...args: unknown[]) => Promise<unknown>) {
  const getCodexAccountUsageWarning = vi.fn(impl)
  vi.mocked(getCodexAccountUsageWarningClient).mockReturnValue({ getCodexAccountUsageWarning } as never)
  return getCodexAccountUsageWarning
}

function deferredGet() {
  const calls: { resolve: (value: unknown) => void; reject: (reason: unknown) => void }[] = []
  const client = mockGet(() => new Promise((resolve, reject) => calls.push({ resolve, reject })))
  return { calls, client }
}

const input = () => screen.getByLabelText('Codex account-usage warning percentage') as HTMLInputElement
const saveButton = () => screen.getByRole('button', { name: 'Save Codex account-usage warning' }) as HTMLButtonElement
const clearButton = () => screen.getByRole('button', { name: 'Clear Codex account-usage warning' }) as HTMLButtonElement
const checkButton = () => screen.getByRole('button', { name: 'Check Codex account warning' }) as HTMLButtonElement
const result = () => screen.getByRole('status')

const NOTE =
  'Advisory only. When you explicitly check the Codex account, this shows whether a reported account-usage window has reached this percentage. It never blocks, reserves or stops any request, and it is separate from the account-usage stop. A check describes the host’s Codex account, not usage attributable to this run, and shows no remaining capacity.'

beforeEach(() => {
  vi.mocked(setCodexAccountUsageWarningClient).mockReset()
  vi.mocked(getCodexAccountUsageWarningClient).mockReset()
})

describe('CodexAccountUsageWarningControl', () => {
  it('is labelled, advisory, distinct from the stop, and never claims eligibility or capacity', () => {
    render(<CodexAccountUsageWarningControl runId="run-1" setting={configured(80)} editable />)

    expect(screen.getByRole('group', { name: 'Codex account-usage warning' })).toBeTruthy()
    expect(screen.getByText(NOTE)).toBeTruthy()
    const text = (document.body.textContent ?? '').replace(NOTE, '')
    expect(text).not.toMatch(/eligible|ready|remaining|capacity available|live usage|safe to|available quota/i)
    expect(screen.queryByText(/account-usage stop$/)).toBeNull()
  })

  it.each([
    [notConfigured, 'Current run setting: Not configured', ''],
    [configured(80), 'Current run setting: 80% used', '80'],
    [new CodexAccountUsageWarningSettingResponse({ state: 'Unknown' }), 'Current run setting: Unknown', ''],
    [undefined, 'Current run setting: Unknown', ''],
  ])('renders the saved setting %j', (setting, text, draft) => {
    render(<CodexAccountUsageWarningControl runId="run-1" setting={setting} editable />)

    expect(screen.getByText(text)).toBeTruthy()
    expect(input().value).toBe(draft)
  })

  it('offers the explicit check only for a valid saved warning and performs no read on mount', () => {
    const get = mockGet(async () => checkResult('Below', 80, 1))
    const { rerender } = render(<CodexAccountUsageWarningControl runId="run-1" setting={configured(80)} editable />)

    expect(checkButton().textContent).toBe('Check Codex account warning')
    expect(result().textContent).toBe('Not checked in this view.')

    for (const setting of [notConfigured, new CodexAccountUsageWarningSettingResponse({ state: 'Unknown' }), undefined]) {
      rerender(<CodexAccountUsageWarningControl runId="run-1" setting={setting} editable />)
      expect(screen.queryByRole('button', { name: 'Check Codex account warning' })).toBeNull()
    }

    expect(get).not.toHaveBeenCalled()
  })

  it('saving refreshes the cockpit once and performs no warning read', async () => {
    const set = mockSet(async () => ({ percent: 90 }))
    const get = mockGet(async () => checkResult('Below', 90, 1))
    const onSaved = vi.fn().mockResolvedValue(true)
    render(<CodexAccountUsageWarningControl runId="run-1" setting={notConfigured} editable onSaved={onSaved} />)

    fireEvent.change(input(), { target: { value: '90' } })
    fireEvent.click(saveButton())

    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1))
    expect(JSON.stringify(set.mock.calls[0][1])).toBe('{"percent":90}')
    expect(get).not.toHaveBeenCalled()
  })

  it('clearing sends an explicit null, refreshes, and adds no warning read', async () => {
    const set = mockSet(async () => ({}))
    const get = mockGet(async () => checkResult('Below', 70, 1))
    const onSaved = vi.fn().mockResolvedValue(true)
    render(<CodexAccountUsageWarningControl runId="run-1" setting={configured(70)} editable onSaved={onSaved} />)

    fireEvent.click(clearButton())

    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1))
    expect(JSON.stringify(set.mock.calls[0][1])).toBe('{"percent":null}')
    expect(input().value).toBe('')
    expect(get).not.toHaveBeenCalled()
  })

  it.each(['0', '101', '3.5', '1e2', '+5', '  ', 'abc'])('shows a validation error for %j and calls no server', (value) => {
    const set = mockSet(async () => ({}))
    render(<CodexAccountUsageWarningControl runId="run-1" setting={notConfigured} editable />)

    fireEvent.change(input(), { target: { value } })
    fireEvent.click(saveButton())

    expect(screen.getByRole('alert').textContent).toBe('Enter a whole number from 1 to 100.')
    expect(set).not.toHaveBeenCalled()
  })

  it('does not treat an empty Save as a silent clear', () => {
    const set = mockSet(async () => ({}))
    render(<CodexAccountUsageWarningControl runId="run-1" setting={configured(40)} editable />)

    fireEvent.change(input(), { target: { value: '' } })
    fireEvent.click(saveButton())

    expect(screen.getByRole('alert').textContent).toBe('Enter a whole number from 1 to 100, or use Clear.')
    expect(set).not.toHaveBeenCalled()
  })

  it('shows a fixed conflict message without echoing server text and does not refresh', async () => {
    mockSet(async () => {
      throw new ApiException('sensitive', 409, '{"errors":[{"detail":"sensitive"}]}', {}, null)
    })
    const onSaved = vi.fn()
    render(<CodexAccountUsageWarningControl runId="run-1" setting={notConfigured} editable onSaved={onSaved} />)

    fireEvent.change(input(), { target: { value: '5' } })
    fireEvent.click(saveButton())

    expect(await screen.findByText('The run changed concurrently, or it does not allow this. Reload the run and retry.')).toBeTruthy()
    expect(screen.queryByText(/sensitive/)).toBeNull()
    expect(onSaved).not.toHaveBeenCalled()
  })

  it('is read-only when the run is no longer editable but can still be checked explicitly', async () => {
    const get = mockGet(async () => checkResult('Below', 80, 12))
    render(<CodexAccountUsageWarningControl runId="run-1" setting={configured(80)} editable={false} />)

    expect(screen.getByText("This run's Codex account-usage warning can no longer be changed.")).toBeTruthy()
    expect(screen.queryByLabelText('Codex account-usage warning percentage')).toBeNull()

    fireEvent.click(checkButton())
    await waitFor(() => expect(result().getAttribute('data-result')).toBe('below'))
    expect(get).toHaveBeenCalledTimes(1)
  })
})

describe('CodexAccountUsageWarningControl explicit check', () => {
  it('performs one read on activation, is pending with the classification hidden, then shows a dated Below result', async () => {
    const { calls, client } = deferredGet()
    render(<CodexAccountUsageWarningControl runId="run-1" setting={configured(80)} editable />)

    fireEvent.click(checkButton())

    await waitFor(() => expect(result().textContent).toBe('Checking the Codex account…'))
    expect(result().getAttribute('data-result')).toBe('pending')
    expect(checkButton().disabled).toBe(true)
    expect(client).toHaveBeenCalledTimes(1)
    expect(client).toHaveBeenCalledWith('run-1')

    await act(async () => {
      calls[0].resolve(checkResult('Below', 80, 10))
    })

    await waitFor(() => expect(result().getAttribute('data-result')).toBe('below'))
    expect(result().textContent).toContain('No reported usage window had reached the 80% warning when the Codex account was observed.')
    expect(result().textContent).toContain(`Host retrieval time: ${OBSERVED.toLocaleString()}`)
    expect(result().textContent).toContain('codex primary window: 10% used')
    expect(checkButton().disabled).toBe(false)
  })

  it('shows a Reached result and a repeat check hides it while pending, and a failure clears it', async () => {
    const { calls } = deferredGet()
    render(<CodexAccountUsageWarningControl runId="run-1" setting={configured(80)} editable />)

    fireEvent.click(checkButton())
    await act(async () => {
      calls[0].resolve(checkResult('Reached', 80, 80))
    })
    await waitFor(() => expect(result().getAttribute('data-result')).toBe('reached'))
    expect(result().textContent).toContain('codex primary window: 80% used (warning reached)')

    fireEvent.click(checkButton())
    await waitFor(() => expect(result().getAttribute('data-result')).toBe('pending'))
    expect(result().textContent).not.toContain('warning reached')

    await act(async () => {
      calls[1].reject(new Error('sensitive detail'))
    })
    await waitFor(() => expect(result().getAttribute('data-result')).toBe('failed'))
    expect(result().textContent).toBe('The Codex account could not be checked. Try again.')
    expect(screen.queryByText(/sensitive/)).toBeNull()
  })

  it('ignores a second activation while a check is pending', async () => {
    const { calls, client } = deferredGet()
    render(<CodexAccountUsageWarningControl runId="run-1" setting={configured(80)} editable />)

    fireEvent.click(checkButton())
    fireEvent.click(checkButton())
    fireEvent.click(checkButton())

    expect(client).toHaveBeenCalledTimes(1)
    await act(async () => {
      calls[0].resolve(checkResult('Below', 80, 1))
    })
  })

  it('shows an unavailable answer with fixed copy and no classification', async () => {
    mockGet(async () => new GetCodexAccountUsageWarningResponse({ state: 'Unavailable', reason: 'EvidenceExpired', windows: [] }))
    render(<CodexAccountUsageWarningControl runId="run-1" setting={configured(80)} editable />)

    fireEvent.click(checkButton())

    await waitFor(() => expect(result().getAttribute('data-result')).toBe('unavailable'))
    expect(result().textContent).toBe('The Codex account observation was no longer current, so the warning could not be checked.')
  })

  it('a replaced saved warning (no parent key) drops the pending state and observation, and a late completion stays inert', async () => {
    const { calls } = deferredGet()
    const { rerender } = render(<CodexAccountUsageWarningControl runId="run-1" setting={configured(80)} editable />)

    fireEvent.click(checkButton())
    await waitFor(() => expect(result().getAttribute('data-result')).toBe('pending'))

    rerender(<CodexAccountUsageWarningControl runId="run-1" setting={configured(90)} editable />)
    expect(result().textContent).toBe('Not checked in this view.')
    expect(checkButton().disabled).toBe(false)
    expect(input().value).toBe('90')

    await act(async () => {
      calls[0].resolve(checkResult('Reached', 80, 99))
    })
    expect(result().textContent).toBe('Not checked in this view.')
  })

  it('A -> B -> A starts a new lifetime without the earlier observation and ignores the old completion', async () => {
    const { calls } = deferredGet()
    const { rerender } = render(<CodexAccountUsageWarningControl runId="run-1" setting={configured(80)} editable />)

    fireEvent.click(checkButton())
    await act(async () => {
      calls[0].resolve(checkResult('Below', 80, 1))
    })
    await waitFor(() => expect(result().getAttribute('data-result')).toBe('below'))
    fireEvent.click(checkButton())
    await waitFor(() => expect(result().getAttribute('data-result')).toBe('pending'))

    rerender(<CodexAccountUsageWarningControl runId="run-1" setting={configured(90)} editable />)
    rerender(<CodexAccountUsageWarningControl runId="run-1" setting={configured(80)} editable />)
    expect(result().textContent).toBe('Not checked in this view.')

    await act(async () => {
      calls[1].resolve(checkResult('Reached', 80, 95))
    })
    expect(result().textContent).toBe('Not checked in this view.')
  })

  it('a run switch drops the check and a stale completion never reaches the other run', async () => {
    const { calls } = deferredGet()
    const { rerender } = render(<CodexAccountUsageWarningControl runId="run-1" setting={configured(80)} editable />)

    fireEvent.click(checkButton())
    rerender(<CodexAccountUsageWarningControl runId="run-2" setting={configured(80)} editable />)
    expect(result().textContent).toBe('Not checked in this view.')

    await act(async () => {
      calls[0].resolve(checkResult('Reached', 80, 99))
    })
    expect(result().textContent).toBe('Not checked in this view.')
  })

  it('an authoritative save of a new value drops the old observation without reading again', async () => {
    mockSet(async () => ({ percent: 90 }))
    const get = mockGet(async () => checkResult('Below', 80, 1))
    let setting = configured(80)
    const onSaved = vi.fn(async () => true)
    const { rerender } = render(<CodexAccountUsageWarningControl runId="run-1" setting={setting} editable onSaved={onSaved} />)

    fireEvent.click(checkButton())
    await waitFor(() => expect(result().getAttribute('data-result')).toBe('below'))

    fireEvent.change(input(), { target: { value: '90' } })
    fireEvent.click(saveButton())
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1))
    setting = configured(90)
    rerender(<CodexAccountUsageWarningControl runId="run-1" setting={setting} editable onSaved={onSaved} />)

    expect(result().textContent).toBe('Not checked in this view.')
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('unmounting while a check is pending leaves a late completion inert and warning-free', async () => {
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    const { calls } = deferredGet()
    const { unmount } = render(<CodexAccountUsageWarningControl runId="run-1" setting={configured(80)} editable />)

    fireEvent.click(checkButton())
    unmount()
    await act(async () => {
      calls[0].resolve(checkResult('Below', 80, 1))
    })

    expect(errorSpy).not.toHaveBeenCalled()
    errorSpy.mockRestore()
  })
})
