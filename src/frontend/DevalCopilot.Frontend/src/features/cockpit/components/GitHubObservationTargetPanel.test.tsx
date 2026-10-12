import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { gitHubObservationTargetClient, recordGitHubObservationTargetClient } from '../../../api/clients'
import type { GetGitHubObservationTargetResponse, RecordGitHubObservationTargetResponse } from '../../../api/clients'
import { ApiException } from '../../../api/generated/api-client'
import type { ObservationSource } from '../gitHubObservationTarget'
import { GitHubObservationTargetPanel } from './GitHubObservationTargetPanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  gitHubObservationTargetClient: vi.fn(),
  recordGitHubObservationTargetClient: vi.fn(),
}))

const SHA = 'c'.repeat(40)
const AT = new Date('2026-10-12T20:00:00.000Z')
const A: ObservationSource = { runId: 'run-a', operationId: 'op-a', commitSha: SHA, branchName: 'devalcopilot/workspace/abc/1' }
const B: ObservationSource = { runId: 'run-b', operationId: 'op-b', commitSha: 'd'.repeat(40), branchName: 'devalcopilot/workspace/abc/2' }

const wireSource = (source: ObservationSource) => ({ operationId: source.operationId, commitSha: source.commitSha, branchName: source.branchName })
const wireTarget = (owner = 'octo-cat', repository = 'hello.world') => ({
  id: 'target-1',
  eventId: 'event-1',
  host: 'github.com',
  owner,
  repository,
  recordedAtUtc: AT,
})
const reading = (source: ObservationSource, state: string, target?: ReturnType<typeof wireTarget>) =>
  ({
    runId: source.runId,
    state,
    source: state === 'Unavailable' ? undefined : wireSource(source),
    target,
  }) as unknown as GetGitHubObservationTargetResponse
const wrote = (source: ObservationSource, owner = 'octo-cat', repository = 'hello.world') =>
  ({ runId: source.runId, source: wireSource(source), target: wireTarget(owner, repository) }) as unknown as RecordGitHubObservationTargetResponse

function install(
  read: (runId: string) => Promise<GetGitHubObservationTargetResponse>,
  write: (runId: string, request: unknown) => Promise<RecordGitHubObservationTargetResponse> = (runId) =>
    Promise.resolve(wrote(runId === 'run-b' ? B : A)),
) {
  const getGitHubObservationTarget = vi.fn(read)
  const recordGitHubObservationTarget = vi.fn(write)
  vi.mocked(gitHubObservationTargetClient).mockReturnValue(
    { getGitHubObservationTarget } as unknown as ReturnType<typeof gitHubObservationTargetClient>,
  )
  vi.mocked(recordGitHubObservationTargetClient).mockReturnValue(
    { recordGitHubObservationTarget } as unknown as ReturnType<typeof recordGitHubObservationTargetClient>,
  )
  return { get: getGitHubObservationTarget, record: recordGitHubObservationTarget }
}

async function click(element: HTMLElement) {
  await act(async () => {
    fireEvent.click(element)
  })
}

async function type(label: string, value: string) {
  await act(async () => {
    fireEvent.change(screen.getByLabelText(label), { target: { value } })
  })
}

const show = () => screen.getByRole('button', { name: 'Show GitHub observation target' })
const load = () => screen.getByRole('button', { name: /^(Load|Reload) GitHub observation target$/ })
const closeButton = () => screen.getByRole('button', { name: 'Close GitHub observation target' })
const recordButton = () => screen.getByRole('button', { name: 'Record GitHub observation target' }) as HTMLButtonElement

async function openAndLoad() {
  await click(show())
  await click(load())
}

describe('GitHubObservationTargetPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    cleanup()
  })

  it('is collapsed and reads and writes nothing until the owner explicitly acts', async () => {
    const { get, record } = install((id) => Promise.resolve(reading(id === 'run-b' ? B : A, 'NotRecorded')))
    render(<GitHubObservationTargetPanel source={A} />)

    expect(get).not.toHaveBeenCalled()
    expect(screen.queryByLabelText('GitHub owner')).toBeNull()
    await click(show())
    expect(get).not.toHaveBeenCalled()
    expect(screen.getByText('Not loaded.')).not.toBeNull()
    expect(screen.queryByLabelText('GitHub owner')).toBeNull()
    expect(record).not.toHaveBeenCalled()
  })

  it('shows the exact branch and commit and the local-only disclosure, as plain text without any link or copy action', async () => {
    install((id) => Promise.resolve(reading(id === 'run-b' ? B : A, 'NotRecorded')))
    const { container } = render(<GitHubObservationTargetPanel source={A} />)

    await click(show())

    expect(screen.getByText(A.branchName)).not.toBeNull()
    expect(screen.getByText(SHA)).not.toBeNull()
    const note = container.querySelector('.dc-github-target-note')!.textContent!
    for (const phrase of ['local selection', 'not a verified remote identity', 'permission', 'CI result', 'publication authority', 'cannot be changed']) {
      expect(note).toContain(phrase)
    }
    expect(container.querySelectorAll('a, [href]')).toHaveLength(0)
    expect(screen.queryByRole('button', { name: /copy|open|push|publish|verify/i })).toBeNull()
  })

  it('offers blank owner and repository fields after an explicit load, with the record action separate and disabled', async () => {
    install((id) => Promise.resolve(reading(id === 'run-b' ? B : A, 'NotRecorded')))
    render(<GitHubObservationTargetPanel source={A} />)

    await openAndLoad()

    expect((screen.getByLabelText('GitHub owner') as HTMLInputElement).value).toBe('')
    expect((screen.getByLabelText('GitHub repository') as HTMLInputElement).value).toBe('')
    expect(recordButton().disabled).toBe(true)
    expect(screen.getByText('No GitHub observation target is recorded for this local delivery yet.')).not.toBeNull()
    expect(load().textContent).toBe('Reload GitHub observation target')
  })

  it('shows the selection to be recorded before submission, validates without trimming, and records exactly once', async () => {
    const { record } = install((id) => Promise.resolve(reading(id === 'run-b' ? B : A, 'NotRecorded')))
    const { container } = render(<GitHubObservationTargetPanel source={A} />)
    await openAndLoad()

    await type('GitHub owner', ' octo-cat')
    await type('GitHub repository', 'hello.world')
    expect(recordButton().disabled).toBe(true)
    expect(screen.getByLabelText('GitHub owner').getAttribute('aria-invalid')).toBe('true')
    expect(container.querySelector('.dc-github-target-review')).toBeNull()

    await type('GitHub owner', 'octo-cat')
    expect(recordButton().disabled).toBe(false)
    expect(container.querySelector('.dc-github-target-review')!.textContent).toBe(
      `Selection to record: github.com / octo-cat / hello.world for the local commit ${SHA} on the branch ${A.branchName}. It cannot be changed after it is recorded.`,
    )

    await click(recordButton())

    expect(record).toHaveBeenCalledTimes(1)
    expect(record.mock.calls[0][1]).toMatchObject({ operationId: 'op-a', commitSha: SHA, owner: 'octo-cat', repository: 'hello.world' })
    expect(container.querySelector('.dc-github-target-result')!.getAttribute('data-status')).toBe('Available')
    expect(screen.getByText('This GitHub observation target is recorded for this local delivery and is immutable:')).not.toBeNull()
    expect(screen.getByText('octo-cat')).not.toBeNull()
    expect(screen.getByText('hello.world')).not.toBeNull()
    expect(screen.getByText('event-1')).not.toBeNull()
    expect(screen.getByText('2026-10-12T20:00:00.000Z')).not.toBeNull()
    expect(screen.queryByLabelText('GitHub owner')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Record GitHub observation target' })).toBeNull()
  })

  it('sends one request for a duplicate click and for a click in the same batch', async () => {
    const write = vi.fn(() => new Promise<RecordGitHubObservationTargetResponse>(() => undefined))
    install((id) => Promise.resolve(reading(id === 'run-b' ? B : A, 'NotRecorded')), write)
    render(<GitHubObservationTargetPanel source={A} />)
    await openAndLoad()
    await type('GitHub owner', 'octo-cat')
    await type('GitHub repository', 'hello.world')

    await act(async () => {
      fireEvent.click(recordButton())
      fireEvent.click(recordButton())
    })
    await click(recordButton())

    expect(write).toHaveBeenCalledTimes(1)
    expect(screen.getByText('Recording the selection…')).not.toBeNull()
    expect((screen.getByLabelText('GitHub owner') as HTMLInputElement).disabled).toBe(true)
  })

  it('never sends a record click issued after a close in the same batch', async () => {
    const { record } = install((id) => Promise.resolve(reading(id === 'run-b' ? B : A, 'NotRecorded')))
    render(<GitHubObservationTargetPanel source={A} />)
    await openAndLoad()
    await type('GitHub owner', 'octo-cat')
    await type('GitHub repository', 'hello.world')
    const submit = recordButton()

    await act(async () => {
      fireEvent.click(closeButton())
      fireEvent.click(submit)
    })

    expect(record).not.toHaveBeenCalled()
    expect(screen.queryByLabelText('GitHub owner')).toBeNull()
    expect(show()).not.toBeNull()
  })

  it('offers an explicit reload before another submit after an ambiguous recording, and the reload finds the original record', async () => {
    let recorded = false
    const { get, record } = install(
      (id) => Promise.resolve(reading(id === 'run-b' ? B : A, recorded ? 'Available' : 'NotRecorded', recorded ? wireTarget() : undefined)),
      () => {
        recorded = true
        return Promise.reject(new Error('offline'))
      },
    )
    render(<GitHubObservationTargetPanel source={A} />)
    await openAndLoad()
    await type('GitHub owner', 'octo-cat')
    await type('GitHub repository', 'hello.world')

    await click(recordButton())

    expect(screen.getByRole('alert').textContent).toContain('The outcome of recording is unknown')
    expect(screen.getByRole('alert').textContent).toContain('Reload')
    expect(recordButton().disabled).toBe(true)
    expect((screen.getByLabelText('GitHub owner') as HTMLInputElement).disabled).toBe(true)
    await click(recordButton())
    expect(record).toHaveBeenCalledTimes(1)

    await click(load())

    expect(get).toHaveBeenCalledTimes(2)
    expect(screen.queryByRole('alert')).toBeNull()
    expect(screen.getByText('This GitHub observation target is recorded for this local delivery and is immutable:')).not.toBeNull()
    expect(record).toHaveBeenCalledTimes(1)
  })

  it('words a definite refusal differently from an unknown outcome and still requires a reload', async () => {
    const { record } = install(
      (id) => Promise.resolve(reading(id === 'run-b' ? B : A, 'NotRecorded')),
      () => Promise.reject(new ApiException('conflict', 409, '', {}, null)),
    )
    render(<GitHubObservationTargetPanel source={A} />)
    await openAndLoad()
    await type('GitHub owner', 'octo-cat')
    await type('GitHub repository', 'hello.world')

    await click(recordButton())

    expect(screen.getByRole('alert').textContent).toContain('The host refused this selection')
    expect(recordButton().disabled).toBe(true)
    await click(load())
    expect(recordButton().disabled).toBe(false)
    expect(record).toHaveBeenCalledTimes(1)
  })

  it('shows a mismatched or incoherent answer as a failure with none of its content', async () => {
    const { get } = install(() => Promise.resolve(reading(B, 'NotRecorded')))
    render(<GitHubObservationTargetPanel source={A} />)

    await openAndLoad()

    expect(screen.getByRole('alert').textContent).toContain('not coherent for this local delivery')
    expect(screen.queryByLabelText('GitHub owner')).toBeNull()
    get.mockRejectedValueOnce(new Error('offline'))
    await click(load())
    expect(screen.getByRole('alert').textContent).toContain('could not be read')
  })

  it('shows the Unavailable state without any form and a not-recordable state without a form', async () => {
    const { get } = install(() => Promise.resolve(reading(A, 'Unavailable')))
    render(<GitHubObservationTargetPanel source={A} />)

    await openAndLoad()
    expect(screen.getByText(/^Unavailable: the recorded facts/)).not.toBeNull()
    expect(screen.queryByLabelText('GitHub owner')).toBeNull()

    get.mockResolvedValueOnce({ runId: 'run-a', state: 'NotRecorded' } as unknown as GetGitHubObservationTargetResponse)
    await click(load())
    expect(screen.getByText(/cannot currently admit this delivery as a source/)).not.toBeNull()
    expect(screen.queryByLabelText('GitHub owner')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Record GitHub observation target' })).toBeNull()
  })

  it('cannot be opened and read for a delivery without a source, and says so when opened', async () => {
    const { get } = install(() => Promise.resolve(reading(A, 'NotRecorded')))
    render(<GitHubObservationTargetPanel source={null} />)

    expect((show() as HTMLButtonElement).disabled).toBe(true)
    await click(show())
    expect(get).not.toHaveBeenCalled()
  })

  it('starts collapsed, blank and unloaded for another source and for a return to an earlier one, and closing then reopening resets it', async () => {
    const { get } = install((id) => Promise.resolve(reading(id === 'run-b' ? B : A, 'NotRecorded')))
    const view = render(<GitHubObservationTargetPanel source={A} />)
    await openAndLoad()
    await type('GitHub owner', 'octo-cat')

    view.rerender(<GitHubObservationTargetPanel source={B} />)
    expect(screen.queryByLabelText('GitHub owner')).toBeNull()
    view.rerender(<GitHubObservationTargetPanel source={A} />)
    expect(screen.queryByLabelText('GitHub owner')).toBeNull()
    await click(show())
    expect(screen.getByText('Not loaded.')).not.toBeNull()
    await click(load())
    expect((screen.getByLabelText('GitHub owner') as HTMLInputElement).value).toBe('')

    await type('GitHub owner', 'someone')
    await click(closeButton())
    await click(show())
    expect(screen.getByText('Not loaded.')).not.toBeNull()
    await click(load())
    expect((screen.getByLabelText('GitHub owner') as HTMLInputElement).value).toBe('')
    expect(get).toHaveBeenCalledTimes(3)
  })

  it('ignores a late answer of an opening that was closed and reopened', async () => {
    let release!: (value: GetGitHubObservationTargetResponse) => void
    install(() => new Promise<GetGitHubObservationTargetResponse>((resolve) => (release = resolve)))
    render(<GitHubObservationTargetPanel source={A} />)
    await openAndLoad()
    expect(screen.getByText('Reading the recorded GitHub observation target…')).not.toBeNull()

    await click(closeButton())
    await click(show())
    await act(async () => release(reading(A, 'NotRecorded')))

    expect(screen.getByText('Not loaded.')).not.toBeNull()
    expect(screen.queryByLabelText('GitHub owner')).toBeNull()
  })
})
