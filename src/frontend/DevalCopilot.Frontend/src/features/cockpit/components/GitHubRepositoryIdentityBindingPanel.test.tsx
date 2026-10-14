import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  bindGitHubRepositoryIdentityClient,
  gitHubRepositoryIdentityBindingClient,
  gitHubRepositoryObservationClient,
} from '../../../api/clients'
import type {
  BindGitHubRepositoryIdentityResponse,
  GetGitHubRepositoryIdentityBindingResponse,
  GetGitHubRepositoryObservationResponse,
} from '../../../api/clients'
import type { ObservationScope, ObservationSource } from '../gitHubObservationTarget'
import { GitHubRepositoryIdentityBindingPanel } from './GitHubRepositoryIdentityBindingPanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  bindGitHubRepositoryIdentityClient: vi.fn(),
  gitHubRepositoryIdentityBindingClient: vi.fn(),
  gitHubRepositoryObservationClient: vi.fn(),
}))

const SHA = 'c'.repeat(40)
const TARGET_AT = new Date('2026-10-13T12:00:00.000Z')
const OBSERVED = new Date('2026-10-13T12:01:00.000Z')
const BOUND = new Date('2026-10-13T12:02:00.000Z')
const source: ObservationSource = { runId: 'run-a', operationId: 'op-a', commitSha: SHA, branchName: 'devalcopilot/workspace/abc/1' }
const target: ObservationScope = {
  id: 'target-1',
  eventId: 'event-1',
  host: 'github.com',
  owner: 'octo-cat',
  repository: 'hello.world',
  recordedAtUtc: TARGET_AT,
}

const wireSource = { operationId: source.operationId, commitSha: source.commitSha, branchName: source.branchName }
const wireTarget = { ...target }
const wireBinding = (overrides: Record<string, unknown> = {}) => ({
  id: 'binding-1',
  eventId: 'binding-event-1',
  repositoryNodeId: 'R_kgDOExample01',
  owner: 'octo-cat',
  repository: 'hello.world',
  observedAtUtc: OBSERVED,
  recordedAtUtc: BOUND,
  ...overrides,
})
const notBound = () =>
  ({ runId: 'run-a', state: 'NotRecorded', source: wireSource, target: wireTarget, binding: null }) as unknown as GetGitHubRepositoryIdentityBindingResponse
const alreadyBound = () =>
  ({ runId: 'run-a', state: 'Available', source: wireSource, target: wireTarget, binding: wireBinding() }) as unknown as GetGitHubRepositoryIdentityBindingResponse
const observed = (nodeId = 'R_kgDOExample01') =>
  ({
    runId: 'run-a',
    status: 'Observed',
    source: wireSource,
    target: wireTarget,
    observation: { repositoryNodeId: nodeId, owner: 'OCTO-cat', repository: 'hello.world', observedAtUtc: OBSERVED },
  }) as unknown as GetGitHubRepositoryObservationResponse
const posted = () =>
  ({ runId: 'run-a', source: wireSource, target: wireTarget, binding: wireBinding() }) as unknown as BindGitHubRepositoryIdentityResponse

function install(
  overrides: Partial<{
    read: () => Promise<GetGitHubRepositoryIdentityBindingResponse>
    observe: () => Promise<GetGitHubRepositoryObservationResponse>
    bind: () => Promise<BindGitHubRepositoryIdentityResponse>
  }> = {},
) {
  const read = vi.fn(overrides.read ?? (() => Promise.resolve(notBound())))
  const observe = vi.fn(overrides.observe ?? (() => Promise.resolve(observed())))
  const bind = vi.fn(overrides.bind ?? (() => Promise.resolve(posted())))
  vi.mocked(gitHubRepositoryIdentityBindingClient).mockReturnValue(
    { getGitHubRepositoryIdentityBinding: read } as unknown as ReturnType<typeof gitHubRepositoryIdentityBindingClient>,
  )
  vi.mocked(gitHubRepositoryObservationClient).mockReturnValue(
    { getGitHubRepositoryObservation: observe } as unknown as ReturnType<typeof gitHubRepositoryObservationClient>,
  )
  vi.mocked(bindGitHubRepositoryIdentityClient).mockReturnValue(
    { bindGitHubRepositoryIdentity: bind } as unknown as ReturnType<typeof bindGitHubRepositoryIdentityClient>,
  )
  return { read, observe, bind }
}

async function click(element: HTMLElement) {
  await act(async () => {
    fireEvent.click(element)
  })
}

const region = () => screen.getByRole('region', { name: 'GitHub identity binding' })
const button = (name: string | RegExp) => within(region()).getByRole('button', { name })

describe('GitHubRepositoryIdentityBindingPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    cleanup()
  })

  it('is collapsed with one Show action and calls nothing on mount, while waiting or on opening', async () => {
    vi.useFakeTimers()
    const mocks = install()
    render(<GitHubRepositoryIdentityBindingPanel source={source} target={target} />)
    act(() => {
      vi.advanceTimersByTime(10 * 60 * 1000)
    })
    vi.useRealTimers()

    expect(within(region()).getAllByRole('button')).toHaveLength(1)
    await click(button('Show GitHub identity binding'))

    const text = region().textContent ?? ''
    expect(text).toContain(source.branchName)
    expect(text).toContain(source.commitSha)
    expect(text).toContain('Not loaded.')
    expect(within(region()).queryByRole('button', { name: 'Bind this repository identity' })).toBeNull()
    expect(mocks.read).not.toHaveBeenCalled()
    expect(mocks.observe).not.toHaveBeenCalled()
    expect(mocks.bind).not.toHaveBeenCalled()
  })

  it('offers Bind only after a not-bound read and a candidate, shows the exact facts and the disclosure first, then binds the candidate', async () => {
    const mocks = install()
    render(<GitHubRepositoryIdentityBindingPanel source={source} target={target} />)
    await click(button('Show GitHub identity binding'))

    await click(button('Load GitHub identity binding'))
    expect(region().textContent).toContain('No repository identity is bound for this recorded target yet.')
    expect(within(region()).queryByRole('button', { name: 'Bind this repository identity' })?.hasAttribute('disabled') ?? true).toBe(true)
    await click(button('Check candidate identity'))

    const review = region().querySelector('.dc-github-binding-review')?.textContent ?? ''
    expect(review).toContain('github.com / octo-cat / hello.world')
    expect(review).toContain('R_kgDOExample01')
    expect(review).toContain(source.commitSha)
    expect(review).toContain('immutably')
    expect(review).toContain('historical identity evidence only')
    expect(region().querySelector('.dc-github-binding-observation')?.textContent).toContain('2026-10-13')
    const bind = button('Bind this repository identity')
    expect(bind.hasAttribute('disabled')).toBe(false)
    expect(mocks.bind).not.toHaveBeenCalled()

    await click(bind)

    expect(mocks.bind).toHaveBeenCalledTimes(1)
    expect(region().querySelector('[data-status="Available"]')).not.toBeNull()
    expect(region().querySelector('.dc-github-binding-recorded')?.textContent).toContain('R_kgDOExample01')
    expect(within(region()).queryByRole('button', { name: 'Bind this repository identity' })).toBeNull()
    expect(within(region()).getByRole('button', { name: 'Reload GitHub identity binding' })).toBeTruthy()
  })

  it('shows an existing binding as recorded history and offers no bind', async () => {
    install({ read: () => Promise.resolve(alreadyBound()) })
    render(<GitHubRepositoryIdentityBindingPanel source={source} target={target} />)
    await click(button('Show GitHub identity binding'))

    await click(button('Load GitHub identity binding'))

    const text = region().textContent ?? ''
    expect(text).toContain('immutable')
    expect(text).toContain('not a current check of GitHub')
    expect(text).toContain('R_kgDOExample01')
    expect(within(region()).queryByRole('button', { name: 'Bind this repository identity' })).toBeNull()
  })

  it('shows a refused bind as a fixed alert, disarms binding and requires a reload and a new candidate check', async () => {
    const mocks = install({ bind: () => Promise.reject(new Error('network')) })
    render(<GitHubRepositoryIdentityBindingPanel source={source} target={target} />)
    await click(button('Show GitHub identity binding'))
    await click(button('Load GitHub identity binding'))
    await click(button('Check candidate identity'))

    await click(button('Bind this repository identity'))

    expect(within(region()).getByRole('alert').textContent).toContain('Reload')
    // The earlier read is cleared, so nothing can be bound until an explicit reload and a new candidate check.
    expect(within(region()).queryByRole('button', { name: 'Bind this repository identity' })).toBeNull()
    expect(mocks.bind).toHaveBeenCalledTimes(1)
    await click(button('Load GitHub identity binding'))
    expect(within(region()).queryByRole('button', { name: 'Bind this repository identity' })?.hasAttribute('disabled') ?? true).toBe(true)
    await click(button('Check candidate identity'))
    await click(button('Bind this repository identity'))
    expect(mocks.bind).toHaveBeenCalledTimes(2)
  })

  it.each([
    ['a lost response', () => Promise.reject(new Error('network'))],
    ['an incoherent success', () => Promise.resolve({ ...posted(), binding: wireBinding({ repositoryNodeId: 'R_other' }) } as unknown as BindGitHubRepositoryIdentityResponse)],
  ])('after %s the earlier not-bound statement is gone, the outcome message stays and nothing is requested by itself', async (_name, bindAnswer) => {
    const mocks = install({ bind: bindAnswer })
    render(<GitHubRepositoryIdentityBindingPanel source={source} target={target} />)
    await click(button('Show GitHub identity binding'))
    await click(button('Load GitHub identity binding'))
    await click(button('Check candidate identity'))
    expect(region().textContent).toContain('No repository identity is bound for this recorded target yet.')

    await click(button('Bind this repository identity'))

    expect(region().textContent).not.toContain('No repository identity is bound for this recorded target yet.')
    expect(region().textContent).toContain('Not loaded.')
    expect(within(region()).getByRole('alert').textContent).toContain('Reload')
    expect(mocks.read).toHaveBeenCalledTimes(1)
    expect(mocks.observe).toHaveBeenCalledTimes(1)
    expect(mocks.bind).toHaveBeenCalledTimes(1)
  })

  it('renders hostile text from the host inertly, never as markup or a link', async () => {
    install({ observe: () => Promise.resolve(observed('R_<img src=x onerror=alert(1)>')) })
    render(<GitHubRepositoryIdentityBindingPanel source={source} target={target} />)
    await click(button('Show GitHub identity binding'))
    await click(button('Load GitHub identity binding'))
    await click(button('Check candidate identity'))

    expect(region().querySelector('img')).toBeNull()
    expect(region().querySelector('a')).toBeNull()
    // An identity outside the conservative node ID set is an incoherent answer: nothing is shown and nothing can be bound.
    expect(region().textContent).not.toContain('onerror')
    expect(within(region()).getByRole('alert').textContent).toContain('not coherent')
    expect(button('Bind this repository identity').hasAttribute('disabled')).toBe(true)
  })

  it('closing discards everything and reopening starts unloaded', async () => {
    install()
    render(<GitHubRepositoryIdentityBindingPanel source={source} target={target} />)
    await click(button('Show GitHub identity binding'))
    await click(button('Load GitHub identity binding'))
    await click(button('Check candidate identity'))
    await click(button('Close GitHub identity binding'))

    await click(button('Show GitHub identity binding'))

    expect(region().textContent).toContain('Not loaded.')
    expect(within(region()).queryByRole('button', { name: 'Bind this repository identity' })).toBeNull()
  })
})
