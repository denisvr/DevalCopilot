import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  gitHubBranchObservationClient,
  gitHubCiObservationClient,
  gitHubObservationTargetClient,
  gitHubRepositoryIdentityBindingClient,
  gitHubRepositoryObservationClient,
  gitHubRepositoryPermissionObservationClient,
} from '../../../api/clients'
import type { GetGitHubObservationTargetResponse, GetGitHubRepositoryPermissionObservationResponse } from '../../../api/clients'
import { GetGitHubRepositoryPermissionObservationResponse as GeneratedPermissionResponse } from '../../../api/generated/api-client'
import type { ObservationScope, ObservationSource } from '../gitHubObservationTarget'
import { GitHubObservationTargetPanel } from './GitHubObservationTargetPanel'
import { GitHubRepositoryPermissionPanel } from './GitHubRepositoryPermissionPanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  gitHubObservationTargetClient: vi.fn(),
  gitHubRepositoryPermissionObservationClient: vi.fn(),
  gitHubCiObservationClient: vi.fn(),
  gitHubBranchObservationClient: vi.fn(),
  gitHubRepositoryObservationClient: vi.fn(),
  gitHubRepositoryIdentityBindingClient: vi.fn(),
}))

const SHA = 'c'.repeat(40)
const NODE_ID = 'R_kgDOExample01'
const AT = new Date('2026-10-12T20:00:00.000Z')
const BOUND_OBSERVED = new Date('2026-10-12T20:10:00.000Z')
const BOUND_RECORDED = new Date('2026-10-12T20:11:00.000Z')
const OBSERVED = new Date('2026-10-12T21:00:00.000Z')
const DISCLOSURE = 'Repository access does not establish permission to push this branch.'
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
const observation = (permission: string | null) => ({
  repositoryNodeId: NODE_ID,
  owner: 'OCTO-cat',
  repository: 'hello.world',
  observedAtUtc: OBSERVED,
  viewerPermission: permission,
})
const reported = (permission: string | null) =>
  ({
    ...withBinding,
    status: permission === null ? 'PermissionNotReturned' : 'PermissionReported',
    observation: observation(permission),
  }) as unknown as GetGitHubRepositoryPermissionObservationResponse
const withStatus = (status: string, extra: Record<string, unknown> = {}) =>
  ({ runId: source.runId, status, ...extra }) as unknown as GetGitHubRepositoryPermissionObservationResponse

function install(read: () => Promise<GetGitHubRepositoryPermissionObservationResponse>) {
  const getGitHubRepositoryPermissionObservation = vi.fn(read)
  vi.mocked(gitHubRepositoryPermissionObservationClient).mockReturnValue(
    { getGitHubRepositoryPermissionObservation } as unknown as ReturnType<typeof gitHubRepositoryPermissionObservationClient>,
  )
  return getGitHubRepositoryPermissionObservation
}

async function click(element: HTMLElement) {
  await act(async () => {
    fireEvent.click(element)
  })
}

const region = () => screen.getByRole('region', { name: 'GitHub repository permission' })
const show = () => within(region()).getByRole('button', { name: 'Show GitHub repository permission' })
const check = () => within(region()).getByRole('button', { name: /^(Check|Recheck) GitHub repository permission$/ })
const close = () => within(region()).getByRole('button', { name: 'Close GitHub repository permission' })

describe('GitHubRepositoryPermissionPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    cleanup()
  })

  it('is collapsed with one Show action and reads nothing on mount or while waiting', () => {
    vi.useFakeTimers()
    const get = install(() => Promise.resolve(reported('WRITE')))

    render(<GitHubRepositoryPermissionPanel source={source} target={target} />)
    act(() => {
      vi.advanceTimersByTime(10 * 60 * 1000)
    })
    vi.useRealTimers()

    expect(within(region()).getAllByRole('button')).toHaveLength(1)
    expect(show()).toBeTruthy()
    expect(get).not.toHaveBeenCalled()
  })

  it('opening shows the exact scope, the stored-authentication note and the fixed disclosure but reads nothing', async () => {
    const get = install(() => Promise.resolve(reported('WRITE')))
    render(<GitHubRepositoryPermissionPanel source={source} target={target} />)

    await click(show())

    const text = region().textContent ?? ''
    expect(text).toContain('Recorded owner' + 'octo-cat')
    expect(text).toContain('Recorded repository' + 'hello.world')
    expect(text).toMatch(/default stored authentication/)
    expect(text).toContain(DISCLOSURE)
    expect(text).toContain('Not checked.')
    expect(check().textContent).toBe('Check GitHub repository permission')
    expect(get).not.toHaveBeenCalled()
  })

  it.each(['READ', 'TRIAGE', 'TRIAGE_PLUS', 'WRITE', 'MAINTAIN', 'ADMIN'])(
    'shows the literal %s role, the bound ID, the returned identity and the UTC time as inert plain text with the disclosure',
    async (permission) => {
      const get = install(() => Promise.resolve(reported(permission)))
      render(<GitHubRepositoryPermissionPanel source={source} target={target} />)
      await click(show())

      await click(check())

      expect(get).toHaveBeenCalledTimes(1)
      expect(get).toHaveBeenCalledWith('run-a')
      const result = region().querySelector('.dc-github-permission-result')!
      expect(result.getAttribute('data-status')).toBe('PermissionReported')
      expect(result.textContent).toContain('GitHub repository permission' + permission)
      expect(result.textContent).toContain('Bound repository ID' + NODE_ID)
      expect(result.textContent).toContain('OCTO-cat')
      expect(result.textContent).toContain('2026-10-12T21:00:00.000Z')
      expect(region().textContent).toContain(DISCLOSURE)
      expect(result.textContent).not.toMatch(/\b(allowed|denied|ready|eligible|granted)\b/i)
      expect(region().querySelector('a, input, textarea, [contenteditable], img')).toBeNull()
      expect(within(region()).getAllByRole('button').map((button) => button.textContent)).toEqual([
        'Recheck GitHub repository permission',
        'Close GitHub repository permission',
      ])
    },
  )

  it('renders a hostile-looking role string only as plain text, so markup is never interpreted', async () => {
    // The decoder refuses anything outside the six literals, so the only text that can reach the DOM is a literal.
    install(() => Promise.resolve(reported('<b>ADMIN</b>')))
    render(<GitHubRepositoryPermissionPanel source={source} target={target} />)
    await click(show())

    await click(check())

    expect(within(region()).getByRole('alert').textContent).toMatch(/not coherent for this recorded target/)
    expect(region().querySelector('b')).toBeNull()
    expect(region().textContent).not.toContain('<b>')
  })

  it('shows an explicit null permission as not returned, never as a denial, with the identity and no role', async () => {
    install(() => Promise.resolve(reported(null)))
    render(<GitHubRepositoryPermissionPanel source={source} target={target} />)
    await click(show())

    await click(check())

    const result = region().querySelector('.dc-github-permission-result')!
    expect(result.getAttribute('data-status')).toBe('PermissionNotReturned')
    expect(result.textContent).toMatch(/returned no repository permission for the stored default authentication/)
    expect(result.textContent).toMatch(/not a denial and it does not show what kind of account or token was used/)
    expect(result.textContent).not.toContain('GitHub repository permission' + 'READ')
    expect(result.querySelectorAll('dt')).toHaveLength(6 - 1)
    expect(result.textContent).toContain('Bound repository ID' + NODE_ID)
    expect(region().textContent).toContain(DISCLOSURE)
  })

  it('treats a response that omits the permission member as incoherent: the alert, no not-returned claim and no remote facts', async () => {
    const { viewerPermission: omitted, ...identityOnly } = observation(null)
    expect(omitted).toBeNull()
    // The wire is JSON through the real generated response class, so the omitted member is undefined, never an explicit null.
    const malformed = GeneratedPermissionResponse.fromJS(
      JSON.parse(JSON.stringify({ ...withBinding, status: 'PermissionNotReturned', observation: identityOnly })),
    )
    expect(malformed.observation?.viewerPermission).toBeUndefined()
    install(() => Promise.resolve(malformed))
    render(<GitHubRepositoryPermissionPanel source={source} target={target} />)
    await click(show())

    await click(check())

    expect(within(region()).getByRole('alert').textContent).toMatch(/not coherent for this recorded target/)
    expect(region().querySelector('.dc-github-permission-result')).toBeNull()
    expect(region().querySelector('[data-status]')).toBeNull()
    expect(region().textContent).not.toMatch(/returned no repository permission/)
    expect(region().textContent).not.toContain('Observed (UTC)')
    expect(region().textContent).not.toContain('OCTO-cat')
    expect(region().textContent).not.toContain('Bound repository ID')
  })

  it('shows an identity change without any remote identity or role', async () => {
    install(() => Promise.resolve(withStatus('IdentityChanged', withBinding)))
    render(<GitHubRepositoryPermissionPanel source={source} target={target} />)
    await click(show())

    await click(check())

    const text = region().textContent ?? ''
    expect(text).toMatch(/differs from the repository ID bound for this recorded target/)
    expect(text).toContain('Bound repository ID' + NODE_ID)
    expect(region().querySelector('.dc-github-permission-result')).toBeNull()
    expect(text).not.toMatch(/GitHub repository permission(READ|TRIAGE|WRITE|MAINTAIN|ADMIN)/)
  })

  it.each([
    ['CliUnavailable', /not currently available as a vetted direct executable/],
    ['Unknown', /Unknown: no usable answer was returned/],
  ])('shows the fixed %s statement with the bound identity and no observation', async (status, pattern) => {
    install(() => Promise.resolve(withStatus(status, withBinding)))
    render(<GitHubRepositoryPermissionPanel source={source} target={target} />)
    await click(show())

    await click(check())

    expect(region().textContent).toMatch(pattern)
    expect(region().querySelector('.dc-github-permission-result')).toBeNull()
  })

  it('shows binding not recorded as exactly that and offers no binding action', async () => {
    install(() => Promise.resolve(withStatus('BindingNotRecorded', scope)))
    render(<GitHubRepositoryPermissionPanel source={source} target={target} />)
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
    render(<GitHubRepositoryPermissionPanel source={source} target={target} />)
    await click(show())

    await click(check())

    expect(region().textContent).toMatch(pattern)
  })

  it('refuses an answer that is not coherent for this scope, never partially showing it', async () => {
    const get = install(() => Promise.resolve(reported('OWNER')))
    render(<GitHubRepositoryPermissionPanel source={source} target={target} />)
    await click(show())

    await click(check())
    expect(within(region()).getByRole('alert').textContent).toMatch(/not coherent for this recorded target/)
    expect(region().textContent).not.toContain('OWNER')

    get.mockImplementation(() =>
      Promise.resolve({ ...reported('ADMIN'), observation: { ...observation('ADMIN'), repositoryNodeId: 'R_other' } } as unknown as GetGitHubRepositoryPermissionObservationResponse),
    )
    await click(check())
    expect(within(region()).getByRole('alert').textContent).toMatch(/not coherent/)
    expect(region().textContent).not.toContain('R_other')

    get.mockImplementation(() => Promise.reject(new Error('network')))
    await click(check())
    expect(within(region()).getByRole('alert').textContent).toBe('The GitHub repository permission could not be read.')
    expect(region().textContent).not.toContain('ADMIN')
    expect(get).toHaveBeenCalledTimes(3)
  })

  it('disables the check while a read is pending, hides the earlier observation, and closing then reopening starts unchecked', async () => {
    let resolve!: (value: GetGitHubRepositoryPermissionObservationResponse) => void
    let calls = 0
    const get = install(() => (++calls === 1 ? Promise.resolve(reported('READ')) : new Promise((res) => (resolve = res))))
    render(<GitHubRepositoryPermissionPanel source={source} target={target} />)
    await click(show())
    await click(check())
    expect(region().textContent).toContain('GitHub repository permission' + 'READ')

    await click(check())

    expect((check() as HTMLButtonElement).disabled).toBe(true)
    expect(region().textContent).toContain('Querying GitHub')
    expect(region().textContent).not.toContain('READ')
    await click(close())
    await act(async () => resolve(reported('ADMIN')))
    await click(show())

    expect(region().textContent).toContain('Not checked.')
    expect(region().textContent).not.toContain('ADMIN')
    expect(get).toHaveBeenCalledTimes(2)
  })

  it('ignores the obsolete completion of an earlier opening and an earlier scope (A-B-A), shows only the current answer', async () => {
    const first = vi.fn()
    const resolvers: ((value: GetGitHubRepositoryPermissionObservationResponse) => void)[] = []
    const get = install(
      () =>
        new Promise((res) => {
          first()
          resolvers.push(res)
        }),
    )
    const other: ObservationScope = { ...target, id: 'target-2', eventId: 'event-2' }
    const view = render(<GitHubRepositoryPermissionPanel source={source} target={target} />)
    await click(show())
    await click(check())

    view.rerender(<GitHubRepositoryPermissionPanel source={source} target={other} />)
    view.rerender(<GitHubRepositoryPermissionPanel source={source} target={target} />)
    await act(async () => resolvers[0](reported('ADMIN')))

    expect(within(region()).getAllByRole('button')).toHaveLength(1)
    expect(region().textContent).not.toContain('ADMIN')
    await click(show())
    expect(region().textContent).toContain('Not checked.')
    expect(get).toHaveBeenCalledTimes(1)
  })
})

describe('the repository permission region beside an Available target', () => {
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

  it('appears only for an Available target, collapsed, and neither loading the target nor showing it reads the permission or any sibling', async () => {
    installTarget('Available')
    const get = install(() => Promise.resolve(reported('MAINTAIN')))
    const identity = vi.fn(() => Promise.reject(new Error('must not be read')))
    const branch = vi.fn(() => Promise.reject(new Error('must not be read')))
    const binding = vi.fn(() => Promise.reject(new Error('must not be read')))
    const ci = vi.fn(() => Promise.reject(new Error('must not be read')))
    vi.mocked(gitHubRepositoryObservationClient).mockReturnValue({ getGitHubRepositoryObservation: identity } as unknown as ReturnType<typeof gitHubRepositoryObservationClient>)
    vi.mocked(gitHubBranchObservationClient).mockReturnValue({ getGitHubBranchObservation: branch } as unknown as ReturnType<typeof gitHubBranchObservationClient>)
    vi.mocked(gitHubRepositoryIdentityBindingClient).mockReturnValue({ getGitHubRepositoryIdentityBinding: binding } as unknown as ReturnType<typeof gitHubRepositoryIdentityBindingClient>)
    vi.mocked(gitHubCiObservationClient).mockReturnValue({ getGitHubCiObservation: ci } as unknown as ReturnType<typeof gitHubCiObservationClient>)
    render(<GitHubObservationTargetPanel source={source} />)

    expect(screen.queryByRole('region', { name: 'GitHub repository permission' })).toBeNull()
    await click(screen.getByRole('button', { name: 'Show GitHub observation target' }))
    expect(screen.queryByRole('region', { name: 'GitHub repository permission' })).toBeNull()
    await click(screen.getByRole('button', { name: 'Load GitHub observation target' }))

    expect(within(region()).getAllByRole('button')).toHaveLength(1)
    expect(show()).toBeTruthy()
    expect(get).not.toHaveBeenCalled()

    await click(show())
    await click(check())
    expect(get).toHaveBeenCalledTimes(1)
    expect(region().textContent).toContain('GitHub repository permission' + 'MAINTAIN')
    expect(identity).not.toHaveBeenCalled()
    expect(branch).not.toHaveBeenCalled()
    expect(binding).not.toHaveBeenCalled()
    expect(ci).not.toHaveBeenCalled()
  })

  it('does not appear for a NotRecorded or Unavailable target', async () => {
    for (const state of ['NotRecorded', 'Unavailable']) {
      installTarget(state)
      install(() => Promise.resolve(reported('WRITE')))
      render(<GitHubObservationTargetPanel source={source} />)
      await click(screen.getByRole('button', { name: 'Show GitHub observation target' }))
      await click(screen.getByRole('button', { name: 'Load GitHub observation target' }))

      expect(screen.queryByRole('region', { name: 'GitHub repository permission' })).toBeNull()
      cleanup()
    }
  })

  it('ends the permission lifetime when the target is closed or reloaded and never reads on reopening', async () => {
    installTarget('Available')
    const get = install(() => Promise.resolve(reported('ADMIN')))
    render(<GitHubObservationTargetPanel source={source} />)
    await click(screen.getByRole('button', { name: 'Show GitHub observation target' }))
    await click(screen.getByRole('button', { name: 'Load GitHub observation target' }))
    await click(show())
    await click(check())

    await click(screen.getByRole('button', { name: 'Reload GitHub observation target' }))
    expect(within(region()).getAllByRole('button')).toHaveLength(1)
    expect(region().textContent).not.toContain('ADMIN')
    await click(screen.getByRole('button', { name: 'Close GitHub observation target' }))
    await click(screen.getByRole('button', { name: 'Show GitHub observation target' }))
    await click(screen.getByRole('button', { name: 'Load GitHub observation target' }))

    expect(within(region()).getAllByRole('button')).toHaveLength(1)
    expect(region().textContent).not.toContain('ADMIN')
    expect(get).toHaveBeenCalledTimes(1)
  })
})
