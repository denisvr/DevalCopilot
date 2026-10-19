import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  agentAttemptArtifactWindowClient,
  agentAttemptEvidenceClient,
  agentAttemptHistoryClient,
  agentAttemptInputsClient,
  localDeliveryReceiptClient,
  projectRunHistoryClient,
} from '../../../api/clients'
import type {
  AgentAttemptEvidenceResponse,
  AgentAttemptHistoryResponse,
  SealedAgentArtifactWindowResponse,
} from '../../../api/generated/api-client'
import { AgentProcessExecutionResponse, AgentTokenUsageResponse } from '../../../api/generated/api-client'
import { attemptEvidenceFor, attemptIdFor, attemptPageOf, windowOf } from '../agentAttemptHistoryFixture'
import { recordedInputsFor } from '../agentAttemptInputsFixture'
import { availableFor } from '../localDeliveryReceiptFixture'
import { deliveredEntryFor, entryFor, pageOf } from '../projectRunHistoryFixture'
import { ProjectRunHistoryPanel } from './ProjectRunHistoryPanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  projectRunHistoryClient: vi.fn(),
  localDeliveryReceiptClient: vi.fn(),
  agentAttemptHistoryClient: vi.fn(),
  agentAttemptEvidenceClient: vi.fn(),
  agentAttemptArtifactWindowClient: vi.fn(),
  agentAttemptInputsClient: vi.fn(),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}

type HistoryRead = (runId: string, before: number | null | undefined, limit: number | null | undefined) => Promise<AgentAttemptHistoryResponse>
type EvidenceRead = (runId: string, attemptId: string) => Promise<AgentAttemptEvidenceResponse>
type WindowRead = (runId: string, attemptId: string, segment: string, fromOffset: number | undefined) => Promise<SealedAgentArtifactWindowResponse>
type InputsRead = (runId: string, attemptId: string, limit?: number) => Promise<unknown>

let historyRead: ReturnType<typeof vi.fn<HistoryRead>>
let evidenceRead: ReturnType<typeof vi.fn<EvidenceRead>>
let windowRead: ReturnType<typeof vi.fn<WindowRead>>
let inputsRead: ReturnType<typeof vi.fn<InputsRead>>
let runHistoryRead: ReturnType<typeof vi.fn>
let view: ReturnType<typeof render>

/** Per-run defaults: run-N has attempts N+1 and N, each with a context manifest whose text names its own run and attempt. */
const defaults = {
  history: ((runId) => Promise.resolve(attemptPageOf(runId, [2, 1]))) as HistoryRead,
  evidence: ((runId, attemptId) => Promise.resolve(attemptEvidenceFor(runId, Number(attemptId.split('-').pop())))) as EvidenceRead,
  window: ((runId, attemptId) => Promise.resolve(windowOf(`manifest of ${runId} ${attemptId}`))) as WindowRead,
  inputs: ((runId, attemptId) => Promise.resolve(recordedInputsFor(runId, Number(attemptId.split('-').pop()), 2))) as InputsRead,
}

function installAttempts(overrides: Partial<typeof defaults> = {}) {
  historyRead = vi.fn(overrides.history ?? defaults.history)
  evidenceRead = vi.fn(overrides.evidence ?? defaults.evidence)
  windowRead = vi.fn(overrides.window ?? defaults.window)
  inputsRead = vi.fn(overrides.inputs ?? defaults.inputs)
  vi.mocked(agentAttemptInputsClient).mockReturnValue({ readAgentAttemptInputs: inputsRead } as unknown as ReturnType<typeof agentAttemptInputsClient>)
  vi.mocked(agentAttemptHistoryClient).mockReturnValue({ getAgentAttemptHistory: historyRead } as unknown as ReturnType<typeof agentAttemptHistoryClient>)
  vi.mocked(agentAttemptEvidenceClient).mockReturnValue({ getAgentAttemptEvidence: evidenceRead } as unknown as ReturnType<typeof agentAttemptEvidenceClient>)
  vi.mocked(agentAttemptArtifactWindowClient).mockReturnValue({
    getAgentAttemptArtifactWindow: windowRead,
  } as unknown as ReturnType<typeof agentAttemptArtifactWindowClient>)
}

const detail = () => screen.getByRole('region', { name: 'Selected run' })
const attempts = () => within(detail()).getByRole('region', { name: 'Agent attempt history' })
const attemptRows = () => within(attempts()).queryAllByRole('listitem')
const attemptDetail = () => within(attempts()).queryByRole('region', { name: 'Selected Agent attempt evidence' })
const inspectRun = (number: number) => fireEvent.click(screen.getByRole('button', { name: `Inspect run #${number}` }))
const toggleAttempts = () => fireEvent.click(within(attempts()).getByRole('button', { name: /Agent attempt history/ }))
const inspectAttempt = (number: number) => fireEvent.click(within(attempts()).getByRole('button', { name: `Inspect attempt ${number}` }))

/** Opens the attempts of the selected run, selects `attemptNumber` and loads its context manifest text. */
async function showManifest(attemptNumber: number) {
  toggleAttempts()
  await waitFor(() => expect(attemptRows().length).toBeGreaterThan(0))
  inspectAttempt(attemptNumber)
  await within(attempts()).findByText(/Context manifest: 21 bytes/)
  fireEvent.change(within(attempts()).getByRole('combobox'), { target: { value: 'AgentContextManifest' } })
  fireEvent.click(within(attempts()).getByRole('button', { name: 'Load' }))
}

/** Everything of a run's attempt inspection that could leak into a successor's first frame. */
function expectNoAttemptData() {
  expect(within(attempts()).getByRole('button', { name: 'Show Agent attempt history' })).toHaveAttribute('aria-expanded', 'false')
  expect(attemptRows()).toHaveLength(0)
  expect(attemptDetail()).toBeNull()
  expect(within(detail()).queryByText(/manifest of/)).toBeNull()
  expect(within(detail()).queryByRole('combobox')).toBeNull()
}

describe('the Agent attempts of a historical run detail', () => {
  beforeEach(async () => {
    vi.clearAllMocks()
    runHistoryRead = vi.fn(() => Promise.resolve(pageOf([], { entries: [deliveredEntryFor(3), entryFor(2), entryFor(1)] })))
    vi.mocked(projectRunHistoryClient).mockReturnValue({ getProjectRunHistory: runHistoryRead } as unknown as ReturnType<typeof projectRunHistoryClient>)
    const getLocalDeliveryReceipt = vi.fn((runId: string) =>
      Promise.resolve(availableFor({ runId, operationId: 'operation-3', commitSha: 'c'.repeat(40), checkpointId: 'checkpoint-3', checkpointNumber: 3 })),
    )
    vi.mocked(localDeliveryReceiptClient).mockReturnValue({ getLocalDeliveryReceipt } as unknown as ReturnType<typeof localDeliveryReceiptClient>)
    installAttempts()
    view = render(<ProjectRunHistoryPanel projectId="project-1" />)
    fireEvent.click(screen.getByRole('button', { name: 'Show run history' }))
    await screen.findByRole('button', { name: 'Inspect run #3' })
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('is collapsed inside the selected run, reads attempts only when opened, evidence only when selected and text only when loaded', async () => {
    inspectRun(2)

    expectNoAttemptData()
    expect(historyRead).not.toHaveBeenCalled()

    toggleAttempts()
    await waitFor(() => expect(attemptRows()).toHaveLength(2))
    expect(historyRead.mock.calls).toEqual([['run-2', null, 10]])
    expect(evidenceRead).not.toHaveBeenCalled()

    inspectAttempt(1)
    await within(attempts()).findByText(/Context manifest: 21 bytes/)
    expect(evidenceRead.mock.calls).toEqual([['run-2', attemptIdFor('run-2', 1)]])
    fireEvent.change(within(attempts()).getByRole('combobox'), { target: { value: 'AgentContextManifest' } })
    expect(windowRead).not.toHaveBeenCalled()

    fireEvent.click(within(attempts()).getByRole('button', { name: 'Load' }))
    expect(await within(attempts()).findByText(`manifest of run-2 ${attemptIdFor('run-2', 1)}`)).toBeInTheDocument()
    expect(windowRead.mock.calls).toEqual([['run-2', attemptIdFor('run-2', 1), 'context-manifest', 0, 16384]])
    expect(within(attempts()).getByText(/composed entirely by DevalCopilot/)).toBeInTheDocument()
    expect(within(detail()).getByText('Objective 2')).toBeInTheDocument()
  })

  it('shows a replaced run (A to B) with no attempt data in its first frame and lets no late read of A settle B', async () => {
    const slowEvidence = deferred<AgentAttemptEvidenceResponse>()
    const slowWindow = deferred<SealedAgentArtifactWindowResponse>()
    installAttempts({
      evidence: (runId, attemptId) => (runId === 'run-3' && attemptId.endsWith('-2') ? slowEvidence.promise : defaults.evidence(runId, attemptId)),
      window: (runId, attemptId, segment, from) => (runId === 'run-3' ? slowWindow.promise : defaults.window(runId, attemptId, segment, from)),
    })
    inspectRun(3)
    await showManifest(1)
    inspectAttempt(2)

    inspectRun(2)

    expectNoAttemptData()
    await act(async () => {
      slowEvidence.resolve(attemptEvidenceFor('run-3', 2))
      slowWindow.resolve(windowOf('late text of run-3'))
      await Promise.all([slowEvidence.promise, slowWindow.promise])
    })
    expectNoAttemptData()
    expect(within(detail()).queryByText(/late text/)).toBeNull()

    toggleAttempts()
    await waitFor(() => expect(attemptRows()).toHaveLength(2))
    expect(historyRead.mock.calls.map(([runId]) => runId)).toEqual(['run-3', 'run-2'])
    expect(attemptRows()[0]).toHaveTextContent('Attempt #2')
  })

  it('starts a returned run (A to B to A) collapsed and fresh, and the stale reads of the first visit stay ignored', async () => {
    const staleHistory = deferred<AgentAttemptHistoryResponse>()
    let firstVisit = true
    installAttempts({
      history: (runId, before, limit) => (runId === 'run-3' && firstVisit ? ((firstVisit = false), staleHistory.promise) : defaults.history(runId, before, limit)),
    })
    inspectRun(3)
    toggleAttempts()
    inspectRun(2)
    await showManifest(2)
    expect(await within(attempts()).findByText(`manifest of run-2 ${attemptIdFor('run-2', 2)}`)).toBeInTheDocument()

    inspectRun(3)

    expectNoAttemptData()
    await act(async () => {
      staleHistory.resolve(attemptPageOf('run-3', [99, 98]))
      await staleHistory.promise
    })
    expectNoAttemptData()
    expect(within(attempts()).queryByText('Loading attempt history…')).toBeNull()

    toggleAttempts()
    await waitFor(() => expect(attemptRows()).toHaveLength(2))
    expect(within(attempts()).queryByText(/Attempt #99/)).toBeNull()
    expect(historyRead.mock.calls.map(([runId]) => runId)).toEqual(['run-3', 'run-2', 'run-3'])
  })

  it('ends every attempt read, selection and text when the selected run is closed and shows it fresh when selected again', async () => {
    const slowWindow = deferred<SealedAgentArtifactWindowResponse>()
    installAttempts({ window: () => slowWindow.promise })
    inspectRun(3)
    await showManifest(1)

    fireEvent.click(within(detail()).getByRole('button', { name: 'Close selected run' }))
    expect(screen.queryByRole('region', { name: 'Selected run' })).toBeNull()
    inspectRun(3)

    expectNoAttemptData()
    await act(async () => {
      slowWindow.resolve(windowOf('late manifest'))
      await slowWindow.promise
    })
    expectNoAttemptData()
    expect(windowRead).toHaveBeenCalledTimes(1)
  })

  it('ends the attempt inspection on a run history reload, and nothing of the earlier lifetime appears after the run is selected again', async () => {
    const slowEvidence = deferred<AgentAttemptEvidenceResponse>()
    installAttempts({ evidence: () => slowEvidence.promise })
    inspectRun(3)
    toggleAttempts()
    await waitFor(() => expect(attemptRows()).toHaveLength(2))
    inspectAttempt(2)
    expect(within(attempts()).getByText('Loading attempt evidence…')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Reload latest runs' }))
    expect(screen.queryByRole('region', { name: 'Selected run' })).toBeNull()
    await screen.findByRole('button', { name: 'Inspect run #3' })
    inspectRun(3)

    expectNoAttemptData()
    await act(async () => {
      slowEvidence.resolve(attemptEvidenceFor('run-3', 2))
      await slowEvidence.promise
    })
    expectNoAttemptData()
    expect(runHistoryRead).toHaveBeenCalledTimes(2)
    expect(evidenceRead).toHaveBeenCalledTimes(1)
  })

  it('ends the attempt inspection when the history is collapsed or another project replaces it', async () => {
    const slowHistory = deferred<AgentAttemptHistoryResponse>()
    installAttempts({ history: () => slowHistory.promise })
    inspectRun(3)
    toggleAttempts()

    fireEvent.click(screen.getByRole('button', { name: 'Hide run history' }))
    fireEvent.click(screen.getByRole('button', { name: 'Show run history' }))
    await screen.findByRole('button', { name: 'Inspect run #3' })
    inspectRun(3)
    expectNoAttemptData()
    await act(async () => {
      slowHistory.resolve(attemptPageOf('run-3', [7, 6]))
      await slowHistory.promise
    })
    expectNoAttemptData()
    expect(historyRead).toHaveBeenCalledTimes(1)
  })

  it('ends the attempt inspection when another project replaces the history, and a return to the project starts collapsed', async () => {
    const slowWindow = deferred<SealedAgentArtifactWindowResponse>()
    installAttempts({ window: () => slowWindow.promise })
    inspectRun(3)
    await showManifest(1)

    view.rerender(<ProjectRunHistoryPanel projectId="project-2" />)
    expect(screen.queryByRole('region', { name: 'Selected run' })).toBeNull()
    view.rerender(<ProjectRunHistoryPanel projectId="project-1" />)
    expect(screen.getByRole('button', { name: 'Show run history' })).toHaveAttribute('aria-expanded', 'false')
    await act(async () => {
      slowWindow.resolve(windowOf('late manifest'))
      await slowWindow.promise
    })

    fireEvent.click(screen.getByRole('button', { name: 'Show run history' }))
    await screen.findByRole('button', { name: 'Inspect run #3' })
    inspectRun(3)
    expectNoAttemptData()
    expect(screen.queryByText(/late manifest/)).toBeNull()
    expect(historyRead).toHaveBeenCalledTimes(1)
    expect(windowRead).toHaveBeenCalledTimes(1)
  })

  it('settles nothing and reports no React error when attempt reads answer after the detail unmounted', async () => {
    const slowEvidence = deferred<AgentAttemptEvidenceResponse>()
    installAttempts({ evidence: () => slowEvidence.promise })
    const consoleError = vi.spyOn(console, 'error')
    inspectRun(3)
    toggleAttempts()
    await waitFor(() => expect(attemptRows()).toHaveLength(2))
    inspectAttempt(1)

    view.unmount()
    await act(async () => {
      slowEvidence.resolve(attemptEvidenceFor('run-3', 1))
      await slowEvidence.promise
    })

    expect(consoleError).not.toHaveBeenCalled()
    expect(evidenceRead).toHaveBeenCalledTimes(1)
    expect(windowRead).not.toHaveBeenCalled()
    consoleError.mockRestore()
  })

  /** Opens the attempts of the selected run, selects `attemptNumber` and opens its recorded collaboration inputs. */
  async function openInputs(attemptNumber: number) {
    toggleAttempts()
    await waitFor(() => expect(attemptRows().length).toBeGreaterThan(0))
    inspectAttempt(attemptNumber)
    const section = await within(attempts()).findByRole('region', { name: 'Recorded collaboration inputs' })
    fireEvent.click(within(section).getByRole('button', { name: 'Recorded collaboration inputs' }))
  }

  it("shows each historical run's own recorded inputs and none of a replaced run, reload or project in a successor's first frame", async () => {
    const late = deferred<unknown>()
    installAttempts({ inputs: (runId, attemptId) => (runId === 'run-3' ? late.promise : defaults.inputs(runId, attemptId)) })
    inspectRun(2)
    await openInputs(1)
    expect(await within(attempts()).findByText('2 recorded inputs.')).toBeInTheDocument()
    expect(within(attempts()).getAllByRole('listitem').some((item) => item.textContent?.includes('run-2-attempt-1-input-0'))).toBe(true)

    inspectRun(3)
    expectNoAttemptData()
    await openInputs(2)
    expect(within(attempts()).getByText('Loading recorded collaboration inputs…')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Reload latest runs' }))
    await screen.findByRole('button', { name: 'Inspect run #3' })
    inspectRun(3)
    expectNoAttemptData()
    view.rerender(<ProjectRunHistoryPanel projectId="project-2" />)
    view.rerender(<ProjectRunHistoryPanel projectId="project-1" />)
    await act(async () => {
      late.resolve(recordedInputsFor('run-3', 2, 9))
      await late.promise
    })

    expect(screen.queryByText(/9 recorded inputs/)).toBeNull()
    expect(screen.queryByText(/run-3-attempt-2-input/)).toBeNull()
    // ADR-0053: each opening asks for the whole bounded set once, with the explicit limit.
    expect(inputsRead.mock.calls).toEqual([
      ['run-2', attemptIdFor('run-2', 1), 64],
      ['run-3', attemptIdFor('run-3', 2), 64],
    ])
    expect(windowRead).not.toHaveBeenCalled()
  })

  it('settles no recorded-input read and reports no React error after the detail unmounted', async () => {
    const late = deferred<unknown>()
    installAttempts({ inputs: () => late.promise })
    const consoleError = vi.spyOn(console, 'error')
    inspectRun(3)
    await openInputs(1)

    view.unmount()
    await act(async () => {
      late.resolve(recordedInputsFor('run-3', 1, 2))
      await late.promise
    })

    expect(consoleError).not.toHaveBeenCalled()
    expect(inputsRead).toHaveBeenCalledTimes(1)
    consoleError.mockRestore()
  })

  it('shows an identity-invalid attempt without evidence, an incoherent answer without text, and retries without duplicating rows', async () => {
    let historyReads = 0
    installAttempts({
      history: (runId) => {
        historyReads += 1
        return historyReads === 1
          ? Promise.resolve(attemptPageOf(runId, [1, 2]))
          : Promise.resolve({
            ...attemptPageOf(runId, []),
            items: [
              attemptPageOf(runId, [2]).items![0],
              { ...attemptPageOf(runId, [1]).items![0], identityValid: false, role: undefined, provider: undefined, responseContract: undefined, outcome: undefined },
            ],
          } as AgentAttemptHistoryResponse)
      },
      evidence: (runId) => Promise.resolve(attemptEvidenceFor(runId, 9)),
    })
    inspectRun(3)
    toggleAttempts()

    expect(await within(attempts()).findByText(/not coherent, so none of it is shown/)).toBeInTheDocument()
    expect(attemptRows()).toHaveLength(0)
    fireEvent.click(within(attempts()).getByRole('button', { name: 'Retry' }))
    await waitFor(() => expect(attemptRows()).toHaveLength(2))
    expect(attemptRows()[1]).toHaveTextContent('Attempt #1 · Failed · identity could not be verified')
    expect(within(attempts()).queryByRole('button', { name: 'Inspect attempt 1' })).toBeNull()

    inspectAttempt(2)
    expect(await within(attempts()).findByText(/not coherent for this attempt, so none of it is shown/)).toBeInTheDocument()
    expect(within(attempts()).queryByRole('combobox')).toBeNull()
    expect(windowRead).not.toHaveBeenCalled()
  })

  it('shows an explicit error and no metadata, purpose or text for evidence whose identity contradicts the selected row', async () => {
    // HATT-R1: a recognized, internally coherent identity that is not the selected Planner · Codex · Proposal row.
    installAttempts({
      evidence: (runId, attemptId) =>
        Promise.resolve(
          attemptEvidenceFor(runId, Number(attemptId.split('-').pop()), ['AgentContextManifest', 'AgentFinalResponse'], {
            role: 'Implementer',
            provider: 'ClaudeCode',
            responseContract: 'ImplementationReport',
          }),
        ),
    })
    inspectRun(3)
    toggleAttempts()
    await waitFor(() => expect(attemptRows()).toHaveLength(2))
    inspectAttempt(1)

    const evidenceDetail = await within(attempts()).findByRole('region', { name: 'Selected Agent attempt evidence' })
    expect(await within(evidenceDetail).findByText(/not coherent for this attempt, so none of it is shown/)).toBeInTheDocument()
    expect(within(evidenceDetail).getByRole('button', { name: 'Retry' })).toBeInTheDocument()
    expect(within(evidenceDetail).queryByRole('combobox')).toBeNull()
    expect(evidenceDetail.textContent).not.toMatch(/bytes|Implementer|ClaudeCode|Context manifest|Final response/)
    expect(detail().querySelector('pre')).toBeNull()
    expect(windowRead).not.toHaveBeenCalled()
  })

  it('shows the evidence of an attempt whose recorded status advanced after the page was read', async () => {
    installAttempts({
      history: (runId) =>
        Promise.resolve({
          ...attemptPageOf(runId, []),
          items: [{ ...attemptPageOf(runId, [1]).items![0], status: 'Running', outcome: undefined }],
        } as AgentAttemptHistoryResponse),
      evidence: (runId) => Promise.resolve(attemptEvidenceFor(runId, 1, ['AgentContextManifest'], { attemptStatus: 'Completed', outcome: 'Proposed' })),
    })
    inspectRun(3)
    toggleAttempts()
    await waitFor(() => expect(attemptRows()).toHaveLength(1))
    expect(attemptRows()[0]).toHaveTextContent('Attempt #1 · Planner · Codex · Running')

    inspectAttempt(1)

    expect(await within(attempts()).findByText(/Attempt #1 · Planner · Codex · Completed · Result: Proposed/)).toBeInTheDocument()
    expect(within(attempts()).getByText(/Context manifest: 21 bytes/)).toBeInTheDocument()
  })

  it("shows each historical run's own recorded execution facts, never another run's, without any further read", async () => {
    const dispatchedAtUtc = new Date(Date.UTC(2026, 9, 9, 10, 1, 2))
    installAttempts({
      evidence: (runId, attemptId) =>
        Promise.resolve(
          attemptEvidenceFor(runId, Number(attemptId.split('-').pop()), ['AgentContextManifest'], {
            attemptStatus: runId === 'run-3' ? 'Completed' : 'Failed',
            outcome: runId === 'run-3' ? 'Proposed' : 'AccountUsageStopReached',
            // Run 2's attempt stopped before dispatch; its answer carries plausible numbers that must not be shown as known.
            dispatchedAtUtc: runId === 'run-3' ? dispatchedAtUtc : undefined,
            processExecution: new AgentProcessExecutionResponse({ outcome: 'Exited', exitCode: 0, durationMilliseconds: 1500, timeoutMilliseconds: 600_000 }),
            tokenUsage: new AgentTokenUsageResponse({ inputTokens: 300, outputTokens: 20 }),
          }),
        ),
    })
    const facts = () => within(attempts()).getByRole('region', { name: 'Recorded execution facts' })
    inspectRun(3)
    toggleAttempts()
    await waitFor(() => expect(attemptRows()).toHaveLength(2))
    inspectAttempt(1)
    await within(attempts()).findByRole('region', { name: 'Recorded execution facts' })

    expect(facts()).toHaveTextContent('Response contract: Proposal')
    expect(facts()).toHaveTextContent('dispatched 2026-10-09 10:01:02 UTC')
    expect(facts()).toHaveTextContent('Semantic result: Proposed')
    expect(facts()).toHaveTextContent('Process exited with code 0 after 1.5 s')
    expect(facts()).toHaveTextContent('Configured timeout: 10m 00s')
    expect(facts()).toHaveTextContent('Tokens: 300 input · 20 output')

    inspectRun(2)
    expectNoAttemptData()
    toggleAttempts()
    await waitFor(() => expect(attemptRows()).toHaveLength(2))
    inspectAttempt(1)
    await within(attempts()).findByRole('region', { name: 'Recorded execution facts' })

    expect(facts()).toHaveTextContent('not dispatched')
    expect(facts()).toHaveTextContent('Semantic result: AccountUsageStopReached')
    expect(facts()).toHaveTextContent('Process not started')
    expect(facts()).toHaveTextContent('Token usage: none (provider not invoked)')
    expect(facts()).toHaveTextContent('Configured timeout: 10m 00s')
    expect(facts().textContent).not.toMatch(/exited|1\.5 s|300 input|Proposed/)
    expect(evidenceRead.mock.calls).toEqual([
      ['run-3', attemptIdFor('run-3', 1)],
      ['run-2', attemptIdFor('run-2', 1)],
    ])
    expect(windowRead).not.toHaveBeenCalled()
  })

  it('rejects an incoherent window of the historical attempt whole, keeps the accepted text and retries the same offset once', async () => {
    let reads = 0
    installAttempts({
      window: (_runId, _attemptId, _segment, from) => {
        reads += 1
        if (from === 0) {
          return Promise.resolve(windowOf('first ', 0, 10))
        }

        // The first answer for offset 6 overshoots the byte cap and the sealed length; the retry is coherent.
        return Promise.resolve(reads === 2 ? windowOf('x'.repeat(20000), 6) : windowOf('half', 6, 10))
      },
    })
    inspectRun(3)
    await showManifest(1)
    await waitFor(() => expect(within(attempts()).getByText('first')).toBeInTheDocument())

    fireEvent.click(within(attempts()).getByRole('button', { name: 'Load next window' }))
    expect(await within(attempts()).findByText('This artifact window is unavailable.')).toBeInTheDocument()
    expect(within(attempts()).queryByText(/xxxx/)).toBeNull()
    fireEvent.click(within(attempts()).getByRole('button', { name: 'Retry' }))

    await waitFor(() => expect(attempts().querySelector('pre')?.textContent).toBe('first half'))
    expect(windowRead.mock.calls.map((call) => [call[0], call[2], call[3]])).toEqual([
      ['run-3', 'context-manifest', 0],
      ['run-3', 'context-manifest', 6],
      ['run-3', 'context-manifest', 6],
    ])
  })

  it('renders hostile recorded text literally, stores nothing in the browser, mounts no mutation control and never polls', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const hostile = '<img src=x onerror="alert(1)"><script>alert(2)</script>'
    const setItem = vi.spyOn(Storage.prototype, 'setItem')
    installAttempts({ window: () => Promise.resolve(windowOf(hostile)) })
    inspectRun(3)
    await showManifest(1)
    await waitFor(() => expect(within(attempts()).getByText(hostile)).toBeInTheDocument())

    expect(detail().querySelector('img, script')).toBeNull()
    expect(setItem).not.toHaveBeenCalled()
    expect(window.location.href).not.toContain('manifest')
    const names = within(detail()).getAllByRole('button').map((button) => button.getAttribute('aria-label') ?? button.textContent)
    expect(names).toEqual([
      'Show recorded events',
      'Show recorded collaboration',
      'Hide Agent attempt history',
      'Inspect attempt 2',
      'Inspect attempt 1',
      'Close attempt',
      // HIST-INP-01: the collapsed, read-only recorded-input toggle of the selected attempt.
      'Recorded collaboration inputs',
      'Close selected run',
    ])
    expect(detail().querySelector('form, input, textarea, [role="textbox"]')).toBeNull()

    await act(async () => {
      await vi.advanceTimersByTimeAsync(10 * 60 * 1000)
    })
    expect(historyRead).toHaveBeenCalledTimes(1)
    expect(evidenceRead).toHaveBeenCalledTimes(1)
    expect(windowRead).toHaveBeenCalledTimes(1)
    expect(inputsRead).not.toHaveBeenCalled()
    setItem.mockRestore()
  })
})
