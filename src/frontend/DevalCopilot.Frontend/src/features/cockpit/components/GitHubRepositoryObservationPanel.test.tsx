import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { gitHubObservationTargetClient, gitHubRepositoryObservationClient } from '../../../api/clients'
import type { GetGitHubObservationTargetResponse, GetGitHubRepositoryObservationResponse } from '../../../api/clients'
import type { ObservationScope, ObservationSource } from '../gitHubObservationTarget'
import { GitHubObservationTargetPanel } from './GitHubObservationTargetPanel'
import { GitHubRepositoryObservationPanel } from './GitHubRepositoryObservationPanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  gitHubObservationTargetClient: vi.fn(),
  gitHubRepositoryObservationClient: vi.fn(),
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
const observed = (overrides: Record<string, unknown> = {}) =>
  ({
    ...base,
    status: 'Observed',
    observation: { repositoryNodeId: 'R_kgDOExample01', owner: 'OCTO-cat', repository: 'hello.world', observedAtUtc: OBSERVED, ...overrides },
  }) as unknown as GetGitHubRepositoryObservationResponse
const withStatus = (status: string, extra: Record<string, unknown> = {}) =>
  ({ runId: source.runId, status, ...extra }) as unknown as GetGitHubRepositoryObservationResponse

function install(read: () => Promise<GetGitHubRepositoryObservationResponse>) {
  const getGitHubRepositoryObservation = vi.fn(read)
  vi.mocked(gitHubRepositoryObservationClient).mockReturnValue(
    { getGitHubRepositoryObservation } as unknown as ReturnType<typeof gitHubRepositoryObservationClient>,
  )
  return getGitHubRepositoryObservation
}

async function click(element: HTMLElement) {
  await act(async () => {
    fireEvent.click(element)
  })
}

const region = () => screen.getByRole('region', { name: 'GitHub repository identity' })
const show = () => within(region()).getByRole('button', { name: 'Show GitHub repository identity' })
const check = () => within(region()).getByRole('button', { name: /^(Check|Recheck) GitHub repository identity$/ })
const close = () => within(region()).getByRole('button', { name: 'Close GitHub repository identity' })

describe('GitHubRepositoryObservationPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    cleanup()
  })

  it('is collapsed with one Show action and reads nothing on mount or while waiting', () => {
    vi.useFakeTimers()
    const get = install(() => Promise.resolve(observed()))

    render(<GitHubRepositoryObservationPanel source={source} target={target} />)
    act(() => {
      vi.advanceTimersByTime(10 * 60 * 1000)
    })
    vi.useRealTimers()

    expect(within(region()).getAllByRole('button')).toHaveLength(1)
    expect(show()).toBeTruthy()
    expect(get).not.toHaveBeenCalled()
  })

  it('opening shows the exact source and recorded target and the stored-authentication disclosure but reads nothing', async () => {
    const get = install(() => Promise.resolve(observed()))
    render(<GitHubRepositoryObservationPanel source={source} target={target} />)

    await click(show())

    const text = region().textContent ?? ''
    expect(text).toContain(source.branchName)
    expect(text).toContain(SHA)
    expect(text).toContain('Recorded ownerocto-cat')
    expect(text).toContain('Recorded repositoryhello.world')
    expect(text).toMatch(/default stored authentication/)
    expect(text).toMatch(/Only the repository ID and its names are read/)
    expect(text).toMatch(/not proof of a remote branch or commit, a permission, a CI result or publication approval/)
    expect(text).toContain('Not checked.')
    expect(check().textContent).toBe('Check GitHub repository identity')
    expect(get).not.toHaveBeenCalled()
  })

  it('checks once per explicit activation and shows the observed identity as inert text', async () => {
    const get = install(() => Promise.resolve(observed()))
    render(<GitHubRepositoryObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())

    expect(get).toHaveBeenCalledTimes(1)
    expect(get).toHaveBeenCalledWith('run-a')
    const result = region().querySelector('.dc-github-identity-result')!
    expect(result.getAttribute('data-status')).toBe('Observed')
    expect(result.textContent).toContain('R_kgDOExample01')
    expect(result.textContent).toContain('OCTO-cat')
    expect(result.textContent).toContain('2026-10-12T21:00:00.000Z')
    expect(result.textContent).toMatch(/point-in-time observation, not a binding/)
    expect(region().querySelector('a, input, textarea, [contenteditable]')).toBeNull()
    expect(check().textContent).toBe('Recheck GitHub repository identity')
    await click(check())
    expect(get).toHaveBeenCalledTimes(2)
  })

  it.each([
    ['CliUnavailable', /not currently available as a vetted direct executable/],
    ['Unknown', /Unknown: no usable identity was returned/],
  ])('shows the fixed %s statement and no identity', async (status, pattern) => {
    install(() => Promise.resolve(withStatus(status, base)))
    render(<GitHubRepositoryObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())

    expect(region().textContent).toMatch(pattern)
    expect(region().querySelector('.dc-github-identity-result')).toBeNull()
    expect(region().textContent).not.toContain('Repository ID')
  })

  it.each([
    ['NotRecorded', /no longer reports a recorded GitHub observation target/],
    ['Unavailable', /Unavailable: the recorded facts/],
  ])('shows the fixed %s statement for an answer without scope', async (status, pattern) => {
    install(() => Promise.resolve(withStatus(status)))
    render(<GitHubRepositoryObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())

    expect(region().textContent).toMatch(pattern)
  })

  it('refuses a whole answer that does not match this source and target, a foreign name or a failed read', async () => {
    const get = install(() => Promise.resolve(observed({ owner: 'someone-else' })))
    render(<GitHubRepositoryObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())
    expect(within(region()).getByRole('alert').textContent).toMatch(/not coherent for this recorded target/)
    expect(region().textContent).not.toContain('R_kgDOExample01')

    get.mockImplementation(() => Promise.resolve(observed({ repositoryNodeId: '<script>x</script>' })))
    await click(check())
    expect(within(region()).getByRole('alert').textContent).toMatch(/not coherent/)

    get.mockImplementation(() => Promise.reject(new Error('network')))
    await click(check())
    expect(within(region()).getByRole('alert').textContent).toBe('The GitHub repository identity could not be read.')
    expect(get).toHaveBeenCalledTimes(3)
  })

  it('disables the check while a read is pending and closing then reopening starts unchecked', async () => {
    let resolve!: (value: GetGitHubRepositoryObservationResponse) => void
    const get = install(() => new Promise((res) => (resolve = res)))
    render(<GitHubRepositoryObservationPanel source={source} target={target} />)
    await click(show())
    await click(check())

    expect((check() as HTMLButtonElement).disabled).toBe(true)
    expect(region().textContent).toContain('Querying GitHub')
    await click(close())
    await act(async () => resolve(observed()))
    await click(show())

    expect(region().textContent).toContain('Not checked.')
    expect(region().textContent).not.toContain('R_kgDOExample01')
    expect(get).toHaveBeenCalledTimes(1)
  })
})

describe('the identity region beside an Available target', () => {
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

  it('appears only for an Available target, collapsed, and loading the target never reads the identity', async () => {
    installTarget('Available')
    const get = install(() => Promise.resolve(observed()))
    render(<GitHubObservationTargetPanel source={source} />)

    expect(screen.queryByRole('region', { name: 'GitHub repository identity' })).toBeNull()
    await click(screen.getByRole('button', { name: 'Show GitHub observation target' }))
    expect(screen.queryByRole('region', { name: 'GitHub repository identity' })).toBeNull()
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
      install(() => Promise.resolve(observed()))
      render(<GitHubObservationTargetPanel source={source} />)
      await click(screen.getByRole('button', { name: 'Show GitHub observation target' }))
      await click(screen.getByRole('button', { name: 'Load GitHub observation target' }))

      expect(screen.queryByRole('region', { name: 'GitHub repository identity' })).toBeNull()
      cleanup()
    }
  })

  it('ends the identity lifetime when the target is closed or reloaded and never reads on reopening', async () => {
    installTarget('Available')
    const get = install(() => Promise.resolve(observed()))
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
