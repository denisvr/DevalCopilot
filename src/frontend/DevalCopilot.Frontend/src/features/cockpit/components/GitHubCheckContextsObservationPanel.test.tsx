import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  gitHubBranchObservationClient,
  gitHubCheckContextsObservationClient,
  gitHubCheckRunOutputObservationClient,
  gitHubCiObservationClient,
  gitHubObservationTargetClient,
  gitHubRepositoryIdentityBindingClient,
  gitHubRepositoryObservationClient,
} from '../../../api/clients'
import type {
  GetGitHubCheckContextsObservationResponse,
  GetGitHubCheckRunOutputObservationResponse,
  GetGitHubObservationTargetResponse,
} from '../../../api/clients'
import type { ObservationScope, ObservationSource } from '../gitHubObservationTarget'
import { GitHubObservationTargetPanel } from './GitHubObservationTargetPanel'
import { GitHubCheckContextsObservationPanel } from './GitHubCheckContextsObservationPanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  gitHubObservationTargetClient: vi.fn(),
  gitHubCheckContextsObservationClient: vi.fn(),
  gitHubCheckRunOutputObservationClient: vi.fn(),
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
const run = (overrides: Record<string, unknown> = {}) => ({
  kind: 'CheckRun',
  id: 'CR_1',
  name: 'build',
  status: 'COMPLETED',
  conclusion: 'SUCCESS',
  checkSuiteId: 'CS_1',
  ...overrides,
})
const statusContext = (overrides: Record<string, unknown> = {}) => ({
  kind: 'StatusContext',
  id: 'SC_1',
  name: 'ci/legacy',
  status: 'PENDING',
  conclusion: null,
  checkSuiteId: null,
  ...overrides,
})
const observation = (overrides: Record<string, unknown> = {}) => ({
  repositoryNodeId: NODE_ID,
  owner: 'OCTO-cat',
  repository: 'hello.world',
  observedAtUtc: OBSERVED,
  ref: { branchName: source.branchName, commitSha: SHA },
  totalCount: null,
  contexts: null,
  ...overrides,
})
const reported = (contexts: unknown[]) =>
  ({
    ...withBinding,
    status: 'ContextsReported',
    observation: observation({ totalCount: contexts.length, contexts }),
  }) as unknown as GetGitHubCheckContextsObservationResponse
const withStatus = (status: string, extra: Record<string, unknown> = {}) =>
  ({ runId: source.runId, status, ...extra }) as unknown as GetGitHubCheckContextsObservationResponse
const full = (status: string, overrides: Record<string, unknown>) =>
  ({ ...withBinding, status, observation: observation(overrides) }) as unknown as GetGitHubCheckContextsObservationResponse

function install(read: () => Promise<GetGitHubCheckContextsObservationResponse>) {
  const getGitHubCheckContextsObservation = vi.fn(read)
  vi.mocked(gitHubCheckContextsObservationClient).mockReturnValue(
    { getGitHubCheckContextsObservation } as unknown as ReturnType<typeof gitHubCheckContextsObservationClient>,
  )
  return getGitHubCheckContextsObservation
}

async function click(element: HTMLElement) {
  await act(async () => {
    fireEvent.click(element)
  })
}

const region = () => screen.getByRole('region', { name: 'GitHub check details' })
const show = () => within(region()).getByRole('button', { name: 'Show GitHub check details' })
const check = () => within(region()).getByRole('button', { name: /^(Check|Recheck) GitHub check details$/ })
const close = () => within(region()).getByRole('button', { name: 'Close GitHub check details' })
const entries = () => Array.from(region().querySelectorAll('li'))

describe('GitHubCheckContextsObservationPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    cleanup()
  })

  it('is collapsed with one Show action and reads nothing on mount or while waiting', () => {
    vi.useFakeTimers()
    const get = install(() => Promise.resolve(reported([run()])))

    render(<GitHubCheckContextsObservationPanel source={source} target={target} />)
    act(() => {
      vi.advanceTimersByTime(10 * 60 * 1000)
    })
    vi.useRealTimers()

    expect(within(region()).getAllByRole('button')).toHaveLength(1)
    expect(show()).toBeTruthy()
    expect(get).not.toHaveBeenCalled()
  })

  it('opening shows the exact scope, the stored-authentication and bound-count notes and the fixed disclosure but reads nothing', async () => {
    const get = install(() => Promise.resolve(reported([run()])))
    render(<GitHubCheckContextsObservationPanel source={source} target={target} />)

    await click(show())

    const text = region().textContent ?? ''
    expect(text).toContain('Recorded branch' + source.branchName)
    expect(text).toContain('Recorded local commit' + SHA)
    expect(text).toContain('Recorded owner' + 'octo-cat')
    expect(text).toContain('Recorded repository' + 'hello.world')
    expect(text).toMatch(/default stored authentication/)
    expect(text).toMatch(/at most 50/)
    expect(text).toMatch(/bounded collection GitHub returned for this commit/)
    expect(text).toMatch(/not required-check approval, complete workflow history, permission or publication authority/)
    expect(text).toMatch(/host may have redacted any part of a check name that looked like a secret/)
    expect(text).toMatch(/not necessarily the provider's original text/)
    expect(text).toContain('Not checked.')
    expect(check().textContent).toBe('Check GitHub check details')
    expect(entries()).toHaveLength(0)
    expect(get).not.toHaveBeenCalled()
  })

  it('shows a name the host redacted exactly as the safe text it received, inert, beside the fixed redaction disclosure', async () => {
    const redacted = 'run [REDACTED] end'
    install(() => Promise.resolve(reported([run({ id: 'CR_1', name: redacted }), statusContext({ id: 'SC_1', name: '[REDACTED]' })])))
    render(<GitHubCheckContextsObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())

    expect(entries().map((entry) => entry.querySelectorAll('dd')[1].textContent)).toEqual([redacted, '[REDACTED]'])
    expect(region().textContent).toMatch(/may have redacted any part of a check name/)
    expect(region().querySelector('a, input, textarea, [contenteditable], script, img')).toBeNull()
  })

  it('shows every returned entry in provider order with kind, exact name, id, literal status and conclusion and the suite only for a check run', async () => {
    const get = install(() =>
      Promise.resolve(
        reported([
          run({ id: 'CR_b', name: 'build', status: 'COMPLETED', conclusion: 'FAILURE', checkSuiteId: 'CS_1' }),
          statusContext({ id: 'SC_a', name: 'build', status: 'ERROR' }),
          run({ id: 'CR_a', name: 'build', status: 'IN_PROGRESS', conclusion: null, checkSuiteId: 'CS_1' }),
        ]),
      ),
    )
    render(<GitHubCheckContextsObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())

    expect(get).toHaveBeenCalledTimes(1)
    expect(get).toHaveBeenCalledWith('run-a')
    const result = region().querySelector('.dc-github-contexts-result')!
    expect(result.getAttribute('data-status')).toBe('ContextsReported')
    expect(entries().map((entry) => entry.querySelector('dl')!.textContent)).toEqual([
      'KindCheckRunNamebuildIDCR_bStatusCOMPLETEDConclusionFAILURECheck suite IDCS_1',
      'KindStatusContextNamebuildIDSC_aStatusERROR',
      'KindCheckRunNamebuildIDCR_aStatusIN_PROGRESSConclusionnot returnedCheck suite IDCS_1',
    ])
    expect(result.textContent).toContain('Returned entries' + '3')
    expect(result.textContent).toContain('GitHub branch' + source.branchName)
    expect(result.textContent).toContain('GitHub commit' + SHA)
    expect(result.textContent).toContain('Bound repository ID' + NODE_ID)
    expect(result.textContent).toContain('OCTO-cat')
    expect(result.textContent).toContain('2026-10-12T21:00:00.000Z')
    expect(result.textContent).not.toMatch(/\b(passed|approved|ready|green)\b/i)
    expect(region().querySelector('a, input, textarea, [contenteditable], img, script')).toBeNull()
    // The contexts region's own actions are still exactly two; only the two check-run entries (never the status context) add one
    // collapsed output region each (ADR-0047).
    const own = within(region()).getAllByRole('button').filter((button) => !button.closest('.dc-github-output'))
    expect(own).toHaveLength(2)
    expect(entries().map((entry) => entry.querySelector('.dc-github-output') !== null)).toEqual([true, false, true])
    expect(region().querySelectorAll('.dc-github-output button')).toHaveLength(2)
    expect(check().textContent).toBe('Recheck GitHub check details')
  })

  it('renders markup-looking and special names as inert text with nothing interpreted', async () => {
    const names = [
      '<script>alert(1)</script>',
      '<img src=x onerror=alert(1)>',
      '[click](https://evil.example) **bold**',
      'say "hi" and back\\slash',
      '&amp; &lt;b&gt;',
      ' padded ',
      'pair ' + String.fromCodePoint(0x1f600) + ' ' + String.fromCodePoint(0x65e5),
    ]
    install(() => Promise.resolve(reported(names.map((name, index) => run({ id: `CR_${index}`, name })))))
    render(<GitHubCheckContextsObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())

    const shown = entries().map((entry) => entry.querySelectorAll('dd')[1].textContent)
    expect(shown).toEqual(names)
    expect(region().querySelector('script, img, a, strong, b, input')).toBeNull()
    expect(region().innerHTML).not.toContain('<script>')
    expect(region().innerHTML).not.toContain('<img')
  })

  it('shows zero returned entries as exactly that and never as a success', async () => {
    install(() => Promise.resolve(reported([])))
    render(<GitHubCheckContextsObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())

    const result = region().querySelector('.dc-github-contexts-result')!
    expect(result.getAttribute('data-status')).toBe('ContextsReported')
    expect(result.textContent).toMatch(/returned zero checks and statuses for the recorded commit/)
    expect(result.textContent).toMatch(/not a success, a pass or an approval/)
    expect(result.textContent).toContain('Returned entries' + '0')
    expect(entries()).toHaveLength(0)
    expect(result.textContent).not.toMatch(/\b(passed|approved|ready|green)\b/i)
  })

  it('shows fifty entries and offers no way to see a fifty-first', async () => {
    install(() => Promise.resolve(reported(Array.from({ length: 50 }, (_, index) => run({ id: `CR_${index}`, name: `job ${index}` })))))
    render(<GitHubCheckContextsObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())

    expect(entries()).toHaveLength(50)
    expect(entries()[49].textContent).toContain('job 49')
    expect(within(region()).queryByRole('button', { name: /more|next|older/i })).toBeNull()
  })

  it('shows the total but no entry when the 50-entry bound is exceeded and says so', async () => {
    install(() => Promise.resolve(full('LimitExceeded', { totalCount: 73 })))
    render(<GitHubCheckContextsObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())

    const result = region().querySelector('.dc-github-contexts-result')!
    expect(result.getAttribute('data-status')).toBe('LimitExceeded')
    expect(result.textContent).toMatch(/more than 50 checks and statuses/)
    expect(result.textContent).toMatch(/no entry is shown, and nothing is truncated silently/)
    expect(result.textContent).toContain('Total reported by GitHub' + '73')
    expect(entries()).toHaveLength(0)
    expect(result.textContent).toContain('GitHub commit' + SHA)
  })

  it('withholds every count and entry for a different commit and says so', async () => {
    install(() => Promise.resolve(full('CommitDifferent', { ref: { branchName: source.branchName, commitSha: OTHER } })))
    render(<GitHubCheckContextsObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())

    const result = region().querySelector('.dc-github-contexts-result')!
    expect(result.getAttribute('data-status')).toBe('CommitDifferent')
    expect(result.textContent).toMatch(/points at a different commit than the recorded local commit/)
    expect(result.textContent).toMatch(/no check details for the recorded commit are shown/)
    expect(result.textContent).toContain('GitHub commit' + OTHER)
    expect(result.textContent).not.toContain('Returned entries')
    expect(result.textContent).not.toContain('Total reported')
    expect(entries()).toHaveLength(0)
  })

  it('refuses a different-commit answer that nevertheless carries entries or a count, showing none of it', async () => {
    const get = install(() =>
      Promise.resolve(full('CommitDifferent', { ref: { branchName: source.branchName, commitSha: OTHER }, totalCount: 1, contexts: [run({ name: 'LEAKED-NAME' })] })),
    )
    render(<GitHubCheckContextsObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())

    expect(within(region()).getByRole('alert').textContent).toMatch(/not coherent for this recorded target/)
    expect(region().textContent).not.toContain('LEAKED-NAME')
    expect(entries()).toHaveLength(0)
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('shows no check summary returned as exactly that, never as no checks', async () => {
    install(() => Promise.resolve(full('NoContextsReturned', {})))
    render(<GitHubCheckContextsObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())

    const result = region().querySelector('.dc-github-contexts-result')!
    expect(result.getAttribute('data-status')).toBe('NoContextsReturned')
    expect(result.textContent).toMatch(/returned no check summary for the recorded commit/)
    expect(result.textContent).toMatch(/does not mean that no checks exist/)
    expect(result.textContent).toContain('GitHub commit' + SHA)
    expect(entries()).toHaveLength(0)
  })

  it('shows a ref that was not returned without a commit or entries', async () => {
    install(() => Promise.resolve(full('RefNotReturned', { ref: null })))
    render(<GitHubCheckContextsObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())

    const result = region().querySelector('.dc-github-contexts-result')!
    expect(result.getAttribute('data-status')).toBe('RefNotReturned')
    expect(result.textContent).toMatch(/returned no ref for this branch in this observation/)
    expect(result.textContent).not.toContain('GitHub commit')
    expect(entries()).toHaveLength(0)
  })

  it('shows an identity change without any remote identity, ref or entries', async () => {
    install(() => Promise.resolve(withStatus('IdentityChanged', withBinding)))
    render(<GitHubCheckContextsObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())

    const text = region().textContent ?? ''
    expect(text).toMatch(/differs from the repository ID bound for this recorded target/)
    expect(text).toContain('Bound repository ID' + NODE_ID)
    expect(region().querySelector('.dc-github-contexts-result')).toBeNull()
    expect(text).not.toContain('GitHub branch')
  })

  it.each([
    ['CliUnavailable', /not currently available as a vetted direct executable/],
    ['Unknown', /Unknown: no usable answer was returned/],
  ])('shows the fixed %s statement with the bound identity and no observation', async (status, pattern) => {
    install(() => Promise.resolve(withStatus(status, withBinding)))
    render(<GitHubCheckContextsObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())

    expect(region().textContent).toMatch(pattern)
    expect(region().querySelector('.dc-github-contexts-result')).toBeNull()
  })

  it('shows binding not recorded as exactly that and offers no binding action', async () => {
    install(() => Promise.resolve(withStatus('BindingNotRecorded', scope)))
    render(<GitHubCheckContextsObservationPanel source={source} target={target} />)
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
    render(<GitHubCheckContextsObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())

    expect(region().textContent).toMatch(pattern)
  })

  it('refuses an answer that is not coherent for this scope, never partially showing it, and reports a failed read', async () => {
    const get = install(() => Promise.resolve(reported([run({ name: 'bad' + String.fromCharCode(10) + 'name' })])))
    render(<GitHubCheckContextsObservationPanel source={source} target={target} />)
    await click(show())

    await click(check())
    expect(within(region()).getByRole('alert').textContent).toMatch(/not coherent for this recorded target/)
    expect(entries()).toHaveLength(0)

    get.mockImplementation(() =>
      Promise.resolve({ ...reported([run()]), observation: observation({ repositoryNodeId: 'R_other', totalCount: 1, contexts: [run()] }) } as unknown as GetGitHubCheckContextsObservationResponse),
    )
    await click(check())
    expect(within(region()).getByRole('alert').textContent).toMatch(/not coherent/)
    expect(region().textContent).not.toContain('R_other')

    get.mockImplementation(() => Promise.reject(new Error('network')))
    await click(check())
    expect(within(region()).getByRole('alert').textContent).toBe('The GitHub check details could not be read.')
    expect(entries()).toHaveLength(0)
    expect(get).toHaveBeenCalledTimes(3)
  })

  it('disables the check while a read is pending, hides the earlier entries, and closing then reopening starts unchecked', async () => {
    let resolve!: (value: GetGitHubCheckContextsObservationResponse) => void
    let calls = 0
    const get = install(() =>
      ++calls === 1 ? Promise.resolve(reported([run({ name: 'first-name' })])) : new Promise((res) => (resolve = res)),
    )
    render(<GitHubCheckContextsObservationPanel source={source} target={target} />)
    await click(show())
    await click(check())
    expect(region().textContent).toContain('first-name')

    await click(check())

    expect((check() as HTMLButtonElement).disabled).toBe(true)
    expect(region().textContent).toContain('Querying GitHub')
    expect(region().textContent).not.toContain('first-name')
    await click(close())
    await act(async () => resolve(reported([run({ name: 'late-name' })])))
    await click(show())

    expect(region().textContent).toContain('Not checked.')
    expect(region().textContent).not.toContain('late-name')
    expect(get).toHaveBeenCalledTimes(2)
  })
})

describe('the check details region beside an Available target', () => {
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

  it('appears only for an Available target, collapsed, and neither loading the target nor showing it reads the details or any sibling', async () => {
    installTarget('Available')
    const get = install(() => Promise.resolve(reported([run({ name: 'unit' })])))
    const identity = vi.fn(() => Promise.reject(new Error('must not be read')))
    const branch = vi.fn(() => Promise.reject(new Error('must not be read')))
    const binding = vi.fn(() => Promise.reject(new Error('must not be read')))
    const ci = vi.fn(() => Promise.reject(new Error('must not be read')))
    vi.mocked(gitHubRepositoryObservationClient).mockReturnValue({ getGitHubRepositoryObservation: identity } as unknown as ReturnType<typeof gitHubRepositoryObservationClient>)
    vi.mocked(gitHubBranchObservationClient).mockReturnValue({ getGitHubBranchObservation: branch } as unknown as ReturnType<typeof gitHubBranchObservationClient>)
    vi.mocked(gitHubRepositoryIdentityBindingClient).mockReturnValue({ getGitHubRepositoryIdentityBinding: binding } as unknown as ReturnType<typeof gitHubRepositoryIdentityBindingClient>)
    vi.mocked(gitHubCiObservationClient).mockReturnValue({ getGitHubCiObservation: ci } as unknown as ReturnType<typeof gitHubCiObservationClient>)
    render(<GitHubObservationTargetPanel source={source} />)

    expect(screen.queryByRole('region', { name: 'GitHub check details' })).toBeNull()
    await click(screen.getByRole('button', { name: 'Show GitHub observation target' }))
    expect(screen.queryByRole('region', { name: 'GitHub check details' })).toBeNull()
    await click(screen.getByRole('button', { name: 'Load GitHub observation target' }))

    expect(within(region()).getAllByRole('button')).toHaveLength(1)
    expect(screen.getByRole('region', { name: 'GitHub commit checks' })).toBeTruthy()
    expect(get).not.toHaveBeenCalled()

    await click(show())
    await click(check())
    expect(get).toHaveBeenCalledTimes(1)
    expect(entries().map((entry) => entry.querySelector('dl')!.textContent)).toEqual(['KindCheckRunNameunitIDCR_1StatusCOMPLETEDConclusionSUCCESSCheck suite IDCS_1'])
    expect(identity).not.toHaveBeenCalled()
    expect(branch).not.toHaveBeenCalled()
    expect(binding).not.toHaveBeenCalled()
    expect(ci).not.toHaveBeenCalled()
  })

  it('does not appear for a NotRecorded or Unavailable target', async () => {
    for (const state of ['NotRecorded', 'Unavailable']) {
      installTarget(state)
      install(() => Promise.resolve(reported([run()])))
      render(<GitHubObservationTargetPanel source={source} />)
      await click(screen.getByRole('button', { name: 'Show GitHub observation target' }))
      await click(screen.getByRole('button', { name: 'Load GitHub observation target' }))

      expect(screen.queryByRole('region', { name: 'GitHub check details' })).toBeNull()
      cleanup()
    }
  })

  it('ends the details lifetime when the target is closed or reloaded and never reads on reopening', async () => {
    installTarget('Available')
    const get = install(() => Promise.resolve(reported([run({ name: 'unit' })])))
    render(<GitHubObservationTargetPanel source={source} />)
    await click(screen.getByRole('button', { name: 'Show GitHub observation target' }))
    await click(screen.getByRole('button', { name: 'Load GitHub observation target' }))
    await click(show())
    await click(check())

    await click(screen.getByRole('button', { name: 'Reload GitHub observation target' }))
    expect(within(region()).getAllByRole('button')).toHaveLength(1)
    expect(region().textContent).not.toContain('unit')
    await click(screen.getByRole('button', { name: 'Close GitHub observation target' }))
    await click(screen.getByRole('button', { name: 'Show GitHub observation target' }))
    await click(screen.getByRole('button', { name: 'Load GitHub observation target' }))

    expect(within(region()).getAllByRole('button')).toHaveLength(1)
    expect(region().textContent).not.toContain('unit')
    expect(get).toHaveBeenCalledTimes(1)
  })
})

describe('the output region of a check run entry (ADR-0047)', () => {
  const outputWire = (checkRunId: string, text: string) =>
    ({
      ...withBinding,
      checkRunId,
      status: 'OutputReported',
      observation: {
        repositoryNodeId: NODE_ID,
        owner: 'OCTO-cat',
        repository: 'hello.world',
        observedAtUtc: OBSERVED,
        ref: { branchName: source.branchName, commitSha: SHA },
        checkRun: {
          id: checkRunId,
          checkSuiteId: 'CS_1',
          commitSha: SHA,
          name: 'unit',
          status: 'COMPLETED',
          conclusion: 'FAILURE',
          output: { title: null, summary: null, text },
        },
      },
    }) as unknown as GetGitHubCheckRunOutputObservationResponse

  function installOutput(read: (runId: string, checkRunId: string) => Promise<GetGitHubCheckRunOutputObservationResponse>) {
    const getGitHubCheckRunOutputObservation = vi.fn(read)
    vi.mocked(gitHubCheckRunOutputObservationClient).mockReturnValue(
      { getGitHubCheckRunOutputObservation } as unknown as ReturnType<typeof gitHubCheckRunOutputObservationClient>,
    )
    return getGitHubCheckRunOutputObservation
  }

  const outputRegion = (checkRunId: string) => screen.getByRole('region', { name: `GitHub check output for check run ${checkRunId}` })
  const outputShow = (checkRunId: string) => within(outputRegion(checkRunId)).getByRole('button', { name: 'Show GitHub check output' })
  const outputCheck = (checkRunId: string) =>
    within(outputRegion(checkRunId)).getByRole('button', { name: /^(Check|Recheck) GitHub check output$/ })

  async function openContexts(contexts: unknown[]) {
    const get = install(() => Promise.resolve(reported(contexts)))
    render(<GitHubCheckContextsObservationPanel source={source} target={target} />)
    await click(show())
    await click(check())
    return get
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    cleanup()
  })

  it('is offered only for check runs, collapsed, and neither the contexts reading nor opening it sends any request', async () => {
    const contexts = await openContexts([run({ id: 'CR_1' }), statusContext({ id: 'SC_1' }), run({ id: 'CR_2' })])
    const output = installOutput((_runId, checkRunId) => Promise.resolve(outputWire(checkRunId, 'x')))

    expect(region().querySelectorAll('.dc-github-output')).toHaveLength(2)
    expect(entries()[1].querySelector('.dc-github-output')).toBeNull()
    expect(within(outputRegion('CR_1')).getAllByRole('button')).toHaveLength(1)

    await click(outputShow('CR_1'))
    await click(outputShow('CR_2'))

    expect(output).not.toHaveBeenCalled()
    expect(contexts).toHaveBeenCalledTimes(1)
  })

  it('sends one request for exactly the selected check run on its own explicit check, shows only that answer and re-reads nothing else', async () => {
    const contexts = await openContexts([run({ id: 'CR_1', name: 'unit' }), run({ id: 'CR_2', name: 'lint' })])
    const output = installOutput((_runId, checkRunId) => Promise.resolve(outputWire(checkRunId, `log of ${checkRunId}`)))

    await click(outputShow('CR_2'))
    await click(outputCheck('CR_2'))

    expect(output).toHaveBeenCalledTimes(1)
    expect(output).toHaveBeenCalledWith('run-a', 'CR_2')
    expect(outputRegion('CR_2').querySelector('[data-field="text"]')?.textContent).toBe('log of CR_2')
    expect(outputRegion('CR_1').textContent).not.toContain('log of')
    expect(contexts).toHaveBeenCalledTimes(1)
  })

  it('treats the preceding contexts reading as no authorization: the host answer alone decides what is shown', async () => {
    await openContexts([run({ id: 'CR_1', name: 'unit' })])
    const unavailable = {
      ...withBinding,
      checkRunId: 'CR_1',
      status: 'CheckRunUnavailable',
      observation: {
        repositoryNodeId: NODE_ID,
        owner: 'OCTO-cat',
        repository: 'hello.world',
        observedAtUtc: OBSERVED,
        ref: { branchName: source.branchName, commitSha: SHA },
        checkRun: null,
      },
    } as unknown as GetGitHubCheckRunOutputObservationResponse
    installOutput(() => Promise.resolve(unavailable))

    await click(outputShow('CR_1'))
    await click(outputCheck('CR_1'))

    expect(outputRegion('CR_1').textContent).toMatch(/did not show this check run as belonging/)
    expect(outputRegion('CR_1').querySelector('pre')).toBeNull()
  })

  it('ends the output region when the contexts are rechecked, and ignores a completion that arrives afterwards', async () => {
    await openContexts([run({ id: 'CR_1', name: 'unit' })])
    let release!: (value: GetGitHubCheckRunOutputObservationResponse) => void
    const output = installOutput(() => new Promise((resolve) => (release = resolve)))
    await click(outputShow('CR_1'))
    await click(outputCheck('CR_1'))

    await click(check())
    await act(async () => release(outputWire('CR_1', 'late output')))

    expect(within(outputRegion('CR_1')).getAllByRole('button')).toHaveLength(1)
    expect(region().textContent).not.toContain('late output')
    expect(output).toHaveBeenCalledTimes(1)
  })

  it('discards an obtained output when the contexts are rechecked or closed and never reads on reopening', async () => {
    const contexts = await openContexts([run({ id: 'CR_1', name: 'unit' })])
    const output = installOutput((_runId, checkRunId) => Promise.resolve(outputWire(checkRunId, 'first output')))
    await click(outputShow('CR_1'))
    await click(outputCheck('CR_1'))
    expect(outputRegion('CR_1').textContent).toContain('first output')

    await click(check())
    expect(outputRegion('CR_1').textContent).not.toContain('first output')
    expect(within(outputRegion('CR_1')).getAllByRole('button')).toHaveLength(1)

    await click(outputShow('CR_1'))
    await click(outputCheck('CR_1'))
    expect(outputRegion('CR_1').textContent).toContain('first output')
    await click(close())
    await click(show())
    await click(check())

    expect(outputRegion('CR_1').textContent).not.toContain('first output')
    expect(within(outputRegion('CR_1')).getAllByRole('button')).toHaveLength(1)
    expect(output).toHaveBeenCalledTimes(2)
    expect(contexts).toHaveBeenCalledTimes(3)
  })

  it('keeps each entry output region independent: one pending read does not block or overwrite another', async () => {
    await openContexts([run({ id: 'CR_1', name: 'unit' }), run({ id: 'CR_2', name: 'lint' })])
    const releases = new Map<string, (value: GetGitHubCheckRunOutputObservationResponse) => void>()
    installOutput((_runId, checkRunId) => new Promise((resolve) => releases.set(checkRunId, resolve)))
    await click(outputShow('CR_1'))
    await click(outputShow('CR_2'))

    await click(outputCheck('CR_1'))
    await click(outputCheck('CR_2'))
    await act(async () => releases.get('CR_2')!(outputWire('CR_2', 'second finished first')))
    await act(async () => releases.get('CR_1')!(outputWire('CR_1', 'first finished last')))

    expect(outputRegion('CR_1').querySelector('[data-field="text"]')?.textContent).toBe('first finished last')
    expect(outputRegion('CR_2').querySelector('[data-field="text"]')?.textContent).toBe('second finished first')
  })
})
