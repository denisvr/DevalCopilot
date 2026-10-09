// @vitest-environment jsdom
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { abandonManualRunClient, manualRunAbandonmentClient } from '../../../api/clients'
import {
  AbandonManualRunResponse,
  ApiException,
  GetManualRunAbandonmentResponse,
  ManualRunAbandonmentResponse,
} from '../../../api/generated/api-client'
import { AbandonRunPanel } from './AbandonRunPanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  abandonManualRunClient: vi.fn(),
  manualRunAbandonmentClient: vi.fn(),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

const eligible = () => new GetManualRunAbandonmentResponse({ eligible: true, latestEventSequence: 5 })

const notEligible = (refusalCode: string) => new GetManualRunAbandonmentResponse({ eligible: false, refusalCode, latestEventSequence: 5 })

const abandoned = (reason = 'Superseded by a smaller change') =>
  new GetManualRunAbandonmentResponse({
    eligible: false,
    refusalCode: 'run_abandonment.already_abandoned',
    latestEventSequence: 6,
    abandonment: new ManualRunAbandonmentResponse({ reason, abandonedAtUtc: new Date('2026-10-08T12:34:56Z') }),
  })

function installStatus(read: (runId: string) => Promise<GetManualRunAbandonmentResponse>) {
  const getManualRunAbandonment = vi.fn(read)
  vi.mocked(manualRunAbandonmentClient).mockReturnValue({ getManualRunAbandonment } as unknown as ReturnType<typeof manualRunAbandonmentClient>)
  return getManualRunAbandonment
}

function installPost(post: () => Promise<unknown>) {
  const abandonManualRun = vi.fn(post)
  vi.mocked(abandonManualRunClient).mockReturnValue({ abandonManualRun } as unknown as ReturnType<typeof abandonManualRunClient>)
  return abandonManualRun
}

const problem = (status: number, code?: string) =>
  new ApiException('raw server text', status, JSON.stringify({ errors: [{ code }] }), {}, null)

const region = () => screen.getByRole('region', { name: 'Abandon run' })
const textbox = () => screen.getByLabelText('Reason') as HTMLTextAreaElement
const button = () => screen.getByRole('button', { name: 'Abandon run' }) as HTMLButtonElement
const type = (value: string) => fireEvent.change(textbox(), { target: { value } })

async function renderSettled(status = eligible(), props: { onSaved?: () => unknown; onProjectChanged?: () => void } = {}) {
  installStatus(() => Promise.resolve(status))
  const view = render(<AbandonRunPanel runId="run-1" latestSequence={5} {...props} />)
  await waitFor(() => expect(screen.queryByText('Reading the abandonment status…')).not.toBeInTheDocument())
  return view
}

describe('AbandonRunPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('eligible form', () => {
    it('shows the labelled form that explains it is not a completion, keeps history and cancels nothing', async () => {
      await renderSettled()

      expect(within(region()).getByRole('heading', { name: 'Abandon run' })).toBeInTheDocument()
      const text = region().textContent ?? ''
      expect(text).toMatch(/not a successful completion/)
      expect(text).toMatch(/kept exactly as they are/)
      expect(text).toMatch(/Nothing is cancelled, repaired, released or deleted/)
      expect(text).toMatch(/normal checks and approvals/)
      expect(text).toMatch(/refused while an attempt, verification or local commit/)
    })

    it('enables the button only for a valid, non-blank reason', async () => {
      await renderSettled()
      expect(button()).toBeDisabled()

      type('   ')
      expect(button()).toBeDisabled()
      type('No longer needed')
      expect(button()).toBeEnabled()
      type('')
      expect(button()).toBeDisabled()
    })

    it.each([
      ['control characters', 'Reas\u0001on', /control or invisible formatting characters/],
      ['an over-long reason', 'x'.repeat(2049), /at most 2048 bytes/],
    ])('explains and withholds submit for %s', async (_name, draft, copy) => {
      await renderSettled()

      type(draft)

      expect(button()).toBeDisabled()
      expect(within(region()).getByText(copy)).toBeInTheDocument()
      expect(textbox()).toHaveAttribute('aria-invalid', 'true')
    })

    it('posts the normalized reason once, refreshes the project list and the cockpit, and shows the recorded abandonment', async () => {
      const get = installStatus(() => Promise.resolve(eligible()))
      const post = installPost(() => Promise.resolve(new AbandonManualRunResponse({ runId: 'run-1', reason: 'Superseded' })))
      const onSaved = vi.fn(async () => true)
      const onProjectChanged = vi.fn()
      render(<AbandonRunPanel runId="run-1" latestSequence={5} onSaved={onSaved} onProjectChanged={onProjectChanged} />)
      await waitFor(() => expect(button()).toBeInTheDocument())
      type('  Superseded\r\nby a smaller change  ')
      await waitFor(() => expect(button()).toBeEnabled())
      get.mockResolvedValue(abandoned('Superseded\nby a smaller change'))

      fireEvent.click(button())

      await waitFor(() => expect(post).toHaveBeenCalledTimes(1))
      const [runId, body] = post.mock.calls[0] as unknown as [string, Record<string, string>]
      expect(runId).toBe('run-1')
      expect(body).toEqual({ reason: 'Superseded\nby a smaller change' })
      await waitFor(() => expect(screen.queryByLabelText('Reason')).not.toBeInTheDocument())
      expect(onSaved).toHaveBeenCalledTimes(1)
      expect(onProjectChanged).toHaveBeenCalledTimes(1)
      const text = region().textContent ?? ''
      expect(text).toMatch(/Abandoned/)
      expect(text).toMatch(/not completed/)
      expect(text).toContain('Superseded\nby a smaller change')
      expect(text).toContain('2026-10-08T12:34:56.000Z')
    })

    it('disables the form while the request is pending and ignores a second click', async () => {
      const pending = deferred<unknown>()
      await renderSettled()
      const post = installPost(() => pending.promise)
      type('Reason')

      fireEvent.click(button())
      await waitFor(() => expect(textbox()).toBeDisabled())
      expect(button()).toBeDisabled()
      fireEvent.click(button())

      expect(post).toHaveBeenCalledTimes(1)
      await act(async () => pending.resolve(new AbandonManualRunResponse({})))
    })

    it('shows fixed copy for a refusal, keeps the draft and never shows the server text', async () => {
      await renderSettled()
      installPost(() => Promise.reject(problem(409, 'run_abandonment.local_commit_open')))
      type('Reason')

      fireEvent.click(button())

      const alert = await screen.findByRole('alert')
      expect(alert.textContent).toMatch(/never used to override an ambiguous local delivery/)
      expect(alert.textContent).not.toContain('raw server text')
      expect(textbox().value).toBe('Reason')
    })

    it('reconciles an unknown outcome only by reading the status, without resubmitting, and shows an abandonment that was in fact recorded', async () => {
      const get = installStatus(() => Promise.resolve(eligible()))
      render(<AbandonRunPanel runId="run-1" latestSequence={5} />)
      await waitFor(() => expect(button()).toBeInTheDocument())
      const post = installPost(() => Promise.reject(new TypeError('Failed to fetch')))
      type('Reason')
      get.mockResolvedValue(abandoned('Reason'))

      fireEvent.click(button())

      await waitFor(() => expect(screen.queryByLabelText('Reason')).not.toBeInTheDocument())
      expect(region().textContent).toMatch(/Abandoned/)
      expect(region().textContent).toContain('Reason')
      await new Promise((resolve) => setTimeout(resolve, 20))
      expect(post).toHaveBeenCalledTimes(1)
    })

    it('keeps the form and a fixed unknown-outcome message when the reconciliation shows nothing was recorded', async () => {
      const get = installStatus(() => Promise.resolve(eligible()))
      render(<AbandonRunPanel runId="run-1" latestSequence={5} />)
      await waitFor(() => expect(button()).toBeInTheDocument())
      const post = installPost(() => Promise.reject(problem(503)))
      type('Reason')

      fireEvent.click(button())

      const alert = await screen.findByRole('alert')
      expect(alert.textContent).toBe('The request outcome was unknown; the recorded status is shown.')
      await waitFor(() => expect(get.mock.calls.length).toBeGreaterThanOrEqual(2))
      await new Promise((resolve) => setTimeout(resolve, 20))
      expect(post).toHaveBeenCalledTimes(1)
      expect(textbox().value).toBe('Reason')
    })

    it('does not report a recorded abandonment as failed when the follow-up status read fails', async () => {
      const get = installStatus(() => Promise.resolve(eligible()))
      installPost(() => Promise.resolve(new AbandonManualRunResponse({})))
      render(<AbandonRunPanel runId="run-1" latestSequence={5} onSaved={async () => false} />)
      await waitFor(() => expect(button()).toBeInTheDocument())
      type('Reason')
      get.mockRejectedValue(new Error('read failed'))

      fireEvent.click(button())

      await waitFor(() => expect(within(region()).getByRole('alert').textContent).toMatch(/status could not be read/))
      expect(region().textContent).not.toMatch(/outcome was unknown|could not be abandoned/)
      expect(screen.queryByLabelText('Reason')).not.toBeInTheDocument()
    })
  })

  describe('status settlement', () => {
    it('withholds the form while the first read is pending', () => {
      installStatus(() => new Promise(() => undefined))
      render(<AbandonRunPanel runId="run-1" latestSequence={5} />)

      expect(within(region()).getByText('Reading the abandonment status…')).toBeInTheDocument()
      expect(screen.queryByLabelText('Reason')).not.toBeInTheDocument()
    })

    it('shows no form after a failed read, with a safe message', async () => {
      installStatus(() => Promise.reject(new Error('internal path C:\\secret')))
      render(<AbandonRunPanel runId="run-1" latestSequence={5} />)

      const alert = await screen.findByRole('alert')

      expect(alert.textContent).toMatch(/cannot be abandoned now/)
      expect(alert.textContent).not.toContain('secret')
      expect(screen.queryByLabelText('Reason')).not.toBeInTheDocument()
    })

    it('keeps the draft but disables submit while a refresh is pending, and again after it settles', async () => {
      const get = installStatus(() => Promise.resolve(eligible()))
      const view = render(<AbandonRunPanel runId="run-1" latestSequence={5} />)
      await waitFor(() => expect(button()).toBeInTheDocument())
      type('Reason')
      await waitFor(() => expect(button()).toBeEnabled())

      const refresh = deferred<GetManualRunAbandonmentResponse>()
      get.mockReturnValueOnce(refresh.promise)
      view.rerender(<AbandonRunPanel runId="run-1" latestSequence={6} />)

      expect(button()).toBeDisabled()
      expect(region().textContent).toMatch(/being refreshed/)
      expect(textbox().value).toBe('Reason')
      await act(async () => refresh.resolve(eligible()))
      await waitFor(() => expect(button()).toBeEnabled())
    })

    it('withholds submit and the form after a failed refresh, and a later success starts with an empty draft', async () => {
      const get = installStatus(() => Promise.resolve(eligible()))
      const view = render(<AbandonRunPanel runId="run-1" latestSequence={5} />)
      await waitFor(() => expect(button()).toBeInTheDocument())
      type('Reason')

      get.mockRejectedValueOnce(new Error('boom'))
      view.rerender(<AbandonRunPanel runId="run-1" latestSequence={6} />)

      await screen.findByRole('alert')
      expect(screen.queryByRole('button', { name: 'Abandon run' })).not.toBeInTheDocument()

      view.rerender(<AbandonRunPanel runId="run-1" latestSequence={7} />)
      await waitFor(() => expect(textbox()).toBeInTheDocument())
      expect(textbox().value).toBe('')
      expect(button()).toBeDisabled()
    })
  })

  describe('not eligible', () => {
    it.each([
      ['run_abandonment.active_attempt', /attempt of this project is still active/],
      ['run_abandonment.active_verification', /verification execution of this project is still active/],
      ['run_abandonment.local_commit_open', /never used to override an ambiguous local delivery/],
      ['run_abandonment.workspace_busy', /being prepared or committed/],
      ['run_abandonment.run_not_abandonable', /already ended/],
      ['run_abandonment.run_not_manual', /Only a manual Agent run/],
      ['run_abandonment.abandonment_incoherent', /not coherent/],
    ])('explains %s with fixed copy and offers no form', async (code, copy) => {
      await renderSettled(notEligible(code))

      expect(within(region()).getByText(copy)).toBeInTheDocument()
      expect(screen.queryByLabelText('Reason')).not.toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Abandon run' })).not.toBeInTheDocument()
    })

    it('never echoes an unknown refusal code', async () => {
      await renderSettled(notEligible('run_abandonment.brand_new_reason'))

      expect(region().textContent).not.toContain('brand_new_reason')
    })
  })

  describe('recorded abandonment', () => {
    it('shows the persisted reason with its line breaks, the UTC time and the not-completed note, and offers no form', async () => {
      await renderSettled(abandoned('Line one\nLine two'))

      const text = region().textContent ?? ''
      expect(text).toContain('Line one\nLine two')
      expect(text).toContain('2026-10-08T12:34:56.000Z')
      expect(text).toMatch(/not completed/)
      expect(text).toMatch(/new work still needs normal checks/)
      expect(text).not.toMatch(/successfully completed|Completed/)
      expect(screen.queryByLabelText('Reason')).not.toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Abandon run' })).not.toBeInTheDocument()
    })

    it('renders the reason as inert text and never as markup', async () => {
      await renderSettled(abandoned('<img src=x onerror=alert(1)><b>bold</b>'))

      expect(region().querySelector('img')).toBeNull()
      expect(region().querySelector('b')).toBeNull()
      expect(region().textContent).toContain('<img src=x onerror=alert(1)><b>bold</b>')
    })

    it('says a refresh is pending while the recorded abandonment is being re-read', async () => {
      const get = installStatus(() => Promise.resolve(abandoned()))
      const view = render(<AbandonRunPanel runId="run-1" latestSequence={5} />)
      await waitFor(() => expect(region().textContent).toMatch(/Superseded/))

      const refresh = deferred<GetManualRunAbandonmentResponse>()
      get.mockReturnValueOnce(refresh.promise)
      view.rerender(<AbandonRunPanel runId="run-1" latestSequence={6} />)

      expect(region().textContent).toMatch(/being refreshed/)
      await act(async () => refresh.resolve(abandoned()))
    })
  })

  describe('ownership', () => {
    it('discards the draft and starts fresh when the run changes, including A to B to A', async () => {
      installStatus(() => Promise.resolve(eligible()))
      const view = render(<AbandonRunPanel key="run-a" runId="run-a" latestSequence={5} />)
      await waitFor(() => expect(button()).toBeInTheDocument())
      type('Draft for A')

      view.rerender(<AbandonRunPanel runId="run-b" latestSequence={5} />)
      await waitFor(() => expect(textbox().value).toBe(''))
      type('Draft for B')
      view.rerender(<AbandonRunPanel runId="run-a" latestSequence={5} />)

      await waitFor(() => expect(textbox().value).toBe(''))
      expect(button()).toBeDisabled()
    })

    it('discards the draft and the shown error when a re-read makes the run ineligible and then eligible again', async () => {
      const get = installStatus(() => Promise.resolve(eligible()))
      installPost(() => Promise.reject(problem(409, 'run_abandonment.reason_conflict')))
      const view = render(<AbandonRunPanel runId="run-1" latestSequence={5} />)
      await waitFor(() => expect(button()).toBeInTheDocument())
      type('First draft')
      fireEvent.click(button())
      await screen.findByRole('alert')

      get.mockResolvedValue(notEligible('run_abandonment.active_attempt'))
      view.rerender(<AbandonRunPanel runId="run-1" latestSequence={6} />)
      await waitFor(() => expect(screen.queryByLabelText('Reason')).not.toBeInTheDocument())
      get.mockResolvedValue(eligible())
      view.rerender(<AbandonRunPanel runId="run-1" latestSequence={7} />)

      await waitFor(() => expect(textbox().value).toBe(''))
      expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    })

    it('ignores a request completion after the run was replaced and shows nothing for the replacement', async () => {
      installStatus(() => Promise.resolve(eligible()))
      const pending = deferred<unknown>()
      installPost(() => pending.promise)
      const onSaved = vi.fn()
      const view = render(<AbandonRunPanel runId="run-a" latestSequence={5} onSaved={onSaved} />)
      await waitFor(() => expect(button()).toBeInTheDocument())
      type('Reason')
      fireEvent.click(button())

      view.rerender(<AbandonRunPanel runId="run-b" latestSequence={5} onSaved={onSaved} />)
      await waitFor(() => expect(textbox().value).toBe(''))
      await act(async () => pending.reject(problem(409, 'run_abandonment.reason_conflict')))

      expect(screen.queryByRole('alert')).not.toBeInTheDocument()
      expect(onSaved).not.toHaveBeenCalled()
    })

    it('keeps an accepted abandonment real when its form lifetime was replaced before the response: the status and project list still refresh, the cockpit refresh does not', async () => {
      const get = installStatus(() => Promise.resolve(eligible()))
      const pending = deferred<unknown>()
      installPost(() => pending.promise)
      const onSaved = vi.fn()
      const onProjectChanged = vi.fn()
      const view = render(<AbandonRunPanel runId="run-1" latestSequence={5} onSaved={onSaved} onProjectChanged={onProjectChanged} />)
      await waitFor(() => expect(button()).toBeInTheDocument())
      type('Reason')
      fireEvent.click(button())
      get.mockResolvedValue(notEligible('run_abandonment.active_attempt'))
      view.rerender(<AbandonRunPanel runId="run-1" latestSequence={6} onSaved={onSaved} onProjectChanged={onProjectChanged} />)
      await waitFor(() => expect(screen.queryByLabelText('Reason')).not.toBeInTheDocument())
      get.mockResolvedValue(abandoned('Reason'))

      await act(async () => pending.resolve(new AbandonManualRunResponse({})))

      await waitFor(() => expect(region().textContent).toMatch(/Abandoned/))
      expect(onProjectChanged).toHaveBeenCalledTimes(1)
      expect(onSaved).not.toHaveBeenCalled()
      expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    })

    it('clears a shown request error when the draft is edited', async () => {
      await renderSettled()
      installPost(() => Promise.reject(problem(404)))
      type('Reason')
      fireEvent.click(button())
      await screen.findByRole('alert')

      type('Reason edited')

      expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    })

    it('clears a shown request error even when an edit leaves the very same text', async () => {
      await renderSettled()
      installPost(() => Promise.reject(problem(404)))
      type('Reason')
      fireEvent.click(button())
      await screen.findByRole('alert')

      fireEvent.change(textbox(), { target: { value: 'Reason ' } })
      fireEvent.change(textbox(), { target: { value: 'Reason' } })

      expect(screen.queryByRole('alert')).not.toBeInTheDocument()
      expect(textbox().value).toBe('Reason')
    })

    it('does nothing after unmount', async () => {
      installStatus(() => Promise.resolve(eligible()))
      const pending = deferred<unknown>()
      installPost(() => pending.promise)
      const onSaved = vi.fn()
      const view = render(<AbandonRunPanel runId="run-1" latestSequence={5} onSaved={onSaved} />)
      await waitFor(() => expect(button()).toBeInTheDocument())
      type('Reason')
      fireEvent.click(button())
      view.unmount()

      await act(async () => pending.resolve(new AbandonManualRunResponse({})))

      expect(onSaved).not.toHaveBeenCalled()
    })
  })
})
