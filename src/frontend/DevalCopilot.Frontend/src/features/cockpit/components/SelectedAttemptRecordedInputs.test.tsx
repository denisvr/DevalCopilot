import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  agentAttemptArtifactWindowClient,
  agentAttemptEvidenceClient,
  agentAttemptHistoryClient,
  agentAttemptInputMessageClient,
  agentAttemptInputsClient,
} from '../../../api/clients'
import { attemptEvidenceFor, attemptIdFor, attemptPageOf, windowOf } from '../agentAttemptHistoryFixture'
import { emptyInputsFor, inputReferenceFor, recordedInputsFor, unavailableInputsFor } from '../agentAttemptInputsFixture'
import { inputFieldValue, recordedInputMessageFor, unavailableInputMessageFor } from '../agentAttemptInputMessageFixture'
import { MESSAGE_SCHEMAS } from '../collaborationHistory'
import { AgentAttemptHistoryPanel } from './AgentAttemptHistoryPanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  agentAttemptHistoryClient: vi.fn(),
  agentAttemptEvidenceClient: vi.fn(),
  agentAttemptArtifactWindowClient: vi.fn(),
  agentAttemptInputsClient: vi.fn(),
  agentAttemptInputMessageClient: vi.fn(),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}

type InputsRead = (runId: string, attemptId: string, limit?: number) => Promise<unknown>
type MessageRead = (runId: string, attemptId: string, messageId: string) => Promise<unknown>

let historyRead: ReturnType<typeof vi.fn>
let evidenceRead: ReturnType<typeof vi.fn>
let windowRead: ReturnType<typeof vi.fn>
let inputsRead: ReturnType<typeof vi.fn<InputsRead>>
let messageRead: ReturnType<typeof vi.fn<MessageRead>>

function install(inputs: InputsRead, evidence?: (runId: string, attemptId: string) => Promise<unknown>, message?: MessageRead) {
  historyRead = vi.fn((runId: string) => Promise.resolve(attemptPageOf(runId, [2, 1])))
  evidenceRead = vi.fn(evidence ?? ((runId: string, attemptId: string) => Promise.resolve(attemptEvidenceFor(runId, Number(attemptId.split('-').pop())))))
  windowRead = vi.fn((runId: string, attemptId: string) => Promise.resolve(windowOf(`manifest of ${runId} ${attemptId}`)))
  inputsRead = vi.fn(inputs)
  vi.mocked(agentAttemptHistoryClient).mockReturnValue({ getAgentAttemptHistory: historyRead } as never)
  vi.mocked(agentAttemptEvidenceClient).mockReturnValue({ getAgentAttemptEvidence: evidenceRead } as never)
  vi.mocked(agentAttemptArtifactWindowClient).mockReturnValue({ getAgentAttemptArtifactWindow: windowRead } as never)
  vi.mocked(agentAttemptInputsClient).mockReturnValue({ readAgentAttemptInputs: inputsRead } as never)
  messageRead = vi.fn(message ?? ((runId: string, attemptId: string, messageId: string) =>
    Promise.resolve(recordedInputMessageFor(runId, numberOf(attemptId), Number(messageId.split("-").pop())))))
  vi.mocked(agentAttemptInputMessageClient).mockReturnValue({ readAgentAttemptInputMessage: messageRead } as never)
}

const numberOf = (attemptId: string) => Number(attemptId.split('-').pop())
const recorded = (count: number) => (runId: string, attemptId: string) => Promise.resolve(recordedInputsFor(runId, numberOf(attemptId), count, {}, 64))

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

    expect(inputsRead.mock.calls).toEqual([['run-1', attemptIdFor('run-1', 2), 64]])
    expect(within(inputs()).getByRole('button', { name: 'Recorded collaboration inputs' })).toHaveAttribute('aria-expanded', 'true')
    expect(within(inputs()).getByText(/never the complete prompt, complete context manifest, or a resumable provider session/)).toBeInTheDocument()
    expect(within(inputs()).getByText(/the content of one input is read only when you inspect it/)).toBeInTheDocument()
    expect(messageRead).not.toHaveBeenCalled()
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

  it('reveals eleven loaded references as ten and then one more locally, labeled apart from any host omission, without a request', async () => {
    install(recorded(11))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(null, 2)

    toggleInputs()

    expect(await within(inputs()).findByText('11 recorded inputs.')).toBeInTheDocument()
    expect(inputItems()).toHaveLength(10)
    expect(within(inputs()).getByText(/Showing 10 of the 11 loaded references; the others are only hidden in this view, not missing\./))
      .toBeInTheDocument()
    expect(within(inputs()).queryByText(/The host returned only/)).toBeNull()
    fireEvent.click(within(inputs()).getByRole('button', { name: 'Show 1 more references' }))

    expect(inputItems()).toHaveLength(11)
    expect(inputItems()[10]).toHaveTextContent('Input 11:')
    expect(within(inputs()).queryByRole('button', { name: /more references/ })).toBeNull()
    expect(inputsRead).toHaveBeenCalledTimes(1)
    expect(messageRead).not.toHaveBeenCalled()
  })

  it('reveals sixty-four references in chunks of ten, resets the disclosure when closed and never prefetches content', async () => {
    install(recorded(64))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(null, 2)
    toggleInputs()
    await waitFor(() => expect(inputItems()).toHaveLength(10))

    const seen = [inputItems().length]
    for (const label of ['Show 10 more references', 'Show 10 more references', 'Show 10 more references', 'Show 10 more references', 'Show 10 more references', 'Show 4 more references']) {
      fireEvent.click(within(inputs()).getByRole('button', { name: label }))
      seen.push(inputItems().length)
    }

    expect(seen).toEqual([10, 20, 30, 40, 50, 60, 64])
    expect(inputItems()[63]).toHaveTextContent('Input 64:')
    expect(within(inputs()).getAllByRole('button', { name: /^Inspect input \d+$/ })).toHaveLength(64)
    expect(inputsRead).toHaveBeenCalledTimes(1)
    expect(messageRead).not.toHaveBeenCalled()

    toggleInputs()
    toggleInputs()
    await waitFor(() => expect(inputItems()).toHaveLength(10))
    expect(inputsRead).toHaveBeenCalledTimes(2)
    expect(messageRead).not.toHaveBeenCalled()
  })

  const inspector = () => within(inputs()).getByRole('region', { name: 'Selected recorded input' })
  const inspect = (position: number) => fireEvent.click(within(inputs()).getByRole('button', { name: `Inspect input ${position}` }))

  async function openedInputs(count = 12) {
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(null, 2)
    toggleInputs()
    await waitFor(() => expect(inputItems()).toHaveLength(Math.min(count, 10)))
  }

  it('inspects one explicitly chosen input once and renders its recorded content literally in the schema order with fixed labels', async () => {
    install(recorded(12), undefined, (runId, attemptId, messageId) => {
      const position = Number(messageId.split('-').pop())
      return Promise.resolve(recordedInputMessageFor(runId, numberOf(attemptId), position, {}, { summary: '<b>summary</b> of a recorded input.' }))
    })
    await openedInputs()

    inspect(2)

    expect(within(inspector()).getByText('Recorded input 2 · Challenge · message sequence 101')).toBeInTheDocument()
    expect(within(inspector()).getByText('Loading the recorded input…')).toBeInTheDocument()
    expect(await within(inspector()).findByText('Summary: <b>summary</b> of a recorded input.')).toBeInTheDocument()
    expect(inspector().querySelector('b')).toBeNull()
    expect(messageRead.mock.calls).toEqual([['run-1', attemptIdFor('run-1', 2), inputReferenceFor('run-1', 2, 1).collaborationMessageId]])
    expect(within(inspector()).getByText(/not the exact or complete prompt, not all the context and not proof of what the Agent saw/)).toBeInTheDocument()
    expect(within(inspector()).getByText(`Sequence 101 · Challenge · Protocol 1.0 · Provider-observed · recorded ${inputReferenceFor('run-1', 2, 1).occurredAtUtc}`))
      .toBeInTheDocument()
    expect(within(inspector()).getByText('From Agent · Planner role · Codex to Orchestrator')).toBeInTheDocument()
    const terms = within(inspector()).getAllByRole('term').map((term) => term.textContent)
    const definitions = within(inspector()).getAllByRole('definition').map((definition) => definition.textContent)
    expect(terms).toEqual(MESSAGE_SCHEMAS.Challenge.map(([, label]) => label))
    expect(definitions).toEqual(MESSAGE_SCHEMAS.Challenge.map(([name]) => inputFieldValue(name, 1)))
    expect(within(inputs()).getByRole('button', { name: 'Inspect input 2' })).toHaveAttribute('aria-pressed', 'true')

    // The current input again is a no-op; another input replaces it; Close then the same input again reads afresh.
    inspect(2)
    expect(messageRead).toHaveBeenCalledTimes(1)
    expect(within(inputs()).queryByRole('button', { name: 'Inspect input 12' })).toBeNull()
    fireEvent.click(within(inputs()).getByRole('button', { name: 'Show 2 more references' }))
    expect(inspector()).toHaveTextContent('Recorded input 2 · Challenge')
    expect(messageRead).toHaveBeenCalledTimes(1)
    inspect(12)
    // The replacement's first frame shows its own identity and nothing of the previous input's content.
    expect(within(inspector()).getByText('Recorded input 12 · Challenge · message sequence 111')).toBeInTheDocument()
    expect(within(inspector()).queryByText(/Summary:/)).toBeNull()
    expect(within(inspector()).getByText('Loading the recorded input…')).toBeInTheDocument()
    expect(await within(inspector()).findByText(/^Sequence 111 · /)).toBeInTheDocument()
    fireEvent.click(within(inspector()).getByRole('button', { name: 'Close recorded input' }))
    expect(within(inputs()).queryByRole('region', { name: 'Selected recorded input' })).toBeNull()
    inspect(12)
    expect(within(inspector()).getByText('Loading the recorded input…')).toBeInTheDocument()
    await within(inspector()).findByText(/Summary:/)
    expect(messageRead).toHaveBeenCalledTimes(3)
    expect(evidenceRead).toHaveBeenCalledTimes(1)
    expect(windowRead).not.toHaveBeenCalled()
    expect(inputsRead).toHaveBeenCalledTimes(1)
  })

  it.each([
    ['unavailable content', () => Promise.resolve(recordedInputMessageFor('run-1', 2, 0, {}, { contentStatus: 'Unavailable', summary: null, fields: [] })),
      /Content unavailable: the recorded content is not recognized, coherent, safe or within the recorded bounds.*The recorded input reference itself is unchanged/],
    ['an unavailable input set', () => Promise.resolve(unavailableInputMessageFor('run-1', 2, 0)),
      /Recorded input unavailable: the recorded inputs of this attempt could not be verified as a whole.*does not mean the attempt had no inputs/],
    ['a not-found answer', () => Promise.reject(Object.assign(new Error('missing'), { status: 404 })), /This input is no longer recorded for this attempt\./],
    ['an incoherent answer', () => Promise.resolve(recordedInputMessageFor('run-1', 2, 1)), /not coherent for this selection, so none of it is shown/],
    ['a read failure', () => Promise.reject(new Error('boom')), /The recorded input could not be read\./],
  ] as const)('shows %s as its own fixed state without any content', async (_, respond, text) => {
    install(recorded(3), undefined, respond)
    await openedInputs(3)

    inspect(1)

    expect(await within(inspector()).findByText(text)).toBeInTheDocument()
    expect(within(inspector()).queryByText(/Summary:/)).toBeNull()
    expect(within(inspector()).queryAllByRole('term')).toHaveLength(0)
    expect(inputItems()).toHaveLength(3)
  })

  it('retries a failed input read only on request and for the same input', async () => {
    const respond = vi.fn<MessageRead>()
      .mockRejectedValueOnce(new Error('boom'))
      .mockImplementation((runId, attemptId) => Promise.resolve(recordedInputMessageFor(runId, numberOf(attemptId), 0)))
    install(recorded(3), undefined, respond)
    await openedInputs(3)
    inspect(1)
    expect(await within(inspector()).findByText(/could not be read/)).toBeInTheDocument()
    expect(within(inspector()).queryByRole('button', { name: 'Try the recorded input again' })).toBeInTheDocument()

    fireEvent.click(within(inspector()).getByRole('button', { name: 'Try the recorded input again' }))

    expect(await within(inspector()).findByText('Summary: Recorded summary of input 0.')).toBeInTheDocument()
    expect(within(inspector()).queryByRole('button', { name: 'Try the recorded input again' })).toBeNull()
    expect(messageRead.mock.calls.map(([, , messageId]) => messageId)).toEqual([
      inputReferenceFor('run-1', 2, 0).collaborationMessageId,
      inputReferenceFor('run-1', 2, 0).collaborationMessageId,
    ])
  })

  it('ends the inspector with its opening, attempt or run, and a late content answer appears nowhere', async () => {
    const late = deferred<unknown>()
    install(recorded(3), undefined, () => late.promise)
    const view = render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(null, 2)
    toggleInputs()
    await waitFor(() => expect(inputItems()).toHaveLength(3))
    inspect(1)

    toggleInputs()
    toggleInputs()
    await waitFor(() => expect(inputItems()).toHaveLength(3))
    expect(within(inputs()).queryByRole('region', { name: 'Selected recorded input' })).toBeNull()

    inspect(1)
    fireEvent.click(screen.getByRole('button', { name: 'Inspect attempt 1' }))
    await within(detail()).findByText(/Context manifest: 21 bytes/)
    expectCollapsedInputs()
    view.rerender(<AgentAttemptHistoryPanel runId="run-2" />)
    await act(async () => {
      late.resolve(recordedInputMessageFor('run-1', 2, 0))
      await late.promise
    })

    expect(screen.queryByText(/Recorded summary of input/)).toBeNull()
    expect(screen.queryByRole('region', { name: 'Selected recorded input' })).toBeNull()
    expect(messageRead).toHaveBeenCalledTimes(2)
  })

  it('renders an exact sequence above 2^53 and hostile reference text literally', async () => {
    install((runId, attemptId) => {
      const body = recordedInputsFor(runId, numberOf(attemptId), 2, {}, 64)
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
    const malformed = recordedInputsFor('run-1', 2, 3, {}, 64)
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
      ['run-1', attemptIdFor('run-1', 2), 64],
      ['run-1', attemptIdFor('run-1', 2), 64],
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
