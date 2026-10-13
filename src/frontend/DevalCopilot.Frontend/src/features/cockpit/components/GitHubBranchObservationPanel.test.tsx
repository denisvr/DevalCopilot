import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { gitHubObservationTargetClient, gitHubBranchObservationClient } from '../../../api/clients'
import type { GetGitHubObservationTargetResponse, GetGitHubBranchObservationResponse } from '../../../api/clients'
import type { ObservationScope, ObservationSource } from '../gitHubObservationTarget'
import { GitHubObservationTargetPanel } from './GitHubObservationTargetPanel'
import { GitHubBranchObservationPanel } from './GitHubBranchObservationPanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  gitHubObservationTargetClient: vi.fn(),
  gitHubBranchObservationClient: vi.fn(),
}))

const SHA = 'c'.repeat(40)
const AT = new Date('2026-10-12T20:00:00.000Z')
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
const base = { runId: source.runId, source: wireSource, target: wireTarget }
const matched = (overrides: Record<string, unknown> = {}) =>
  ({
    ...base,
    status: 'CommitMatched',
    observation: { repositoryNodeId: 'R_kgDOExample01', owner: 'OCTO-cat', repository: 'hello.world', observedAtUtc: OBSERVED, ref: { branchName: source.branchName, commitSha: SHA }, ...overrides },
  }) as unknown as GetGitHubBranchObservationResponse
const withStatus = (status: string, extra: Record<string, unknown> = {}) =>
  ({ runId: source.runId, status, ...extra }) as unknown as GetGitHubBranchObservationResponse

function install(read: () => Promise<GetGitHubBranchObservationResponse>) {
  const getGitHubBranchObservation = vi.fn(read)
  vi.mocked(gitHubBranchObservationClient).mockReturnValue(
    { getGitHubBranchObservation } as unknown as ReturnType<typeof gitHubBranchObservationClient>,
  )
  return getGitHubBranchObservation
}

async function click(element: HTMLElement) {
  await act(async () => {
    fireEvent.click(element)
  })
}

const region = () => screen.getByRole('region', { name: 'GitHub remote branch' })
const show = () => within(region()).getByRole('button', { name: 'Show GitHub remote branch' })
const check = () => within(region()).getByRole('button', { name: /^(Check|Recheck) GitHub remote branch$/ })
const close = () => within(region()).getByRole('button', { name: 'Close GitHub remote branch' })

describe('GitHubBranchObservationPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    cleanup()
  })

  it('is collapsed with one Show action and reads nothing on mount or while waiting', () => {
    vi.useFakeTimers()
    const get = install(() => Promise.resolve(matched()))

    render(<GitHubBranchObservationPanel source={source} target={target} />)
    act(() => {
      vi.advanceTimersByTime(10 * 60 * 1000)
    })
    vi.useRealTimers()

    expect(within(region()).getAllByRole('button')).toHaveLength(1)
    expect(show()).toBeTruthy()
    expect(get).not.toHaveBeenCalled()
  })

  it('opening shows the exact source and recorded target and the stored-authentication disclosure but reads nothing', async () => {
    const get = install(() => Promise.resolve(matched()))
    render(<GitHubBranchObservationPanel source={source} target={target} />)

    await click(show())

    const text = region().textContent ?? ''
    expect(text).toContain('Recorded branch' + source.branchName)
    expect(text).toContain('Recorded local commit' + SHA)
    expect(text).toContain('Recorded ownerocto-cat')
    expect(text).toContain('Recorded repositoryhello.world')
    expect(text).toMatch(/default stored authentication/)
    expect(text).toMatch(/Only the repository ID, its names and the commit that branch points at are read/)
    expect(text).toMatch(/not a CI result, a permission, a durable binding, a publication approval or a publication performed by DevalCopilot/)
    expect(text).toContain('Not checked.')
    expect(check().textContent).toBe('Check GitHub remote branch')
    expect(get).not.toHaveBeenCalled()
  })

  it('checks once per explicit activation and shows the matched branch as inert text', async () => {
    const get = install(() => Promise.resolve(matched()))
    render(<GitHubBranchObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())

    expect(get).toHaveBeenCalledTimes(1)
    expect(get).toHaveBeenCalledWith('run-a')
    const result = region().querySelector('.dc-github-branch-result')!
    expect(result.getAttribute('data-status')).toBe('CommitMatched')
    expect(result.textContent).toContain('R_kgDOExample01')
    expect(result.textContent).toContain('OCTO-cat')
    expect(result.textContent).toContain('2026-10-12T21:00:00.000Z')
    expect(result.textContent).toMatch(/not a CI result, a permission, a durable binding, a publication approval/)
    expect(result.textContent).toContain('GitHub branch' + source.branchName)
    expect(result.textContent).toContain('GitHub commit' + SHA)
    expect(region().querySelector('a, input, textarea, [contenteditable]')).toBeNull()
    expect(check().textContent).toBe('Recheck GitHub remote branch')
    await click(check())
    expect(get).toHaveBeenCalledTimes(2)
  })

  it('distinguishes a different commit and a reference that was not returned, each with its own fixed statement', async () => {
    const OTHER = 'd'.repeat(40)
    const get = install(() => Promise.resolve(matched({ ref: { branchName: source.branchName, commitSha: OTHER } })))
    get.mockImplementationOnce(() =>
      Promise.resolve({ ...matched({ ref: { branchName: source.branchName, commitSha: OTHER } }), status: 'CommitDifferent' } as GetGitHubBranchObservationResponse),
    )
    render(<GitHubBranchObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())
    const different = region().querySelector('.dc-github-branch-result')!
    expect(different.getAttribute('data-status')).toBe('CommitDifferent')
    expect(different.textContent).toMatch(/points at a different commit than the recorded local commit/)
    expect(different.textContent).toContain('GitHub commit' + OTHER)
    expect(different.textContent).not.toMatch(/same commit/)

    get.mockImplementation(() =>
      Promise.resolve({ ...matched({ ref: null }), status: 'RefNotReturned' } as GetGitHubBranchObservationResponse),
    )
    await click(check())
    const notReturned = region().querySelector('.dc-github-branch-result')!
    expect(notReturned.getAttribute('data-status')).toBe('RefNotReturned')
    expect(notReturned.textContent).toMatch(/GitHub returned no ref for this branch in this observation/)
    expect(notReturned.textContent).toMatch(/does not show that the branch does not exist/)
    expect(notReturned.textContent).not.toContain('GitHub commit')
    expect(notReturned.textContent).toContain('R_kgDOExample01')
  })

  it('refuses a match claimed for a different commit and a different branch instead of showing it', async () => {
    const get = install(() => Promise.resolve(matched({ ref: { branchName: source.branchName, commitSha: 'd'.repeat(40) } })))
    render(<GitHubBranchObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())
    expect(within(region()).getByRole('alert').textContent).toMatch(/not coherent for this recorded target/)

    get.mockImplementation(() => Promise.resolve(matched({ ref: { branchName: 'main', commitSha: SHA } })))
    await click(check())
    expect(within(region()).getByRole('alert').textContent).toMatch(/not coherent/)
    expect(region().textContent).not.toContain('GitHub commit')
  })

  it.each([
    ['CliUnavailable', /not currently available as a vetted direct executable/],
    ['Unknown', /Unknown: no usable answer was returned/],
  ])('shows the fixed %s statement and no identity', async (status, pattern) => {
    install(() => Promise.resolve(withStatus(status, base)))
    render(<GitHubBranchObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())

    expect(region().textContent).toMatch(pattern)
    expect(region().querySelector('.dc-github-branch-result')).toBeNull()
    expect(region().textContent).not.toContain('Repository ID')
  })

  it.each([
    ['NotRecorded', /no longer reports a recorded GitHub observation target/],
    ['Unavailable', /Unavailable: the recorded facts/],
  ])('shows the fixed %s statement for an answer without scope', async (status, pattern) => {
    install(() => Promise.resolve(withStatus(status)))
    render(<GitHubBranchObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())

    expect(region().textContent).toMatch(pattern)
  })

  it('refuses a whole answer that does not match this source and target, a foreign name or a failed read', async () => {
    const get = install(() => Promise.resolve(matched({ owner: 'someone-else' })))
    render(<GitHubBranchObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())
    expect(within(region()).getByRole('alert').textContent).toMatch(/not coherent for this recorded target/)
    expect(region().textContent).not.toContain('R_kgDOExample01')

    get.mockImplementation(() => Promise.resolve(matched({ repositoryNodeId: '<script>x</script>' })))
    await click(check())
    expect(within(region()).getByRole('alert').textContent).toMatch(/not coherent/)

    get.mockImplementation(() => Promise.reject(new Error('network')))
    await click(check())
    expect(within(region()).getByRole('alert').textContent).toBe('The GitHub remote branch could not be read.')
    expect(get).toHaveBeenCalledTimes(3)
  })

  it('disables the check while a read is pending and closing then reopening starts unchecked', async () => {
    let resolve!: (value: GetGitHubBranchObservationResponse) => void
    const get = install(() => new Promise((res) => (resolve = res)))
    render(<GitHubBranchObservationPanel source={source} target={target} />)
    await click(show())
    await click(check())

    expect((check() as HTMLButtonElement).disabled).toBe(true)
    expect(region().textContent).toContain('Querying GitHub')
    await click(close())
    await act(async () => resolve(matched()))
    await click(show())

    expect(region().textContent).toContain('Not checked.')
    expect(region().textContent).not.toContain('R_kgDOExample01')
    expect(get).toHaveBeenCalledTimes(1)
  })
})

describe('the remote branch region beside an Available target', () => {
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

  it('appears only for an Available target, collapsed, and loading the target never reads the branch', async () => {
    installTarget('Available')
    const get = install(() => Promise.resolve(matched()))
    render(<GitHubObservationTargetPanel source={source} />)

    expect(screen.queryByRole('region', { name: 'GitHub remote branch' })).toBeNull()
    await click(screen.getByRole('button', { name: 'Show GitHub observation target' }))
    expect(screen.queryByRole('region', { name: 'GitHub remote branch' })).toBeNull()
    await click(screen.getByRole('button', { name: 'Load GitHub observation target' }))

    expect(within(region()).getAllByRole('button')).toHaveLength(1)
    expect(show()).toBeTruthy()
    expect(get).not.toHaveBeenCalled()

    await click(show())
    await click(check())
    expect(get).toHaveBeenCalledTimes(1)
    expect(region().textContent).toContain('R_kgDOExample01')
  })

  it('does not appear for a NotRecorded or Unavailable target', async () => {
    for (const state of ['NotRecorded', 'Unavailable']) {
      installTarget(state)
      install(() => Promise.resolve(matched()))
      render(<GitHubObservationTargetPanel source={source} />)
      await click(screen.getByRole('button', { name: 'Show GitHub observation target' }))
      await click(screen.getByRole('button', { name: 'Load GitHub observation target' }))

      expect(screen.queryByRole('region', { name: 'GitHub remote branch' })).toBeNull()
      cleanup()
    }
  })

  it('ends the branch lifetime when the target is closed or reloaded and never reads on reopening', async () => {
    installTarget('Available')
    const get = install(() => Promise.resolve(matched()))
    render(<GitHubObservationTargetPanel source={source} />)
    await click(screen.getByRole('button', { name: 'Show GitHub observation target' }))
    await click(screen.getByRole('button', { name: 'Load GitHub observation target' }))
    await click(show())
    await click(check())

    await click(screen.getByRole('button', { name: 'Reload GitHub observation target' }))
    expect(within(region()).getAllByRole('button')).toHaveLength(1)
    expect(region().textContent).not.toContain('R_kgDOExample01')
    await click(screen.getByRole('button', { name: 'Close GitHub observation target' }))
    await click(screen.getByRole('button', { name: 'Show GitHub observation target' }))
    await click(screen.getByRole('button', { name: 'Load GitHub observation target' }))

    expect(within(region()).getAllByRole('button')).toHaveLength(1)
    expect(region().textContent).not.toContain('R_kgDOExample01')
    expect(get).toHaveBeenCalledTimes(1)
  })
})
