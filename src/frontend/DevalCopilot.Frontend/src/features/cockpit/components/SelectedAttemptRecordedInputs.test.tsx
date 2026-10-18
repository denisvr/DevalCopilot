import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  agentAttemptArtifactWindowClient,
  agentAttemptEvidenceClient,
  agentAttemptHistoryClient,
  agentAttemptInputsClient,
} from '../../../api/clients'
import { attemptEvidenceFor, attemptIdFor, attemptPageOf, windowOf } from '../agentAttemptHistoryFixture'
import { emptyInputsFor, inputReferenceFor, recordedInputsFor, unavailableInputsFor } from '../agentAttemptInputsFixture'
import { AgentAttemptHistoryPanel } from './AgentAttemptHistoryPanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
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

type InputsRead = (runId: string, attemptId: string) => Promise<unknown>

let historyRead: ReturnType<typeof vi.fn>
let evidenceRead: ReturnType<typeof vi.fn>
let windowRead: ReturnType<typeof vi.fn>
let inputsRead: ReturnType<typeof vi.fn<InputsRead>>

function install(inputs: InputsRead, evidence?: (runId: string, attemptId: string) => Promise<unknown>) {
  historyRead = vi.fn((runId: string) => Promise.resolve(attemptPageOf(runId, [2, 1])))
  evidenceRead = vi.fn(evidence ?? ((runId: string, attemptId: string) => Promise.resolve(attemptEvidenceFor(runId, Number(attemptId.split('-').pop())))))
  windowRead = vi.fn((runId: string, attemptId: string) => Promise.resolve(windowOf(`manifest of ${runId} ${attemptId}`)))
  inputsRead = vi.fn(inputs)
  vi.mocked(agentAttemptHistoryClient).mockReturnValue({ getAgentAttemptHistory: historyRead } as never)
  vi.mocked(agentAttemptEvidenceClient).mockReturnValue({ getAgentAttemptEvidence: evidenceRead } as never)
  vi.mocked(agentAttemptArtifactWindowClient).mockReturnValue({ getAgentAttemptArtifactWindow: windowRead } as never)
  vi.mocked(agentAttemptInputsClient).mockReturnValue({ readAgentAttemptInputs: inputsRead } as never)
}

const numberOf = (attemptId: string) => Number(attemptId.split('-').pop())
const recorded = (count: number) => (runId: string, attemptId: string) => Promise.resolve(recordedInputsFor(runId, numberOf(attemptId), count))

const detail = () => screen.getByRole('region', { name: 'Selected Agent attempt evidence' })
const inputs = () => within(detail()).getByRole('region', { name: 'Recorded collaboration inputs' })
const toggleInputs = () => fireEvent.click(within(inputs()).getByRole('button', { name: 'Recorded collaboration inputs' }))
const inputItems = () => within(inputs()).queryAllByRole('listitem')

async function selectAttempt(view: ReturnType<typeof render> | null, number: number) {
  if (view === null) {
    fireEvent.click(screen.getByRole('button', { name: 'Show Agent attempt history' }))
  }

  fireEvent.click(await screen.findByRole('button', { name: `Inspect attempt ${number}` }))
  await within(detail()).findByText(/Context manifest: 21 bytes/)
}

/** The inputs section of the current selection is collapsed and shows nothing of any earlier read. */
function expectCollapsedInputs() {
  expect(within(inputs()).getByRole('button', { name: 'Recorded collaboration inputs' })).toHaveAttribute('aria-expanded', 'false')
  expect(inputItems()).toHaveLength(0)
  expect(within(inputs()).queryByText(/recorded input/)).toBeNull()
  expect(within(inputs()).queryByText(/Loading recorded collaboration inputs/)).toBeNull()
}

describe('the recorded collaboration inputs of a selected attempt', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('is collapsed inside the selected attempt, reads once only when opened and shows the references with the caveat', async () => {
    install(recorded(3))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(null, 2)

    expectCollapsedInputs()
    expect(inputsRead).not.toHaveBeenCalled()

    toggleInputs()
    expect(within(inputs()).getByText('Loading recorded collaboration inputs…')).toBeInTheDocument()
    await waitFor(() => expect(inputItems()).toHaveLength(3))

    expect(inputsRead.mock.calls).toEqual([['run-1', attemptIdFor('run-1', 2)]])
    expect(within(inputs()).getByRole('button', { name: 'Recorded collaboration inputs' })).toHaveAttribute('aria-expanded', 'true')
    expect(within(inputs()).getByText(/never the complete prompt, complete context manifest, or a resumable provider session/)).toBeInTheDocument()
    expect(within(inputs()).getByText(/their content is not loaded/)).toBeInTheDocument()
    expect(within(inputs()).getByText('3 recorded inputs.')).toBeInTheDocument()
    const first = inputReferenceFor('run-1', 2, 0)
    expect(inputItems()[0]).toHaveTextContent(
      `Input 1: Proposal · message sequence ${first.collaborationSequence} · recorded ${first.occurredAtUtc} · message ${first.collaborationMessageId}`,
    )
    expect(inputItems()[1]).toHaveTextContent('Input 2: Challenge · message sequence 101')
    expect(inputItems()[2]).toHaveTextContent('Input 3: Execution report · message sequence 102')
    expect(evidenceRead).toHaveBeenCalledTimes(1)
    expect(windowRead).not.toHaveBeenCalled()

    toggleInputs()
    expectCollapsedInputs()
    expect(inputsRead).toHaveBeenCalledTimes(1)
  })

  it('discloses the exact total and that only the first ten of eleven are shown', async () => {
    install(recorded(11))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(null, 2)

    toggleInputs()

    expect(await within(inputs()).findByText('11 recorded inputs. Only the first 10 are shown.')).toBeInTheDocument()
    expect(inputItems()).toHaveLength(10)
  })

  it('renders an exact sequence above 2^53 and hostile reference text literally', async () => {
    install((runId, attemptId) => {
      const body = recordedInputsFor(runId, numberOf(attemptId), 2)
      body.inputMessages[1] = { ...body.inputMessages[1], collaborationSequence: '9007199254740993', collaborationMessageId: '<b>bold</b>' }
      return Promise.resolve(body)
    })
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(null, 2)

    toggleInputs()

    await waitFor(() => expect(inputItems()).toHaveLength(2))
    expect(inputItems()[1]).toHaveTextContent('message sequence 9007199254740993')
    expect(inputItems()[1]).toHaveTextContent('message <b>bold</b>')
    expect(inputs().querySelector('b')).toBeNull()
  })

  it.each([
    ['Empty', emptyInputsFor('run-1', 2), 'No recorded collaboration inputs — this attempt started from none.'],
    ['Unavailable', unavailableInputsFor('run-1', 2), /Recorded collaboration inputs unavailable.*This does not mean the attempt had no inputs/],
  ])('states %s truthfully without a list', async (_, body, text) => {
    install(() => Promise.resolve(body))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(null, 2)

    toggleInputs()

    expect(await within(inputs()).findByText(text)).toBeInTheDocument()
    expect(inputItems()).toHaveLength(0)
  })

  it('shows one fixed error and no partial list for a malformed answer, and retries the same attempt on request', async () => {
    const malformed = recordedInputsFor('run-1', 2, 3)
    malformed.inputMessages[2] = { ...malformed.inputMessages[2], collaborationSequence: '01' }
    install(vi.fn<InputsRead>().mockResolvedValueOnce(malformed).mockImplementation(recorded(3)))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(null, 2)

    toggleInputs()

    expect(
      await within(inputs()).findByText(/recorded collaboration inputs that are not coherent for this attempt, so none of them is shown/),
    ).toBeInTheDocument()
    expect(inputItems()).toHaveLength(0)
    expect(within(inputs()).queryByText(/recorded inputs\./)).toBeNull()

    fireEvent.click(within(inputs()).getByRole('button', { name: 'Try the recorded inputs again' }))
    await waitFor(() => expect(inputItems()).toHaveLength(3))
    expect(inputsRead.mock.calls).toEqual([
      ['run-1', attemptIdFor('run-1', 2)],
      ['run-1', attemptIdFor('run-1', 2)],
    ])
  })

  it('refuses an answer naming another attempt whole', async () => {
    install(() => Promise.resolve(recordedInputsFor('run-1', 1, 2)))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(null, 2)

    toggleInputs()

    expect(await within(inputs()).findByRole('status')).toHaveTextContent(/not coherent for this attempt/)
    expect(inputItems()).toHaveLength(0)
  })

  it('works without the evidence answer and leaves the evidence, facts and artifact viewer untouched', async () => {
    install(recorded(2), () => Promise.reject(new Error('evidence down')))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    fireEvent.click(screen.getByRole('button', { name: 'Show Agent attempt history' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Inspect attempt 2' }))
    expect(await within(detail()).findByText('The attempt evidence could not be loaded.')).toBeInTheDocument()

    toggleInputs()

    await waitFor(() => expect(inputItems()).toHaveLength(2))
    expect(evidenceRead).toHaveBeenCalledTimes(1)
    expect(windowRead).not.toHaveBeenCalled()
    expect(within(detail()).getByText('The attempt evidence could not be loaded.')).toBeInTheDocument()
  })

  it('keeps opened inputs and a loaded artifact window independent of each other', async () => {
    install(recorded(2))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(null, 2)
    toggleInputs()
    await waitFor(() => expect(inputItems()).toHaveLength(2))

    fireEvent.change(within(detail()).getByRole('combobox'), { target: { value: 'AgentContextManifest' } })
    fireEvent.click(within(detail()).getByRole('button', { name: 'Load' }))
    expect(await within(detail()).findByText(`manifest of run-1 ${attemptIdFor('run-1', 2)}`)).toBeInTheDocument()

    expect(inputItems()).toHaveLength(2)
    expect(inputsRead).toHaveBeenCalledTimes(1)
    expect(evidenceRead).toHaveBeenCalledTimes(1)
    toggleInputs()
    expect(within(detail()).getByText(`manifest of run-1 ${attemptIdFor('run-1', 2)}`)).toBeInTheDocument()
    expect(windowRead).toHaveBeenCalledTimes(1)
  })

  it('shows another attempt (A to B) collapsed in its first frame, and no late answer of A appears in B or in a return to A', async () => {
    const lateA = deferred<unknown>()
    let firstA = true
    install((runId, attemptId) =>
      numberOf(attemptId) === 2 && firstA ? ((firstA = false), lateA.promise) : recorded(numberOf(attemptId) + 1)(runId, attemptId))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(null, 2)
    toggleInputs()

    fireEvent.click(screen.getByRole('button', { name: 'Inspect attempt 1' }))
    expectCollapsedInputs()
    await act(async () => {
      lateA.resolve(recordedInputsFor('run-1', 2, 9))
      await lateA.promise
    })
    expectCollapsedInputs()
    await within(detail()).findByText(/Context manifest: 21 bytes/)

    fireEvent.click(screen.getByRole('button', { name: 'Inspect attempt 2' }))
    await within(detail()).findByText(/Context manifest: 21 bytes/)
    expectCollapsedInputs()
    toggleInputs()
    expect(await within(inputs()).findByText('3 recorded inputs.')).toBeInTheDocument()
    expect(within(inputs()).queryByText(/9 recorded inputs/)).toBeNull()
    expect(inputsRead.mock.calls.map(([, attemptId]) => numberOf(attemptId))).toEqual([2, 2])
  })

  it('ends with the attempt or the run: closing the attempt or another run shows nothing of it, and late answers settle nothing', async () => {
    const late = deferred<unknown>()
    install(() => late.promise)
    const view = render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(null, 2)
    toggleInputs()

    fireEvent.click(within(detail()).getByRole('button', { name: 'Close attempt' }))
    expect(screen.queryByRole('region', { name: 'Recorded collaboration inputs' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Inspect attempt 2' }))
    await within(detail()).findByText(/Context manifest: 21 bytes/)
    expectCollapsedInputs()

    toggleInputs()
    view.rerender(<AgentAttemptHistoryPanel runId="run-2" />)
    expect(screen.queryByRole('region', { name: 'Recorded collaboration inputs' })).toBeNull()
    await act(async () => {
      late.resolve(recordedInputsFor('run-1', 2, 5))
      await late.promise
    })
    expect(screen.queryByText(/recorded input/)).toBeNull()
    expect(inputsRead).toHaveBeenCalledTimes(2)
  })
})
