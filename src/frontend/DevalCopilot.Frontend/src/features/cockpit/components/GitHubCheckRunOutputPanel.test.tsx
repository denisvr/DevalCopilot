import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { gitHubCheckRunOutputObservationClient } from '../../../api/clients'
import type { GetGitHubCheckRunOutputObservationResponse } from '../../../api/clients'
import type { ObservationScope, ObservationSource } from '../gitHubObservationTarget'
import { GitHubCheckRunOutputPanel } from './GitHubCheckRunOutputPanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  gitHubCheckRunOutputObservationClient: vi.fn(),
}))

const SHA = 'c'.repeat(40)
const OTHER = 'd'.repeat(40)
const NODE_ID = 'R_kgDOExample01'
const CHECK = 'CR_kwDOCheck001'
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
const PARENT = { reading: 1 }

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
const scope = { runId: source.runId, checkRunId: CHECK, source: wireSource, target: wireTarget }
const withBinding = { ...scope, binding: wireBinding }
const output = (overrides: Record<string, unknown> = {}) => ({ title: 'Title', summary: 'Summary', text: 'Text', ...overrides })
const checkRun = (overrides: Record<string, unknown> = {}) => ({
  id: CHECK,
  checkSuiteId: 'CS_kwDOSuite001',
  commitSha: SHA,
  name: 'unit tests',
  status: 'COMPLETED',
  conclusion: 'FAILURE',
  output: output(),
  ...overrides,
})
const observation = (overrides: Record<string, unknown> = {}) => ({
  repositoryNodeId: NODE_ID,
  owner: 'OCTO-cat',
  repository: 'hello.world',
  observedAtUtc: OBSERVED,
  ref: { branchName: source.branchName, commitSha: SHA },
  checkRun: null,
  ...overrides,
})
const reported = (outputOverrides: Record<string, unknown> = {}, runOverrides: Record<string, unknown> = {}) =>
  ({
    ...withBinding,
    status: 'OutputReported',
    observation: observation({ checkRun: checkRun({ output: output(outputOverrides), ...runOverrides }) }),
  }) as unknown as GetGitHubCheckRunOutputObservationResponse
const full = (status: string, overrides: Record<string, unknown>) =>
  ({ ...withBinding, status, observation: observation(overrides) }) as unknown as GetGitHubCheckRunOutputObservationResponse
const bare = (status: string, extra: Record<string, unknown> = {}) =>
  ({ runId: source.runId, checkRunId: CHECK, status, ...extra }) as unknown as GetGitHubCheckRunOutputObservationResponse

function install(read: () => Promise<GetGitHubCheckRunOutputObservationResponse>) {
  const getGitHubCheckRunOutputObservation = vi.fn(read)
  vi.mocked(gitHubCheckRunOutputObservationClient).mockReturnValue(
    { getGitHubCheckRunOutputObservation } as unknown as ReturnType<typeof gitHubCheckRunOutputObservationClient>,
  )
  return getGitHubCheckRunOutputObservation
}

async function click(element: HTMLElement) {
  await act(async () => {
    fireEvent.click(element)
  })
}

const renderPanel = (parent: object = PARENT) =>
  render(<GitHubCheckRunOutputPanel source={source} target={target} checkRunId={CHECK} parent={parent} />)
const region = () => screen.getByRole('region', { name: `GitHub check output for check run ${CHECK}` })
const show = () => within(region()).getByRole('button', { name: 'Show GitHub check output' })
const check = () => within(region()).getByRole('button', { name: /^(Check|Recheck) GitHub check output$/ })
const close = () => within(region()).getByRole('button', { name: 'Close GitHub check output' })
const field = (name: 'title' | 'summary' | 'text') => region().querySelector(`[data-field="${name}"]`) as HTMLElement

async function openAndCheck() {
  await click(show())
  await click(check())
}

describe('GitHubCheckRunOutputPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    cleanup()
    vi.useRealTimers()
  })

  it('is collapsed with one Show action and reads nothing on mount or while waiting', () => {
    vi.useFakeTimers()
    const get = install(() => Promise.resolve(reported()))

    renderPanel()
    act(() => {
      vi.advanceTimersByTime(10 * 60 * 1000)
    })

    expect(within(region()).getAllByRole('button')).toHaveLength(1)
    expect(show()).toBeTruthy()
    expect(region().textContent).not.toMatch(/Close|Recheck/)
    expect(get).not.toHaveBeenCalled()
  })

  it('shows the recorded scope, the stored-authentication note and the fixed untrusted-text disclosure before anything is checked, sending nothing', async () => {
    const get = install(() => Promise.resolve(reported()))
    renderPanel()

    await click(show())

    const text = region().textContent ?? ''
    expect(text).toContain(source.branchName)
    expect(text).toContain(SHA)
    expect(text).toContain('octo-cat')
    expect(text).toContain('hello.world')
    expect(text).toContain(CHECK)
    expect(text).toMatch(/default stored authentication/)
    expect(text).toMatch(/untrusted text supplied by GitHub/)
    expect(text).toMatch(/not an instruction, a diagnosis/)
    expect(text).toMatch(/redacted by the host and is not necessarily the provider's original text/)
    expect(text).toContain('Not checked.')
    expect(check().textContent).toBe('Check GitHub check output')
    expect(get).not.toHaveBeenCalled()
  })

  it('checks once for exactly the run and the check and renders the answer as plain text only', async () => {
    const markup = '<script>alert(1)</script> [open](https://evil.example/x) **bold** javascript:alert(1) &amp; <img src=x onerror=alert(1)>'
    const get = install(() => Promise.resolve(reported({ title: '<b>Title</b>', summary: '[s](https://evil.example)', text: markup })))
    renderPanel()

    await openAndCheck()

    expect(get).toHaveBeenCalledTimes(1)
    expect(get).toHaveBeenCalledWith('run-a', CHECK)
    expect(field('title').textContent).toBe('<b>Title</b>')
    expect(field('summary').textContent).toBe('[s](https://evil.example)')
    expect(field('text').textContent).toBe(markup)
    expect(field('text').tagName).toBe('PRE')
    expect(region().querySelector('a, img, script, iframe, form, input, textarea, video, object, embed')).toBeNull()
    expect(region().querySelector('b, strong, em, i')).toBeNull()
    expect(check().textContent).toBe('Recheck GitHub check output')
    const facts = region().textContent ?? ''
    expect(facts).toContain('unit tests')
    expect(facts).toContain('COMPLETED')
    expect(facts).toContain('FAILURE')
    expect(facts).toContain('CS_kwDOSuite001')
    expect(facts).toContain(NODE_ID)
    expect(facts).toContain('OCTO-cat')
    expect(facts).toContain('2026-10-12T21:00:00.000Z')
  })

  it('states null, empty and whitespace-only values with fixed text and shows whitespace exactly as returned', async () => {
    install(() => Promise.resolve(reported({ title: null, summary: '', text: '  \n\t  ' })))
    renderPanel()

    await openAndCheck()

    expect(region().textContent).toContain('GitHub returned no value for this field.')
    expect(region().textContent).toContain('GitHub returned an empty string for this field.')
    expect(region().textContent).toContain('GitHub returned only whitespace for this field; it is shown as returned.')
    expect(field('title')).toBeNull()
    expect(field('summary')).toBeNull()
    expect(field('text').textContent).toBe('  \n\t  ')
    expect(region().querySelectorAll('[data-state="null"]')).toHaveLength(1)
    expect(region().querySelectorAll('[data-state="empty"]')).toHaveLength(1)
    expect(region().querySelectorAll('[data-state="whitespace"]')).toHaveLength(1)
  })

  it('shows a null conclusion as not returned, never as passed', async () => {
    install(() => Promise.resolve(reported({}, { conclusion: null, status: 'IN_PROGRESS' })))
    renderPanel()

    await openAndCheck()

    expect(region().textContent).toContain('not returned')
    expect(region().textContent).not.toMatch(/\b(passed|approved|ready|green)\b/i)
  })

  it('OutputNotReturned states that nothing was returned and shows no output field', async () => {
    install(() => Promise.resolve(full('OutputNotReturned', { checkRun: checkRun({ output: null }) })))
    renderPanel()

    await openAndCheck()

    expect(region().textContent).toMatch(/with no title, summary or text/)
    expect(region().querySelector('pre')).toBeNull()
    expect(region().textContent).toContain('unit tests')
  })

  it.each([
    ['CommitDifferent', full('CommitDifferent', { ref: { branchName: source.branchName, commitSha: OTHER } }), /different commit than the recorded local commit/, OTHER],
    ['CheckRunUnavailable', full('CheckRunUnavailable', {}), /did not show this check run as belonging/, SHA],
    ['RefNotReturned', full('RefNotReturned', { ref: null }), /returned no ref for this branch/, null],
  ])('%s shows its fixed statement and the observed facts but no check or output', async (_name, response, statement, commit) => {
    install(() => Promise.resolve(response))
    renderPanel()

    await openAndCheck()

    expect(region().textContent).toMatch(statement)
    if (commit !== null) {
      expect(region().textContent).toContain(commit)
    }

    expect(region().querySelector('pre')).toBeNull()
    expect(region().textContent).not.toContain('CS_kwDOSuite001')
  })

  it.each([
    ['CliUnavailable', bare('CliUnavailable', withBinding), /not currently available as a vetted direct executable/],
    ['Unknown', bare('Unknown', withBinding), /^.*Unknown: no usable answer/s],
    ['IdentityChanged', bare('IdentityChanged', withBinding), /now differs from the repository ID bound/],
    ['Unavailable', bare('Unavailable'), /Unavailable: the recorded facts/],
    ['NotRecorded', bare('NotRecorded'), /no longer reports a recorded GitHub observation target/],
    ['BindingNotRecorded', bare('BindingNotRecorded', scope), /No repository identity is bound/],
  ])('%s shows only its fixed statement and the bound identity where it has one, never an output', async (_name, response, statement) => {
    install(() => Promise.resolve(response))
    renderPanel()

    await openAndCheck()

    expect(region().textContent).toMatch(statement)
    expect(region().querySelector('pre')).toBeNull()
  })

  it('shows a failed read and an incoherent answer as fixed alerts and none of the answer', async () => {
    const get = install(() => Promise.reject(new Error('network')))
    renderPanel()
    await openAndCheck()
    expect(within(region()).getByRole('alert').textContent).toBe('The GitHub check output could not be read.')

    get.mockImplementation(() => Promise.resolve(reported({ text: 'must not show' }, { id: 'CR_other' })))
    await click(check())

    expect(within(region()).getByRole('alert').textContent).toMatch(/not coherent for this recorded target/)
    expect(region().textContent).not.toContain('must not show')
  })

  it('disables the check while pending and offers close at all times', async () => {
    let release!: (value: GetGitHubCheckRunOutputObservationResponse) => void
    install(() => new Promise((resolve) => (release = resolve)))
    renderPanel()
    await click(show())

    await click(check())

    expect((check() as HTMLButtonElement).disabled).toBe(true)
    expect(region().textContent).toContain('Querying GitHub for the check output')
    expect(close()).toBeTruthy()
    await act(async () => release(reported()))
    expect((check() as HTMLButtonElement).disabled).toBe(false)
  })

  it('closing collapses and discards the answer and reopening starts unchecked', async () => {
    const get = install(() => Promise.resolve(reported({ text: 'first answer' })))
    renderPanel()
    await openAndCheck()
    expect(field('text').textContent).toBe('first answer')

    await click(close())
    expect(region().textContent).not.toContain('first answer')
    await click(show())

    expect(region().textContent).toContain('Not checked.')
    expect(region().textContent).not.toContain('first answer')
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('a new parent reading ends the region: it collapses, drops its answer and a late completion', async () => {
    let release!: (value: GetGitHubCheckRunOutputObservationResponse) => void
    const get = install(() => new Promise((resolve) => (release = resolve)))
    const view = renderPanel(PARENT)
    await openAndCheck()

    view.rerender(<GitHubCheckRunOutputPanel source={source} target={target} checkRunId={CHECK} parent={{ reading: 2 }} />)
    await act(async () => release(reported({ text: 'late answer' })))

    expect(within(region()).getAllByRole('button')).toHaveLength(1)
    expect(region().textContent).not.toContain('late answer')
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('offers no copy, link, download, Agent, rerun or publication action, only Show, Check or Recheck and Close', async () => {
    install(() => Promise.resolve(reported()))
    renderPanel()
    await openAndCheck()

    const names = within(region()).getAllByRole('button').map((button) => button.textContent)

    expect(names).toEqual(['Recheck GitHub check output', 'Close GitHub check output'])
    expect(within(region()).queryAllByRole('link')).toHaveLength(0)
    expect(region().textContent).not.toMatch(/\b(copy|download|open in|send to|rerun|re-run|diagnose|fix|publish|approve|push)\b/i)
  })
})
