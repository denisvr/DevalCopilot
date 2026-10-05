// @vitest-environment jsdom
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiException, CodexAccountUsageStopResponse } from '../../../api/generated/api-client'
import { setCodexAccountUsageStopClient } from '../../../api/clients'
import { CodexAccountUsageStopControl } from './CodexAccountUsageStopControl'

vi.mock('../../../api/clients', () => ({
  setCodexAccountUsageStopClient: vi.fn(),
}))

const configured = (percent: number) => new CodexAccountUsageStopResponse({ state: 'Configured', percent })
const notConfigured = new CodexAccountUsageStopResponse({ state: 'NotConfigured' })

function mockClient(impl: (...args: unknown[]) => Promise<unknown>) {
  const setCodexAccountUsageStop = vi.fn(impl)
  vi.mocked(setCodexAccountUsageStopClient).mockReturnValue({ setCodexAccountUsageStop } as never)
  return setCodexAccountUsageStop
}

const input = () => screen.getByLabelText('Codex account-usage stop percentage') as HTMLInputElement
const saveButton = () => screen.getByRole('button', { name: 'Save Codex account-usage stop' }) as HTMLButtonElement
const clearButton = () => screen.getByRole('button', { name: 'Clear Codex account-usage stop' }) as HTMLButtonElement

const NOTE =
  'Stops a new Codex request when a reported account-usage window reaches this percentage. It is checked when the request is claimed and again just before it starts. Changing the setting never alters the threshold an already-claimed attempt recorded, but a claimed attempt is still checked before it starts and can be stopped. It is a local guard over a provider-reported percentage, not account access, remaining quota or live capacity.'

beforeEach(() => {
  vi.mocked(setCodexAccountUsageStopClient).mockReset()
})

describe('CodexAccountUsageStopControl', () => {
  it('is labelled and says it is a local guard over a provider-reported percentage', () => {
    render(<CodexAccountUsageStopControl runId="run-1" setting={notConfigured} editable />)

    expect(screen.getByRole('group', { name: 'Codex account-usage stop' })).toBeTruthy()
    expect(screen.getByText(NOTE)).toBeTruthy()
    const text = (document.body.textContent ?? '').replace(NOTE, '')
    expect(text).not.toMatch(/eligible|remaining|capacity available|safe to|available quota|live capacity/i)
  })

  it.each([
    [notConfigured, 'Current run setting: Not configured', ''],
    [configured(80), 'Current run setting: 80% used', '80'],
    [new CodexAccountUsageStopResponse({ state: 'Unknown' }), 'Current run setting: Unknown', ''],
    [undefined, 'Current run setting: Unknown', ''],
  ])('renders the saved setting %j', (setting, text, draft) => {
    render(<CodexAccountUsageStopControl runId="run-1" setting={setting} editable />)

    expect(screen.getByText(text)).toBeTruthy()
    expect(input().value).toBe(draft)
  })

  it('has visible Save and Clear text', () => {
    render(<CodexAccountUsageStopControl runId="run-1" setting={notConfigured} editable />)

    expect(saveButton().textContent).toBe('Save')
    expect(clearButton().textContent).toBe('Clear')
  })

  it('saves a valid number and refreshes the cockpit once', async () => {
    const client = mockClient(async () => ({ percent: 90 }))
    const onSaved = vi.fn().mockResolvedValue(true)
    render(<CodexAccountUsageStopControl runId="run-1" setting={notConfigured} editable onSaved={onSaved} />)

    fireEvent.change(input(), { target: { value: '90' } })
    fireEvent.click(saveButton())

    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1))
    expect(JSON.stringify(client.mock.calls[0][1])).toBe('{"percent":90}')
  })

  it('clears with an explicit null and refreshes', async () => {
    const client = mockClient(async () => ({}))
    const onSaved = vi.fn().mockResolvedValue(true)
    render(<CodexAccountUsageStopControl runId="run-1" setting={configured(70)} editable onSaved={onSaved} />)

    fireEvent.click(clearButton())

    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1))
    expect(JSON.stringify(client.mock.calls[0][1])).toBe('{"percent":null}')
    expect(input().value).toBe('')
  })

  it.each(['0', '101', '3.5', '1e2', '+5', '  ', 'abc'])('shows a validation error for %j and does not call the server', (value) => {
    const client = mockClient(async () => ({}))
    render(<CodexAccountUsageStopControl runId="run-1" setting={notConfigured} editable />)

    fireEvent.change(input(), { target: { value } })
    fireEvent.click(saveButton())

    expect(screen.getByRole('alert').textContent).toBe('Enter a whole number from 1 to 100.')
    expect(client).not.toHaveBeenCalled()
  })

  it('does not treat an empty Save as a silent clear', () => {
    const client = mockClient(async () => ({}))
    render(<CodexAccountUsageStopControl runId="run-1" setting={configured(40)} editable />)

    fireEvent.change(input(), { target: { value: '' } })
    fireEvent.click(saveButton())

    expect(screen.getByRole('alert').textContent).toBe('Enter a whole number from 1 to 100, or use Clear.')
    expect(client).not.toHaveBeenCalled()
  })

  it('disables the input and both buttons while pending', async () => {
    let release: (value: unknown) => void = () => undefined
    mockClient(() => new Promise((resolve) => (release = resolve)))
    render(<CodexAccountUsageStopControl runId="run-1" setting={notConfigured} editable />)

    fireEvent.change(input(), { target: { value: '5' } })
    fireEvent.click(saveButton())

    await waitFor(() => expect(input().disabled).toBe(true))
    expect(saveButton().disabled).toBe(true)
    expect(clearButton().disabled).toBe(true)

    release({})
    await waitFor(() => expect(input().disabled).toBe(false))
  })

  it('shows a fixed conflict message without echoing server text and does not refresh', async () => {
    mockClient(async () => {
      throw new ApiException('sensitive', 409, '{"errors":[{"detail":"sensitive"}]}', {}, null)
    })
    const onSaved = vi.fn()
    render(<CodexAccountUsageStopControl runId="run-1" setting={notConfigured} editable onSaved={onSaved} />)

    fireEvent.change(input(), { target: { value: '5' } })
    fireEvent.click(saveButton())

    expect(
      await screen.findByText('The account-usage stop changed concurrently, or this run does not allow it. Reload the run and retry.'),
    ).toBeTruthy()
    expect(screen.queryByText(/sensitive/)).toBeNull()
    expect(onSaved).not.toHaveBeenCalled()
    expect(screen.getByText('Current run setting: Not configured')).toBeTruthy()
  })

  it('shows a fixed message when the refresh after a save fails', async () => {
    mockClient(async () => ({}))
    render(<CodexAccountUsageStopControl runId="run-1" setting={notConfigured} editable onSaved={vi.fn().mockResolvedValue(false)} />)

    fireEvent.change(input(), { target: { value: '5' } })
    fireEvent.click(saveButton())

    expect(
      await screen.findByText('Saved, but the cockpit could not be refreshed; the displayed setting may be out of date.'),
    ).toBeTruthy()
  })

  it('offers no editing controls when the run can no longer be changed', () => {
    render(<CodexAccountUsageStopControl runId="run-1" setting={configured(60)} editable={false} />)

    expect(screen.getByText('Current run setting: 60% used')).toBeTruthy()
    expect(screen.queryByLabelText('Codex account-usage stop percentage')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Save Codex account-usage stop' })).toBeNull()
    expect(screen.getByText("This run's Codex account-usage stop can no longer be changed.")).toBeTruthy()
  })

  it('drops an error when the run switches', async () => {
    mockClient(async () => {
      throw new Error('boom')
    })
    const { rerender } = render(<CodexAccountUsageStopControl runId="run-1" setting={notConfigured} editable />)
    fireEvent.change(input(), { target: { value: '5' } })
    fireEvent.click(saveButton())
    await screen.findByRole('alert')

    rerender(<CodexAccountUsageStopControl runId="run-2" setting={notConfigured} editable />)

    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('does not write or refresh a replacement setting from a stale save completion', async () => {
    let release: (value: unknown) => void = () => undefined
    mockClient(() => new Promise((resolve) => (release = resolve)))
    const onSaved = vi.fn().mockResolvedValue(false)
    const { rerender } = render(<CodexAccountUsageStopControl runId="run-1" setting={notConfigured} editable onSaved={onSaved} />)
    fireEvent.change(input(), { target: { value: '5' } })
    fireEvent.click(saveButton())
    await waitFor(() => expect(input().disabled).toBe(true))

    // The authoritative setting is replaced while the save is still pending.
    rerender(<CodexAccountUsageStopControl runId="run-1" setting={configured(33)} editable onSaved={onSaved} />)
    expect(input().value).toBe('33')

    release({})
    await new Promise((resolve) => setTimeout(resolve, 20))

    expect(onSaved).not.toHaveBeenCalled()
    expect(input().value).toBe('33')
    expect(screen.getByText('Current run setting: 33% used')).toBeTruthy()
    expect(screen.queryByText(/could not be refreshed/)).toBeNull()
  })

  describe('owner identity (run plus authoritative setting) without a parent key', () => {
    const pending = () => {
      const calls: { resolve: () => void; reject: (reason: unknown) => void }[] = []
      const client = mockClient(() => new Promise((resolve, reject) => calls.push({ resolve: () => resolve({}), reject })))
      return { calls, client }
    }
    const view = (setting: CodexAccountUsageStopResponse, onSaved?: () => Promise<boolean>) => (
      <CodexAccountUsageStopControl runId="run-1" setting={setting} editable onSaved={onSaved} />
    )

    it('re-enables a replacement setting of the same run and shows no error from the rejected old request', async () => {
      const { calls } = pending()
      const { rerender } = render(view(configured(70)))
      fireEvent.change(input(), { target: { value: '80' } })
      fireEvent.click(saveButton())
      await waitFor(() => expect(input().disabled).toBe(true))

      rerender(view(configured(90)))
      expect(input().value).toBe('90')
      expect(input().disabled).toBe(false)
      expect(saveButton().disabled).toBe(false)

      calls[0].reject(new Error('boom'))
      await new Promise((resolve) => setTimeout(resolve, 20))
      expect(screen.queryByRole('alert')).toBeNull()
      expect(input().disabled).toBe(false)
    })

    it('does not let an obsolete resolution release or refresh a replacement request', async () => {
      const { calls, client } = pending()
      const onSaved = vi.fn().mockResolvedValue(true)
      const { rerender } = render(view(configured(70), onSaved))
      fireEvent.change(input(), { target: { value: '80' } })
      fireEvent.click(saveButton())
      await waitFor(() => expect(input().disabled).toBe(true))
      rerender(view(configured(90), onSaved))
      fireEvent.change(input(), { target: { value: '95' } })
      fireEvent.click(saveButton())
      await waitFor(() => expect(client).toHaveBeenCalledTimes(2))
      expect(input().disabled).toBe(true)

      calls[0].resolve()
      await new Promise((resolve) => setTimeout(resolve, 20))
      expect(input().disabled).toBe(true)
      expect(onSaved).not.toHaveBeenCalled()
    })

    it('treats A -> B -> A as a new owner that inherits no pending state or error', async () => {
      const { calls } = pending()
      const { rerender } = render(view(configured(70)))
      fireEvent.change(input(), { target: { value: '80' } })
      fireEvent.click(saveButton())
      await waitFor(() => expect(input().disabled).toBe(true))
      rerender(view(configured(90)))
      rerender(view(configured(70)))
      expect(input().disabled).toBe(false)

      calls[0].reject(new Error('boom'))
      await new Promise((resolve) => setTimeout(resolve, 20))
      expect(screen.queryByRole('alert')).toBeNull()
      expect(input().disabled).toBe(false)
    })

    it('ignores a late resolution after unmount without a state-update warning', async () => {
      const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => undefined)
      const { calls } = pending()
      const onSaved = vi.fn().mockResolvedValue(true)
      const { unmount } = render(view(configured(70), onSaved))
      fireEvent.change(input(), { target: { value: '80' } })
      fireEvent.click(saveButton())
      await waitFor(() => expect(input().disabled).toBe(true))
      unmount()

      calls[0].resolve()
      await new Promise((resolve) => setTimeout(resolve, 20))
      expect(onSaved).not.toHaveBeenCalled()
      expect(errorSpy).not.toHaveBeenCalled()
      errorSpy.mockRestore()
    })

    it('still blocks a duplicate click on the same owner', async () => {
      const { client } = pending()
      render(view(configured(70)))
      fireEvent.change(input(), { target: { value: '80' } })
      fireEvent.click(saveButton())
      fireEvent.click(saveButton())
      await waitFor(() => expect(input().disabled).toBe(true))
      expect(client).toHaveBeenCalledTimes(1)
    })
  })
})
