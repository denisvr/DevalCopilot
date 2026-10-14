import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  gitHubBranchObservationClient,
  gitHubCiObservationClient,
  gitHubObservationTargetClient,
  gitHubRepositoryIdentityBindingClient,
  gitHubRepositoryObservationClient,
} from '../../../api/clients'
import type { GetGitHubCiObservationResponse, GetGitHubObservationTargetResponse } from '../../../api/clients'
import type { ObservationScope, ObservationSource } from '../gitHubObservationTarget'
import { GitHubObservationTargetPanel } from './GitHubObservationTargetPanel'
import { GitHubCiObservationPanel } from './GitHubCiObservationPanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  gitHubObservationTargetClient: vi.fn(),
  gitHubCiObservationClient: vi.fn(),
  gitHubBranchObservationClient: vi.fn(),
  gitHubRepositoryObservationClient: vi.fn(),
  gitHubRepositoryIdentityBindingClient: vi.fn(),
}))

const SHA = 'c'.repeat(40)
const OTHER = 'd'.repeat(40)
const NODE_ID = 'R_kgDOExample01'
const AT = new Date('2026-10-12T20:00:00.000Z')
const BOUND_OBSERVED = new Date('2026-10-12T20:10:00.000Z')
const BOUND_RECORDED = new Date('2026-10-12T20:11:00.000Z')
const OBSERVED = new Date('2026-10-12T21:00:00.000Z')
const source: ObservationSource = { runId: 'run-a', operationId: 'op-a', commitSha: SHA, branchName: 'devalcopilot/workspace/abc/1' }
const target: ObservationScope = {
  id: 'target-1',
  eventId: 'event-1',
  host: 'github.com',
  owner: 'octo-cat',
  repository: 'hello.world',
  recordedAtUtc: AT,
}

const wireSource = { operationId: source.operationId, commitSha: source.commitSha, branchName: source.branchName }
const wireTarget = { ...target }
const wireBinding = {
  id: 'binding-1',
  eventId: 'binding-event-1',
  repositoryNodeId: NODE_ID,
  owner: 'octo-cat',
  repository: 'hello.world',
  observedAtUtc: BOUND_OBSERVED,
  recordedAtUtc: BOUND_RECORDED,
}
const scope = { runId: source.runId, source: wireSource, target: wireTarget }
const withBinding = { ...scope, binding: wireBinding }
const observation = (overrides: Record<string, unknown> = {}) => ({
  repositoryNodeId: NODE_ID,
  owner: 'OCTO-cat',
  repository: 'hello.world',
  observedAtUtc: OBSERVED,
  ref: { branchName: source.branchName, commitSha: SHA },
  rollupState: null,
  ...overrides,
})
const reported = (state: string) =>
  ({ ...withBinding, status: 'StatusReported', observation: observation({ rollupState: state }) }) as unknown as GetGitHubCiObservationResponse
const withStatus = (status: string, extra: Record<string, unknown> = {}) =>
  ({ runId: source.runId, status, ...extra }) as unknown as GetGitHubCiObservationResponse

function install(read: () => Promise<GetGitHubCiObservationResponse>) {
  const getGitHubCiObservation = vi.fn(read)
  vi.mocked(gitHubCiObservationClient).mockReturnValue(
    { getGitHubCiObservation } as unknown as ReturnType<typeof gitHubCiObservationClient>,
  )
  return getGitHubCiObservation
}

async function click(element: HTMLElement) {
  await act(async () => {
    fireEvent.click(element)
  })
}

const region = () => screen.getByRole('region', { name: 'GitHub commit checks' })
const show = () => within(region()).getByRole('button', { name: 'Show GitHub commit checks' })
const check = () => within(region()).getByRole('button', { name: /^(Check|Recheck) GitHub commit checks$/ })
const close = () => within(region()).getByRole('button', { name: 'Close GitHub commit checks' })

describe('GitHubCiObservationPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    cleanup()
  })

  it('is collapsed with one Show action and reads nothing on mount or while waiting', () => {
    vi.useFakeTimers()
    const get = install(() => Promise.resolve(reported('SUCCESS')))

    render(<GitHubCiObservationPanel source={source} target={target} />)
    act(() => {
      vi.advanceTimersByTime(10 * 60 * 1000)
    })
    vi.useRealTimers()

    expect(within(region()).getAllByRole('button')).toHaveLength(1)
    expect(show()).toBeTruthy()
    expect(get).not.toHaveBeenCalled()
  })

  it('opening shows the exact scope, the stored-authentication note and the fixed disclosure but reads nothing', async () => {
    const get = install(() => Promise.resolve(reported('SUCCESS')))
    render(<GitHubCiObservationPanel source={source} target={target} />)

    await click(show())

    const text = region().textContent ?? ''
    expect(text).toContain('Recorded branch' + source.branchName)
    expect(text).toContain('Recorded local commit' + SHA)
    expect(text).toContain('Recorded owner' + 'octo-cat')
    expect(text).toContain('Recorded repository' + 'hello.world')
    expect(text).toMatch(/default stored authentication/)
    expect(text).toMatch(/point-in-time aggregate for this commit does not establish complete check or workflow evidence/)
    expect(text).toMatch(/required-check approval, permission or publication authority/)
    expect(text).toContain('Not checked.')
    expect(check().textContent).toBe('Check GitHub commit checks')
    expect(get).not.toHaveBeenCalled()
  })

  it.each(['ERROR', 'EXPECTED', 'FAILURE', 'PENDING', 'SUCCESS'])(
    'shows the literal %s state, the bound ID, the observed ref and commit and the UTC time as inert text',
    async (state) => {
      const get = install(() => Promise.resolve(reported(state)))
      render(<GitHubCiObservationPanel source={source} target={target} />)
      await click(show())

      await click(check())

      expect(get).toHaveBeenCalledTimes(1)
      expect(get).toHaveBeenCalledWith('run-a')
      const result = region().querySelector('.dc-github-ci-result')!
      expect(result.getAttribute('data-status')).toBe('StatusReported')
      expect(result.textContent).toContain('GitHub check state' + state)
      expect(result.textContent).toContain('Bound repository ID' + NODE_ID)
      expect(result.textContent).toContain('GitHub branch' + source.branchName)
      expect(result.textContent).toContain('GitHub commit' + SHA)
      expect(result.textContent).toContain('OCTO-cat')
      expect(result.textContent).toContain('2026-10-12T21:00:00.000Z')
      expect(region().textContent).toMatch(/point-in-time aggregate for this commit/)
      expect(result.textContent).not.toMatch(/\b(passed|approved|ready|green)\b/i)
      expect(region().querySelector('a, input, textarea, [contenteditable]')).toBeNull()
      expect(check().textContent).toBe('Recheck GitHub commit checks')
    },
  )

  it('withholds the state for a different commit and says so, even though nothing else changed', async () => {
    install(() =>
      Promise.resolve({
        ...withBinding,
        status: 'CommitDifferent',
        observation: observation({ ref: { branchName: source.branchName, commitSha: OTHER } }),
      } as unknown as GetGitHubCiObservationResponse),
    )
    render(<GitHubCiObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())

    const result = region().querySelector('.dc-github-ci-result')!
    expect(result.getAttribute('data-status')).toBe('CommitDifferent')
    expect(result.textContent).toMatch(/points at a different commit than the recorded local commit/)
    expect(result.textContent).toMatch(/no check state for the recorded commit is shown/)
    expect(result.textContent).toContain('GitHub commit' + OTHER)
    expect(result.textContent).not.toContain('GitHub check state')
  })

  it('shows no summary returned as exactly that, never as no checks', async () => {
    install(() => Promise.resolve({ ...withBinding, status: 'NoStatusReported', observation: observation() } as unknown as GetGitHubCiObservationResponse))
    render(<GitHubCiObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())

    const result = region().querySelector('.dc-github-ci-result')!
    expect(result.getAttribute('data-status')).toBe('NoStatusReported')
    expect(result.textContent).toMatch(/returned no check summary for the recorded commit/)
    expect(result.textContent).toMatch(/does not mean that no checks exist/)
    expect(result.textContent).not.toContain('GitHub check state')
    expect(result.textContent).toContain('GitHub commit' + SHA)
  })

  it('shows a ref that was not returned without a commit or state', async () => {
    install(() =>
      Promise.resolve({ ...withBinding, status: 'RefNotReturned', observation: observation({ ref: null }) } as unknown as GetGitHubCiObservationResponse),
    )
    render(<GitHubCiObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())

    const result = region().querySelector('.dc-github-ci-result')!
    expect(result.getAttribute('data-status')).toBe('RefNotReturned')
    expect(result.textContent).toMatch(/returned no ref for this branch in this observation/)
    expect(result.textContent).toMatch(/does not show that the branch does not exist/)
    expect(result.textContent).not.toContain('GitHub commit')
    expect(result.textContent).not.toContain('GitHub check state')
  })

  it('shows an identity change without any remote identity, ref or state', async () => {
    install(() => Promise.resolve(withStatus('IdentityChanged', withBinding)))
    render(<GitHubCiObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())

    const text = region().textContent ?? ''
    expect(text).toMatch(/differs from the repository ID bound for this recorded target/)
    expect(text).toContain('Bound repository ID' + NODE_ID)
    expect(region().querySelector('.dc-github-ci-result')).toBeNull()
    expect(text).not.toContain('GitHub check state')
    expect(text).not.toContain('GitHub branch')
    expect(text).not.toContain('GitHub commit' + SHA)
  })

  it.each([
    ['CliUnavailable', /not currently available as a vetted direct executable/],
    ['Unknown', /Unknown: no usable answer was returned/],
  ])('shows the fixed %s statement with the bound identity and no observation', async (status, pattern) => {
    install(() => Promise.resolve(withStatus(status, withBinding)))
    render(<GitHubCiObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())

    expect(region().textContent).toMatch(pattern)
    expect(region().querySelector('.dc-github-ci-result')).toBeNull()
    expect(region().textContent).not.toContain('GitHub check state')
  })

  it('shows binding not recorded as exactly that and offers no binding action', async () => {
    install(() => Promise.resolve(withStatus('BindingNotRecorded', scope)))
    render(<GitHubCiObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())

    expect(region().textContent).toMatch(/No repository identity is bound for this recorded target, so nothing was checked/)
    expect(within(region()).getAllByRole('button')).toHaveLength(2)
  })

  it.each([
    ['NotRecorded', /no longer reports a recorded GitHub observation target/],
    ['Unavailable', /Unavailable: the recorded facts/],
  ])('shows the fixed %s statement for an answer without any fact', async (status, pattern) => {
    install(() => Promise.resolve(withStatus(status)))
    render(<GitHubCiObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())

    expect(region().textContent).toMatch(pattern)
  })

  it('refuses an answer that is not coherent for this scope, never partially showing it', async () => {
    const get = install(() => Promise.resolve(reported('PASSED')))
    render(<GitHubCiObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())
    expect(within(region()).getByRole('alert').textContent).toMatch(/not coherent for this recorded target/)
    expect(region().textContent).not.toContain('PASSED')

    get.mockImplementation(() =>
      Promise.resolve({ ...reported('SUCCESS'), observation: observation({ repositoryNodeId: 'R_other', rollupState: 'SUCCESS' }) } as unknown as GetGitHubCiObservationResponse),
    )
    await click(check())
    expect(within(region()).getByRole('alert').textContent).toMatch(/not coherent/)
    expect(region().textContent).not.toContain('R_other')

    get.mockImplementation(() => Promise.reject(new Error('network')))
    await click(check())
    expect(within(region()).getByRole('alert').textContent).toBe('The GitHub commit checks could not be read.')
    expect(region().textContent).not.toContain('GitHub check state')
    expect(get).toHaveBeenCalledTimes(3)
  })

  it('disables the check while a read is pending, hides the earlier observation, and closing then reopening starts unchecked', async () => {
    let resolve!: (value: GetGitHubCiObservationResponse) => void
    let calls = 0
    const get = install(() => (++calls === 1 ? Promise.resolve(reported('FAILURE')) : new Promise((res) => (resolve = res))))
    render(<GitHubCiObservationPanel source={source} target={target} />)
    await click(show())
    await click(check())
    expect(region().textContent).toContain('GitHub check state' + 'FAILURE')

    await click(check())

    expect((check() as HTMLButtonElement).disabled).toBe(true)
    expect(region().textContent).toContain('Querying GitHub')
    expect(region().textContent).not.toContain('FAILURE')
    await click(close())
    await act(async () => resolve(reported('SUCCESS')))
    await click(show())

    expect(region().textContent).toContain('Not checked.')
    expect(region().textContent).not.toContain('SUCCESS')
    expect(get).toHaveBeenCalledTimes(2)
  })
})

describe('the commit checks region beside an Available target', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    cleanup()
  })

  const targetReading = (state: string) =>
    ({
      runId: source.runId,
      state,
      source: wireSource,
      target: state === 'Available' ? wireTarget : undefined,
    }) as unknown as GetGitHubObservationTargetResponse

  function installTarget(state: string) {
    const getGitHubObservationTarget = vi.fn(() => Promise.resolve(targetReading(state)))
    vi.mocked(gitHubObservationTargetClient).mockReturnValue(
      { getGitHubObservationTarget } as unknown as ReturnType<typeof gitHubObservationTargetClient>,
    )
  }

  it('appears only for an Available target, collapsed, and neither loading the target nor showing it reads the checks or any sibling', async () => {
    installTarget('Available')
    const get = install(() => Promise.resolve(reported('PENDING')))
    const identity = vi.fn(() => Promise.reject(new Error('must not be read')))
    const branch = vi.fn(() => Promise.reject(new Error('must not be read')))
    const binding = vi.fn(() => Promise.reject(new Error('must not be read')))
    vi.mocked(gitHubRepositoryObservationClient).mockReturnValue({ getGitHubRepositoryObservation: identity } as unknown as ReturnType<typeof gitHubRepositoryObservationClient>)
    vi.mocked(gitHubBranchObservationClient).mockReturnValue({ getGitHubBranchObservation: branch } as unknown as ReturnType<typeof gitHubBranchObservationClient>)
    vi.mocked(gitHubRepositoryIdentityBindingClient).mockReturnValue({ getGitHubRepositoryIdentityBinding: binding } as unknown as ReturnType<typeof gitHubRepositoryIdentityBindingClient>)
    render(<GitHubObservationTargetPanel source={source} />)

    expect(screen.queryByRole('region', { name: 'GitHub commit checks' })).toBeNull()
    await click(screen.getByRole('button', { name: 'Show GitHub observation target' }))
    expect(screen.queryByRole('region', { name: 'GitHub commit checks' })).toBeNull()
    await click(screen.getByRole('button', { name: 'Load GitHub observation target' }))

    expect(within(region()).getAllByRole('button')).toHaveLength(1)
    expect(show()).toBeTruthy()
    expect(get).not.toHaveBeenCalled()

    await click(show())
    await click(check())
    expect(get).toHaveBeenCalledTimes(1)
    expect(region().textContent).toContain('GitHub check state' + 'PENDING')
    expect(identity).not.toHaveBeenCalled()
    expect(branch).not.toHaveBeenCalled()
    expect(binding).not.toHaveBeenCalled()
  })

  it('does not appear for a NotRecorded or Unavailable target', async () => {
    for (const state of ['NotRecorded', 'Unavailable']) {
      installTarget(state)
      install(() => Promise.resolve(reported('SUCCESS')))
      render(<GitHubObservationTargetPanel source={source} />)
      await click(screen.getByRole('button', { name: 'Show GitHub observation target' }))
      await click(screen.getByRole('button', { name: 'Load GitHub observation target' }))

      expect(screen.queryByRole('region', { name: 'GitHub commit checks' })).toBeNull()
      cleanup()
    }
  })

  it('ends the checks lifetime when the target is closed or reloaded and never reads on reopening', async () => {
    installTarget('Available')
    const get = install(() => Promise.resolve(reported('SUCCESS')))
    render(<GitHubObservationTargetPanel source={source} />)
    await click(screen.getByRole('button', { name: 'Show GitHub observation target' }))
    await click(screen.getByRole('button', { name: 'Load GitHub observation target' }))
    await click(show())
    await click(check())

    await click(screen.getByRole('button', { name: 'Reload GitHub observation target' }))
    expect(within(region()).getAllByRole('button')).toHaveLength(1)
    expect(region().textContent).not.toContain('SUCCESS')
    await click(screen.getByRole('button', { name: 'Close GitHub observation target' }))
    await click(screen.getByRole('button', { name: 'Show GitHub observation target' }))
    await click(screen.getByRole('button', { name: 'Load GitHub observation target' }))

    expect(within(region()).getAllByRole('button')).toHaveLength(1)
    expect(region().textContent).not.toContain('SUCCESS')
    expect(get).toHaveBeenCalledTimes(1)
  })
})
