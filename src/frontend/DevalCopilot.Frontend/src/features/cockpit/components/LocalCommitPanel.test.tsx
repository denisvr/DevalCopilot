// @vitest-environment jsdom
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { localCommitStatusClient, requestLocalCommitClient } from '../../../api/clients'
import { ApiException, GetLocalCommitStatusResponse, LocalCommitOperationResponse } from '../../../api/generated/api-client'
import { LocalCommitPanel } from './LocalCommitPanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  localCommitStatusClient: vi.fn(),
  requestLocalCommitClient: vi.fn(),
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

const eligible = (marker = 'a') =>
  new GetLocalCommitStatusResponse({
    eligible: true,
    checkpointId: `checkpoint-${marker}`,
    checkpointNumber: 3,
    codeReviewAttemptId: `review-${marker}`,
    humanCheckpointReviewId: `human-${marker}`,
    latestEventSequence: 5,
  })

const notEligible = (refusalCode: string) => new GetLocalCommitStatusResponse({ eligible: false, refusalCode, latestEventSequence: 5 })

const recorded = (operation: Partial<LocalCommitOperationResponse>) =>
  new GetLocalCommitStatusResponse({
    eligible: false,
    refusalCode: 'local_commit.operation_exists',
    latestEventSequence: 6,
    operation: new LocalCommitOperationResponse({
      operationId: 'operation-1',
      checkpointNumber: 3,
      branchName: 'devalcopilot/run-1',
      parentCommitSha: 'p'.repeat(40),
      treeSha: 't'.repeat(40),
      changedPathCount: 2,
      ...operation,
    }),
  })

function installStatus(read: (runId: string) => Promise<GetLocalCommitStatusResponse>) {
  const getLocalCommitStatus = vi.fn(read)
  vi.mocked(localCommitStatusClient).mockReturnValue({ getLocalCommitStatus } as unknown as ReturnType<typeof localCommitStatusClient>)
  return getLocalCommitStatus
}

function installPost(post: () => Promise<unknown>) {
  const requestLocalCommit = vi.fn(post)
  vi.mocked(requestLocalCommitClient).mockReturnValue({ requestLocalCommit } as unknown as ReturnType<typeof requestLocalCommitClient>)
  return requestLocalCommit
}

const region = () => screen.getByRole('region', { name: 'Local commit' })
const textbox = () => screen.getByLabelText('Commit message') as HTMLTextAreaElement
const button = () => screen.getByRole('button', { name: 'Commit locally' }) as HTMLButtonElement
const type = (value: string) => fireEvent.change(textbox(), { target: { value } })

async function renderSettled(status = eligible(), props: { onSaved?: () => unknown } = {}) {
  installStatus(() => Promise.resolve(status))
  const view = render(<LocalCommitPanel runId="run-1" latestSequence={5} {...props} />)
  await waitFor(() => expect(screen.queryByText('Reading the local-commit status…')).not.toBeInTheDocument())
  return view
}

describe('LocalCommitPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('eligible form', () => {
    it('shows the labelled form with the local-only, unsigned, hook-free, irreversible and exact-bytes explanation', async () => {
      await renderSettled()

      expect(within(region()).getByRole('heading', { name: 'Local commit' })).toBeInTheDocument()
      const text = region().textContent ?? ''
      expect(text).toMatch(/local only: nothing is pushed/)
      expect(text).toMatch(/unsigned/)
      expect(text).toMatch(/without running any Git hooks/)
      expect(text).toMatch(/configured Git author/)
      expect(text).toMatch(/irreversible for this run/)
      expect(text).toMatch(/refused rather than converted/)
      expect(text).toMatch(/line-ending normalization/)
    })

    it('enables the button only for a valid non-empty message', async () => {
      await renderSettled()
      expect(button()).toBeDisabled()

      type('   ')
      expect(button()).toBeDisabled()
      type('Add the local commit panel')
      expect(button()).toBeEnabled()
      type('')
      expect(button()).toBeDisabled()
    })

    it.each([
      ['control characters', 'Sub\u0001ject', /control characters/],
      ['an over-long message', 'x'.repeat(2049), /at most 2048 bytes/],
    ])('explains and withholds submit for %s', async (_name, draft, copy) => {
      await renderSettled()

      type(draft)

      expect(button()).toBeDisabled()
      expect(within(region()).getByText(copy)).toBeInTheDocument()
      expect(textbox()).toHaveAttribute('aria-invalid', 'true')
    })

    it('posts the trimmed message with the settled identity and a stable operation id, then shows the recorded operation', async () => {
      const get = installStatus(() => Promise.resolve(eligible()))
      const post = installPost(() => Promise.resolve(new LocalCommitOperationResponse({ status: 'Prepared' })))
      const onSaved = vi.fn(async () => true)
      render(<LocalCommitPanel runId="run-1" latestSequence={5} onSaved={onSaved} />)
      await waitFor(() => expect(button()).toBeInTheDocument())
      type('  Subject line  ')
      await waitFor(() => expect(button()).toBeEnabled())
      get.mockResolvedValue(recorded({ status: 'Prepared' }))

      fireEvent.click(button())

      await waitFor(() => expect(post).toHaveBeenCalledTimes(1))
      const [runId, body] = post.mock.calls[0] as unknown as [string, Record<string, string>]
      expect(runId).toBe('run-1')
      expect(body).toMatchObject({
        checkpointId: 'checkpoint-a',
        codeReviewAttemptId: 'review-a',
        humanCheckpointReviewId: 'human-a',
        message: 'Subject line',
      })
      expect(body.operationId).toMatch(/^[0-9a-f-]{36}$/)
      await waitFor(() => expect(screen.queryByLabelText('Commit message')).not.toBeInTheDocument())
      expect(onSaved).toHaveBeenCalledTimes(1)
      expect(within(region()).getByText(/Prepared\./)).toBeInTheDocument()
    })

    it('disables the form while the request is pending and ignores a second click', async () => {
      const pending = deferred<unknown>()
      await renderSettled()
      const post = installPost(() => pending.promise)
      type('Subject')

      fireEvent.click(button())
      await waitFor(() => expect(textbox()).toBeDisabled())
      expect(button()).toBeDisabled()
      fireEvent.click(button())

      expect(post).toHaveBeenCalledTimes(1)
      await act(async () => pending.resolve(new LocalCommitOperationResponse({})))
    })

    it('shows fixed copy for a refusal and keeps the draft', async () => {
      await renderSettled()
      installPost(() =>
        Promise.reject(new ApiException('raw server text', 409, JSON.stringify({ errors: [{ code: 'local_commit.refused.conversion_refused' }] }), {}, null)),
      )
      type('Subject')

      fireEvent.click(button())

      const alert = await screen.findByRole('alert')
      expect(alert.textContent).toMatch(/line-ending normalization/)
      expect(alert.textContent).not.toContain('raw server text')
      expect(textbox().value).toBe('Subject')
    })

    it('reconciles an unknown outcome through the status read without resubmitting, and reuses the operation id on a manual retry', async () => {
      const get = await (async () => {
        const read = installStatus(() => Promise.resolve(eligible()))
        render(<LocalCommitPanel runId="run-1" latestSequence={5} />)
        await waitFor(() => expect(button()).toBeInTheDocument())
        return read
      })()
      const post = installPost(() => Promise.reject(new TypeError('Failed to fetch')))
      type('Subject')

      fireEvent.click(button())

      const alert = await screen.findByRole('alert')
      expect(alert.textContent).toBe('The request outcome was unknown; the recorded status is shown.')
      await waitFor(() => expect(get.mock.calls.length).toBeGreaterThanOrEqual(2))
      await new Promise((resolve) => setTimeout(resolve, 20))
      expect(post).toHaveBeenCalledTimes(1)

      await waitFor(() => expect(button()).toBeEnabled())
      type('Subject')
      fireEvent.click(button())
      await waitFor(() => expect(post).toHaveBeenCalledTimes(2))
      const ids = post.mock.calls.map((call) => (call as unknown as [string, Record<string, string>])[1].operationId)
      expect(ids[0]).toBe(ids[1])
    })

    it('does not report an admitted request as failed when the follow-up status read fails', async () => {
      const get = installStatus(() => Promise.resolve(eligible()))
      installPost(() => Promise.resolve(new LocalCommitOperationResponse({ status: 'Prepared' })))
      render(<LocalCommitPanel runId="run-1" latestSequence={5} onSaved={async () => false} />)
      await waitFor(() => expect(button()).toBeInTheDocument())
      type('Subject')
      get.mockRejectedValue(new Error('read failed'))

      fireEvent.click(button())

      await waitFor(() => expect(within(region()).getByRole('alert').textContent).toMatch(/status could not be read/))
      expect(region().textContent).not.toMatch(/outcome was unknown|could not be requested/)
      expect(screen.queryByLabelText('Commit message')).not.toBeInTheDocument()
    })
  })

  describe('status settlement', () => {
    it('withholds the form while the first read is pending', () => {
      installStatus(() => new Promise(() => undefined))
      render(<LocalCommitPanel runId="run-1" latestSequence={5} />)

      expect(within(region()).getByText('Reading the local-commit status…')).toBeInTheDocument()
      expect(screen.queryByLabelText('Commit message')).not.toBeInTheDocument()
    })

    it('shows no form after a failed read, with a safe message', async () => {
      installStatus(() => Promise.reject(new Error('internal path C:\\secret')))
      render(<LocalCommitPanel runId="run-1" latestSequence={5} />)

      const alert = await screen.findByRole('alert')

      expect(alert.textContent).toMatch(/cannot be requested now/)
      expect(alert.textContent).not.toContain('secret')
      expect(screen.queryByLabelText('Commit message')).not.toBeInTheDocument()
    })

    it('keeps the draft but disables submit while a refresh is pending, and again after it settles', async () => {
      const get = installStatus(() => Promise.resolve(eligible()))
      const view = render(<LocalCommitPanel runId="run-1" latestSequence={5} />)
      await waitFor(() => expect(button()).toBeInTheDocument())
      type('Subject')
      await waitFor(() => expect(button()).toBeEnabled())

      const refresh = deferred<GetLocalCommitStatusResponse>()
      get.mockReturnValueOnce(refresh.promise)
      view.rerender(<LocalCommitPanel runId="run-1" latestSequence={6} />)

      expect(button()).toBeDisabled()
      expect(region().textContent).toMatch(/being refreshed/)
      expect(textbox().value).toBe('Subject')
      await act(async () => refresh.resolve(eligible()))
      await waitFor(() => expect(button()).toBeEnabled())
    })

    it('withholds submit and the form after a failed refresh', async () => {
      const get = installStatus(() => Promise.resolve(eligible()))
      const view = render(<LocalCommitPanel runId="run-1" latestSequence={5} />)
      await waitFor(() => expect(button()).toBeInTheDocument())
      type('Subject')

      get.mockRejectedValueOnce(new Error('boom'))
      view.rerender(<LocalCommitPanel runId="run-1" latestSequence={6} />)

      await screen.findByRole('alert')
      expect(screen.queryByRole('button', { name: 'Commit locally' })).not.toBeInTheDocument()
    })
  })

  describe('not eligible', () => {
    it.each([
      ['local_commit.human_decision_not_approved', /new checkpoint/],
      ['local_commit.checkpoint_not_current', /new checkpoint/],
      ['local_commit.verification_not_current', /passed execution/],
      ['local_commit.operation_exists', /already has a recorded/],
      ['local_commit.refused.conversion_refused', /line-ending normalization/],
    ])('explains %s with fixed copy and offers no form', async (code, copy) => {
      await renderSettled(notEligible(code))

      expect(within(region()).getByText(copy)).toBeInTheDocument()
      expect(screen.queryByLabelText('Commit message')).not.toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Commit locally' })).not.toBeInTheDocument()
    })

    it('never echoes an unknown refusal code', async () => {
      await renderSettled(notEligible('local_commit.brand_new_reason'))

      expect(region().textContent).not.toContain('brand_new_reason')
    })

    it('offers no form when an eligible status lacks an approval identity', async () => {
      await renderSettled(new GetLocalCommitStatusResponse({ eligible: true, checkpointId: 'checkpoint-a', latestEventSequence: 5 }))

      expect(screen.queryByLabelText('Commit message')).not.toBeInTheDocument()
    })
  })

  describe('recorded operation card', () => {
    const sha = 'c'.repeat(40)

    it('shows the local commit SHA, parent, tree and branch only as local evidence for a Completed operation', async () => {
      await renderSettled(recorded({ status: 'Completed', commitSha: sha }))

      const text = region().textContent ?? ''
      expect(text).toMatch(/Local commit only — not pushed/)
      expect(text).toContain(sha)
      expect(text).toContain('p'.repeat(40))
      expect(text).toContain('t'.repeat(40))
      expect(text).toContain('devalcopilot/run-1')
      expect(text).toMatch(/#3/)
      expect(screen.queryByLabelText('Commit message')).not.toBeInTheDocument()
    })

    it.each(['Prepared', 'Executing', 'Failed', 'Interrupted', 'NeedsAttention'])('never shows a commit SHA for a %s operation', async (status) => {
      await renderSettled(recorded({ status, commitSha: sha }))

      expect(region().textContent).not.toContain(sha)
      expect(region().textContent).toContain(status)
      expect(region().textContent).not.toMatch(/pushed to|clean workspace|reliab/i)
    })

    it('explains that a NeedsAttention outcome is ambiguous and is not retried', async () => {
      await renderSettled(recorded({ status: 'NeedsAttention', outcomeReasonCode: 'local_commit.git_unprovable' }))

      const text = region().textContent ?? ''
      expect(text).toMatch(/ambiguous/)
      expect(text).toMatch(/nothing was retried/)
      expect(text).toMatch(/Git state could not be proven/)
    })

    it('maps a Failed refusal reason to fixed copy and not the raw code', async () => {
      await renderSettled(recorded({ status: 'Failed', outcomeReasonCode: 'local_commit.refused.conversion_refused' }))

      expect(region().textContent).toMatch(/line-ending normalization/)
      expect(region().textContent).not.toContain('conversion_refused')
    })

    it('does not claim a push for a Completed operation and notes that it says nothing about the rest of the workspace', async () => {
      await renderSettled(recorded({ status: 'Completed', commitSha: sha }))

      expect(region().textContent).toMatch(/does not describe the state of the rest of the workspace/)
    })
  })

  describe('identity ownership', () => {
    it('discards the draft and starts fresh when the run changes, including A to B to A', async () => {
      installStatus((runId) => Promise.resolve(eligible(runId)))
      const view = render(<LocalCommitPanel key="run-a" runId="run-a" latestSequence={5} />)
      await waitFor(() => expect(button()).toBeInTheDocument())
      type('Draft for A')

      view.rerender(<LocalCommitPanel runId="run-b" latestSequence={5} />)
      await waitFor(() => expect(textbox().value).toBe(''))
      type('Draft for B')
      view.rerender(<LocalCommitPanel runId="run-a" latestSequence={5} />)

      await waitFor(() => expect(textbox().value).toBe(''))
      expect(button()).toBeDisabled()
    })

    it('discards the draft when a re-read names a new checkpoint identity, and uses a new operation id', async () => {
      const get = installStatus(() => Promise.resolve(eligible('a')))
      const post = installPost(() => Promise.reject(new TypeError('Failed to fetch')))
      const view = render(<LocalCommitPanel runId="run-1" latestSequence={5} />)
      await waitFor(() => expect(button()).toBeInTheDocument())
      type('First draft')
      fireEvent.click(button())
      await screen.findByRole('alert')

      get.mockResolvedValue(eligible('b'))
      view.rerender(<LocalCommitPanel runId="run-1" latestSequence={6} />)

      await waitFor(() => expect(textbox().value).toBe(''))
      expect(screen.queryByRole('alert')).not.toBeInTheDocument()
      type('Second draft')
      await waitFor(() => expect(button()).toBeEnabled())
      post.mockClear()
      fireEvent.click(button())
      await waitFor(() => expect(post).toHaveBeenCalledTimes(1))
      expect((post.mock.calls[0] as unknown as [string, Record<string, string>])[1]).toMatchObject({ checkpointId: 'checkpoint-b' })
    })

    it('ignores a request completion after the run was replaced and shows nothing for the replacement', async () => {
      installStatus((runId) => Promise.resolve(eligible(runId)))
      const pending = deferred<unknown>()
      installPost(() => pending.promise)
      const onSaved = vi.fn()
      const view = render(<LocalCommitPanel runId="run-a" latestSequence={5} onSaved={onSaved} />)
      await waitFor(() => expect(button()).toBeInTheDocument())
      type('Subject')
      fireEvent.click(button())

      view.rerender(<LocalCommitPanel runId="run-b" latestSequence={5} onSaved={onSaved} />)
      await waitFor(() => expect(textbox().value).toBe(''))
      await act(async () => pending.reject(new ApiException('raw', 409, JSON.stringify({ errors: [{ code: 'local_commit.operation_conflict' }] }), {}, null)))

      expect(screen.queryByRole('alert')).not.toBeInTheDocument()
      expect(onSaved).not.toHaveBeenCalled()
    })

    it('clears a shown request error when the draft is edited', async () => {
      await renderSettled()
      installPost(() => Promise.reject(new ApiException('raw', 404, '{}', {}, null)))
      type('Subject')
      fireEvent.click(button())
      await screen.findByRole('alert')

      type('Subject edited')

      expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    })

    it('does nothing after unmount', async () => {
      installStatus(() => Promise.resolve(eligible()))
      const pending = deferred<unknown>()
      installPost(() => pending.promise)
      const onSaved = vi.fn()
      const view = render(<LocalCommitPanel runId="run-1" latestSequence={5} onSaved={onSaved} />)
      await waitFor(() => expect(button()).toBeInTheDocument())
      type('Subject')
      fireEvent.click(button())
      view.unmount()

      await act(async () => pending.resolve(new LocalCommitOperationResponse({})))

      expect(onSaved).not.toHaveBeenCalled()
    })
  })
})
