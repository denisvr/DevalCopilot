import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  agentAttemptArtifactWindowClient,
  agentAttemptEvidenceClient,
  agentAttemptHistoryClient,
  agentAttemptInputsClient,
  agentAttemptOutputMessageClient,
  agentAttemptOutputsClient,
} from '../../../api/clients'
import { attemptEvidenceFor, attemptIdFor, attemptPageOf, windowOf } from '../agentAttemptHistoryFixture'
import { recordedInputsFor } from '../agentAttemptInputsFixture'
import { outputFieldValue, recordedOutputMessageFor, unavailableOutputMessageFor } from '../agentAttemptOutputMessageFixture'
import { emptyOutputsFor, outputReferenceFor, recordedOutputsFor, unavailableOutputsFor } from '../agentAttemptOutputsFixture'
import { MESSAGE_SCHEMAS } from '../collaborationHistory'
import { AgentAttemptHistoryPanel } from './AgentAttemptHistoryPanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  agentAttemptHistoryClient: vi.fn(),
  agentAttemptEvidenceClient: vi.fn(),
  agentAttemptArtifactWindowClient: vi.fn(),
  agentAttemptInputsClient: vi.fn(),
  agentAttemptOutputsClient: vi.fn(),
  agentAttemptOutputMessageClient: vi.fn(),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}

type OutputsRead = (runId: string, attemptId: string) => Promise<unknown>
type MessageRead = (runId: string, attemptId: string, messageId: string) => Promise<unknown>

let historyRead: ReturnType<typeof vi.fn>
let evidenceRead: ReturnType<typeof vi.fn>
let windowRead: ReturnType<typeof vi.fn>
let inputsRead: ReturnType<typeof vi.fn>
let outputsRead: ReturnType<typeof vi.fn<OutputsRead>>
let messageRead: ReturnType<typeof vi.fn<MessageRead>>

const numberOf = (attemptId: string) => Number(attemptId.split('-').pop())
const positionOf = (messageId: string) => Number(messageId.split('-').pop())

function install(outputs: OutputsRead, evidence?: (runId: string, attemptId: string) => Promise<unknown>, message?: MessageRead) {
  historyRead = vi.fn((runId: string) => Promise.resolve(attemptPageOf(runId, [2, 1])))
  evidenceRead = vi.fn(evidence ?? ((runId: string, attemptId: string) => Promise.resolve(attemptEvidenceFor(runId, numberOf(attemptId)))))
  windowRead = vi.fn((runId: string, attemptId: string) => Promise.resolve(windowOf(`manifest of ${runId} ${attemptId}`)))
  inputsRead = vi.fn((runId: string, attemptId: string) => Promise.resolve(recordedInputsFor(runId, numberOf(attemptId), 2, {}, 64)))
  outputsRead = vi.fn(outputs)
  messageRead = vi.fn(message ?? ((runId: string, attemptId: string, messageId: string) =>
    Promise.resolve(recordedOutputMessageFor(runId, numberOf(attemptId), positionOf(messageId)))))
  vi.mocked(agentAttemptHistoryClient).mockReturnValue({ getAgentAttemptHistory: historyRead } as never)
  vi.mocked(agentAttemptEvidenceClient).mockReturnValue({ getAgentAttemptEvidence: evidenceRead } as never)
  vi.mocked(agentAttemptArtifactWindowClient).mockReturnValue({ getAgentAttemptArtifactWindow: windowRead } as never)
  vi.mocked(agentAttemptInputsClient).mockReturnValue({ readAgentAttemptInputs: inputsRead } as never)
  vi.mocked(agentAttemptOutputsClient).mockReturnValue({ readAgentAttemptOutputs: outputsRead } as never)
  vi.mocked(agentAttemptOutputMessageClient).mockReturnValue({ readAgentAttemptOutputMessage: messageRead } as never)
}

const recorded = (count: number) => (runId: string, attemptId: string) => Promise.resolve(recordedOutputsFor(runId, numberOf(attemptId), count))

const detail = () => screen.getByRole('region', { name: 'Selected Agent attempt evidence' })
const outputs = () => within(detail()).getByRole('region', { name: 'Recorded collaboration outputs' })
const toggleOutputs = () => fireEvent.click(within(outputs()).getByRole('button', { name: 'Recorded collaboration outputs' }))
const outputItems = () => within(outputs()).queryAllByRole('listitem')

async function selectAttempt(view: ReturnType<typeof render> | null, number: number) {
  if (view === null) {
    fireEvent.click(screen.getByRole('button', { name: 'Show Agent attempt history' }))
  }

  fireEvent.click(await screen.findByRole('button', { name: `Inspect attempt ${number}` }))
  await within(detail()).findByText(/Context manifest: 21 bytes/)
}

/** The outputs section of the current selection is collapsed and shows nothing of any earlier read. */
function expectCollapsedOutputs() {
  expect(within(outputs()).getByRole('button', { name: 'Recorded collaboration outputs' })).toHaveAttribute('aria-expanded', 'false')
  expect(outputItems()).toHaveLength(0)
  expect(within(outputs()).queryByText(/recorded output/)).toBeNull()
  expect(within(outputs()).queryByText(/Loading recorded collaboration outputs/)).toBeNull()
}

describe('the recorded collaboration outputs of a selected attempt', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('is collapsed inside the selected attempt, reads once only when opened and shows the references with the caveat', async () => {
    install(recorded(3))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(null, 2)

    expectCollapsedOutputs()
    expect(outputsRead).not.toHaveBeenCalled()

    toggleOutputs()
    expect(within(outputs()).getByText('Loading recorded collaboration outputs…')).toBeInTheDocument()
    await waitFor(() => expect(outputItems()).toHaveLength(3))

    expect(outputsRead.mock.calls).toEqual([['run-1', attemptIdFor('run-1', 2)]])
    expect(within(outputs()).getByRole('button', { name: 'Recorded collaboration outputs' })).toHaveAttribute('aria-expanded', 'true')
    expect(within(outputs()).getByText(/stored collaboration messages attributed to this attempt, as read now/)).toBeInTheDocument()
    expect(within(outputs()).getByText(/not proof of what it generated or saw, and neither a current approval nor proof that it succeeded/)).toBeInTheDocument()
    expect(within(outputs()).getByText(/the content of one output is read only when you inspect it/)).toBeInTheDocument()
    expect(messageRead).not.toHaveBeenCalled()
    expect(within(outputs()).getByText('3 recorded outputs.')).toBeInTheDocument()
    const first = outputReferenceFor('run-1', 2, 0)
    expect(outputItems()[0]).toHaveTextContent(
      `Output 1: Proposal · message sequence ${first.collaborationSequence} · recorded ${first.occurredAtUtc} · message ${first.collaborationMessageId}`,
    )
    expect(outputItems()[1]).toHaveTextContent('Output 2: Question · message sequence 101')
    expect(outputItems()[2]).toHaveTextContent('Output 3: Escalation · message sequence 102')
    expect(evidenceRead).toHaveBeenCalledTimes(1)
    expect(windowRead).not.toHaveBeenCalled()
    // The sibling inputs section is independent: nothing of it was read.
    expect(inputsRead).not.toHaveBeenCalled()

    toggleOutputs()
    expectCollapsedOutputs()
    expect(outputsRead).toHaveBeenCalledTimes(1)
  })

  it('reveals eleven loaded references as ten and then one more locally, never as host omission, without a request', async () => {
    install(recorded(11))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(null, 2)

    toggleOutputs()

    expect(await within(outputs()).findByText('11 recorded outputs.')).toBeInTheDocument()
    expect(outputItems()).toHaveLength(10)
    expect(within(outputs()).getByText(/Showing 10 of the 11 loaded references; the others are only hidden in this view, not missing\./))
      .toBeInTheDocument()
    expect(within(outputs()).queryByText(/The host returned only/)).toBeNull()
    fireEvent.click(within(outputs()).getByRole('button', { name: 'Show 1 more references' }))

    expect(outputItems()).toHaveLength(11)
    expect(outputItems()[10]).toHaveTextContent('Output 11:')
    expect(within(outputs()).queryByRole('button', { name: /more references/ })).toBeNull()
    expect(outputsRead).toHaveBeenCalledTimes(1)
    expect(messageRead).not.toHaveBeenCalled()
  })

  it('reveals sixty-four references in chunks of ten, resets the disclosure when closed and never prefetches content', async () => {
    install(recorded(64))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(null, 2)
    toggleOutputs()
    await waitFor(() => expect(outputItems()).toHaveLength(10))

    const seen = [outputItems().length]
    for (const label of ['Show 10 more references', 'Show 10 more references', 'Show 10 more references', 'Show 10 more references', 'Show 10 more references', 'Show 4 more references']) {
      fireEvent.click(within(outputs()).getByRole('button', { name: label }))
      seen.push(outputItems().length)
    }

    expect(seen).toEqual([10, 20, 30, 40, 50, 60, 64])
    expect(outputItems()[63]).toHaveTextContent('Output 64:')
    expect(within(outputs()).getAllByRole('button', { name: /^Inspect output \d+$/ })).toHaveLength(64)
    expect(outputsRead).toHaveBeenCalledTimes(1)
    expect(messageRead).not.toHaveBeenCalled()

    toggleOutputs()
    toggleOutputs()
    await waitFor(() => expect(outputItems()).toHaveLength(10))
    expect(outputsRead).toHaveBeenCalledTimes(2)
    expect(messageRead).not.toHaveBeenCalled()
  })

  const inspector = () => within(outputs()).getByRole('region', { name: 'Selected recorded output' })
  const inspect = (position: number) => fireEvent.click(within(outputs()).getByRole('button', { name: `Inspect output ${position}` }))

  async function openedOutputs(count = 12) {
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(null, 2)
    toggleOutputs()
    await waitFor(() => expect(outputItems()).toHaveLength(Math.min(count, 10)))
  }

  it('inspects one explicitly chosen output once and renders its recorded content literally in the schema order with fixed labels', async () => {
    install(recorded(12), undefined, (runId, attemptId, messageId) =>
      Promise.resolve(recordedOutputMessageFor(runId, numberOf(attemptId), positionOf(messageId), {}, { summary: '<b>summary</b> of a recorded output.' })))
    await openedOutputs()

    inspect(2)

    expect(within(inspector()).getByText('Recorded output 2 · Question · message sequence 101')).toBeInTheDocument()
    expect(within(inspector()).getByText('Loading the recorded output…')).toBeInTheDocument()
    expect(await within(inspector()).findByText('Summary: <b>summary</b> of a recorded output.')).toBeInTheDocument()
    expect(inspector().querySelector('b')).toBeNull()
    expect(messageRead.mock.calls).toEqual([['run-1', attemptIdFor('run-1', 2), outputReferenceFor('run-1', 2, 1).collaborationMessageId]])
    expect(within(inspector()).getByText(/stored collaboration message attributed to this attempt, read now when requested/)).toBeInTheDocument()
    expect(within(inspector()).getByText(/not the attempt's complete provider response, not proof of what it generated or saw/)).toBeInTheDocument()
    expect(within(inspector()).getByText(`Sequence 101 · Question · Protocol 1.0 · Provider-observed · recorded ${outputReferenceFor('run-1', 2, 1).occurredAtUtc}`))
      .toBeInTheDocument()
    expect(within(inspector()).getByText('From Agent · Planner role · Codex to Orchestrator')).toBeInTheDocument()
    const terms = within(inspector()).getAllByRole('term').map((term) => term.textContent)
    const definitions = within(inspector()).getAllByRole('definition').map((definition) => definition.textContent)
    expect(terms).toEqual(MESSAGE_SCHEMAS.Question.map(([, label]) => label))
    expect(definitions).toEqual(MESSAGE_SCHEMAS.Question.map(([name]) => outputFieldValue(name, 1)))
    expect(within(outputs()).getByRole('button', { name: 'Inspect output 2' })).toHaveAttribute('aria-pressed', 'true')

    // The current output again is a no-op; another output replaces it; Close then the same output again reads afresh.
    inspect(2)
    expect(messageRead).toHaveBeenCalledTimes(1)
    expect(within(outputs()).queryByRole('button', { name: 'Inspect output 12' })).toBeNull()
    fireEvent.click(within(outputs()).getByRole('button', { name: 'Show 2 more references' }))
    expect(inspector()).toHaveTextContent('Recorded output 2 · Question')
    expect(messageRead).toHaveBeenCalledTimes(1)
    inspect(12)
    // The replacement's first frame shows its own identity and nothing of the previous output's content.
    expect(within(inspector()).getByText('Recorded output 12 · Escalation · message sequence 111')).toBeInTheDocument()
    expect(within(inspector()).queryByText(/Summary:/)).toBeNull()
    expect(within(inspector()).getByText('Loading the recorded output…')).toBeInTheDocument()
    expect(await within(inspector()).findByText(/^Sequence 111 · /)).toBeInTheDocument()
    fireEvent.click(within(inspector()).getByRole('button', { name: 'Close recorded output' }))
    expect(within(outputs()).queryByRole('region', { name: 'Selected recorded output' })).toBeNull()
    inspect(12)
    expect(within(inspector()).getByText('Loading the recorded output…')).toBeInTheDocument()
    await within(inspector()).findByText(/Summary:/)
    expect(messageRead).toHaveBeenCalledTimes(3)
    expect(evidenceRead).toHaveBeenCalledTimes(1)
    expect(windowRead).not.toHaveBeenCalled()
    expect(outputsRead).toHaveBeenCalledTimes(1)
    expect(inputsRead).not.toHaveBeenCalled()
  })

  it.each([
    ['unavailable content', () => Promise.resolve(recordedOutputMessageFor('run-1', 2, 0, {}, { contentStatus: 'Unavailable', summary: null, fields: [] })),
      /Content unavailable: the recorded content is not recognized, coherent, safe or within the recorded bounds.*The recorded output reference itself is unchanged/],
    ['an unavailable output set', () => Promise.resolve(unavailableOutputMessageFor('run-1', 2, 0)),
      /Recorded output unavailable: the recorded outputs of this attempt could not be verified as a whole.*does not mean the attempt produced no output/],
    ['a not-found answer', () => Promise.reject(Object.assign(new Error('missing'), { status: 404 })), /This output is no longer recorded for this attempt\./],
    ['an incoherent answer', () => Promise.resolve(recordedOutputMessageFor('run-1', 2, 1)), /not coherent for this selection, so none of it is shown/],
    ['a read failure', () => Promise.reject(new Error('boom')), /The recorded output could not be read\./],
  ] as const)('shows %s as its own fixed state without any content', async (_, respond, text) => {
    install(recorded(3), undefined, respond)
    await openedOutputs(3)

    inspect(1)

    expect(await within(inspector()).findByText(text)).toBeInTheDocument()
    expect(within(inspector()).queryByText(/Summary:/)).toBeNull()
    expect(within(inspector()).queryAllByRole('term')).toHaveLength(0)
    expect(outputItems()).toHaveLength(3)
  })

  it('retries a failed output read only on request and for the same output', async () => {
    const respond = vi.fn<MessageRead>()
      .mockRejectedValueOnce(new Error('boom'))
      .mockImplementation((runId, attemptId) => Promise.resolve(recordedOutputMessageFor(runId, numberOf(attemptId), 0)))
    install(recorded(3), undefined, respond)
    await openedOutputs(3)
    inspect(1)
    expect(await within(inspector()).findByText(/could not be read/)).toBeInTheDocument()
    expect(within(inspector()).queryByRole('button', { name: 'Try the recorded output again' })).toBeInTheDocument()

    fireEvent.click(within(inspector()).getByRole('button', { name: 'Try the recorded output again' }))

    expect(await within(inspector()).findByText('Summary: Recorded summary of output 0.')).toBeInTheDocument()
    expect(within(inspector()).queryByRole('button', { name: 'Try the recorded output again' })).toBeNull()
    expect(messageRead.mock.calls.map(([, , messageId]) => messageId)).toEqual([
      outputReferenceFor('run-1', 2, 0).collaborationMessageId,
      outputReferenceFor('run-1', 2, 0).collaborationMessageId,
    ])
  })

  it('ends the inspector with its opening, attempt or run, and a late content answer appears nowhere', async () => {
    const late = deferred<unknown>()
    install(recorded(3), undefined, () => late.promise)
    const view = render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(null, 2)
    toggleOutputs()
    await waitFor(() => expect(outputItems()).toHaveLength(3))
    inspect(1)

    toggleOutputs()
    toggleOutputs()
    await waitFor(() => expect(outputItems()).toHaveLength(3))
    expect(within(outputs()).queryByRole('region', { name: 'Selected recorded output' })).toBeNull()

    inspect(1)
    fireEvent.click(screen.getByRole('button', { name: 'Inspect attempt 1' }))
    await within(detail()).findByText(/Context manifest: 21 bytes/)
    expectCollapsedOutputs()
    view.rerender(<AgentAttemptHistoryPanel runId="run-2" />)
    await act(async () => {
      late.resolve(recordedOutputMessageFor('run-1', 2, 0))
      await late.promise
    })

    expect(screen.queryByText(/Recorded summary of output/)).toBeNull()
    expect(screen.queryByRole('region', { name: 'Selected recorded output' })).toBeNull()
    expect(messageRead).toHaveBeenCalledTimes(2)
  })

  it('renders an exact sequence above 2^53 and hostile reference text literally', async () => {
    install((runId, attemptId) => {
      const body = recordedOutputsFor(runId, numberOf(attemptId), 2)
      body.outputMessages[1] = { ...body.outputMessages[1], collaborationSequence: '9007199254740993', collaborationMessageId: '<b>bold</b>' }
      return Promise.resolve(body)
    })
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(null, 2)

    toggleOutputs()

    await waitFor(() => expect(outputItems()).toHaveLength(2))
    expect(outputItems()[1]).toHaveTextContent('message sequence 9007199254740993')
    expect(outputItems()[1]).toHaveTextContent('message <b>bold</b>')
    expect(outputs().querySelector('b')).toBeNull()
  })

  it.each([
    ['Empty', emptyOutputsFor('run-1', 2), /No stored collaboration message is attributed to this attempt in this read\. This does not prove that the provider produced\s+no output/],
    ['Unavailable', unavailableOutputsFor('run-1', 2), /Recorded collaboration outputs unavailable.*This\s+does not mean the attempt produced no output/],
  ])('states %s truthfully without a list', async (_, body, text) => {
    install(() => Promise.resolve(body))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(null, 2)

    toggleOutputs()

    expect(await within(outputs()).findByText(text)).toBeInTheDocument()
    expect(outputItems()).toHaveLength(0)
  })

  it('shows one fixed error and no partial list for a malformed answer, and retries the same attempt on request', async () => {
    const malformed = recordedOutputsFor('run-1', 2, 3)
    malformed.outputMessages[2] = { ...malformed.outputMessages[2], collaborationSequence: '01' }
    install(vi.fn<OutputsRead>().mockResolvedValueOnce(malformed).mockImplementation(recorded(3)))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(null, 2)

    toggleOutputs()

    expect(
      await within(outputs()).findByText(/recorded collaboration outputs that are not coherent for this attempt, so none of them is shown/),
    ).toBeInTheDocument()
    expect(outputItems()).toHaveLength(0)
    expect(within(outputs()).queryByText(/recorded outputs\./)).toBeNull()

    fireEvent.click(within(outputs()).getByRole('button', { name: 'Try the recorded outputs again' }))
    await waitFor(() => expect(outputItems()).toHaveLength(3))
    expect(outputsRead.mock.calls).toEqual([
      ['run-1', attemptIdFor('run-1', 2)],
      ['run-1', attemptIdFor('run-1', 2)],
    ])
  })

  it('refuses a reference of a type the attempt role may not author as an incoherent answer whole', async () => {
    const body = recordedOutputsFor('run-1', 2, 3)
    body.outputMessages[2] = { ...body.outputMessages[2], type: 'Decision' }
    install(() => Promise.resolve(body))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(null, 2)

    toggleOutputs()

    expect(await within(outputs()).findByRole('status')).toHaveTextContent(/not coherent for this attempt/)
    expect(outputItems()).toHaveLength(0)
  })

  it('refuses an answer naming another attempt whole', async () => {
    install(() => Promise.resolve(recordedOutputsFor('run-1', 1, 2)))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(null, 2)

    toggleOutputs()

    expect(await within(outputs()).findByRole('status')).toHaveTextContent(/not coherent for this attempt/)
    expect(outputItems()).toHaveLength(0)
  })

  it('works without the evidence answer and leaves the evidence, facts and artifact viewer untouched', async () => {
    install(recorded(2), () => Promise.reject(new Error('evidence down')))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    fireEvent.click(screen.getByRole('button', { name: 'Show Agent attempt history' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Inspect attempt 2' }))
    expect(await within(detail()).findByText('The attempt evidence could not be loaded.')).toBeInTheDocument()

    toggleOutputs()

    await waitFor(() => expect(outputItems()).toHaveLength(2))
    expect(evidenceRead).toHaveBeenCalledTimes(1)
    expect(windowRead).not.toHaveBeenCalled()
    expect(within(detail()).getByText('The attempt evidence could not be loaded.')).toBeInTheDocument()
  })

  it('keeps opened outputs, opened inputs and a loaded artifact window independent of one another', async () => {
    install(recorded(2))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(null, 2)
    toggleOutputs()
    await waitFor(() => expect(outputItems()).toHaveLength(2))

    const inputsSection = within(detail()).getByRole('region', { name: 'Recorded collaboration inputs' })
    fireEvent.click(within(inputsSection).getByRole('button', { name: 'Recorded collaboration inputs' }))
    await waitFor(() => expect(inputsRead).toHaveBeenCalledTimes(1))
    fireEvent.change(within(detail()).getByRole('combobox'), { target: { value: 'AgentContextManifest' } })
    fireEvent.click(within(detail()).getByRole('button', { name: 'Load' }))
    expect(await within(detail()).findByText(`manifest of run-1 ${attemptIdFor('run-1', 2)}`)).toBeInTheDocument()

    expect(outputItems()).toHaveLength(2)
    expect(outputsRead).toHaveBeenCalledTimes(1)
    expect(evidenceRead).toHaveBeenCalledTimes(1)
    // Closing the inputs section leaves the outputs section as it is, and the reverse.
    fireEvent.click(within(inputsSection).getByRole('button', { name: 'Recorded collaboration inputs' }))
    expect(outputItems()).toHaveLength(2)
    toggleOutputs()
    expect(within(inputsSection).getByRole('button', { name: 'Recorded collaboration inputs' })).toHaveAttribute('aria-expanded', 'false')
    expect(within(detail()).getByText(`manifest of run-1 ${attemptIdFor('run-1', 2)}`)).toBeInTheDocument()
    expect(windowRead).toHaveBeenCalledTimes(1)
    expect(outputsRead).toHaveBeenCalledTimes(1)
  })

  it('shows another attempt (A to B) collapsed in its first frame, and no late answer of A appears in B or in a return to A', async () => {
    const lateA = deferred<unknown>()
    let firstA = true
    install((runId, attemptId) =>
      numberOf(attemptId) === 2 && firstA ? ((firstA = false), lateA.promise) : recorded(numberOf(attemptId) + 1)(runId, attemptId))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(null, 2)
    toggleOutputs()

    fireEvent.click(screen.getByRole('button', { name: 'Inspect attempt 1' }))
    expectCollapsedOutputs()
    await act(async () => {
      lateA.resolve(recordedOutputsFor('run-1', 2, 9))
      await lateA.promise
    })
    expectCollapsedOutputs()
    await within(detail()).findByText(/Context manifest: 21 bytes/)

    fireEvent.click(screen.getByRole('button', { name: 'Inspect attempt 2' }))
    await within(detail()).findByText(/Context manifest: 21 bytes/)
    expectCollapsedOutputs()
    toggleOutputs()
    expect(await within(outputs()).findByText('3 recorded outputs.')).toBeInTheDocument()
    expect(within(outputs()).queryByText(/9 recorded outputs/)).toBeNull()
    expect(outputsRead.mock.calls.map(([, attemptId]) => numberOf(attemptId))).toEqual([2, 2])
  })

  it('ends with the attempt or the run: closing the attempt or another run shows nothing of it, and late answers settle nothing', async () => {
    const late = deferred<unknown>()
    install(() => late.promise)
    const view = render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(null, 2)
    toggleOutputs()

    fireEvent.click(within(detail()).getByRole('button', { name: 'Close attempt' }))
    expect(screen.queryByRole('region', { name: 'Recorded collaboration outputs' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Inspect attempt 2' }))
    await within(detail()).findByText(/Context manifest: 21 bytes/)
    expectCollapsedOutputs()

    toggleOutputs()
    view.rerender(<AgentAttemptHistoryPanel runId="run-2" />)
    expect(screen.queryByRole('region', { name: 'Recorded collaboration outputs' })).toBeNull()
    await act(async () => {
      late.resolve(recordedOutputsFor('run-1', 2, 5))
      await late.promise
    })
    expect(screen.queryByText(/recorded output/)).toBeNull()
    expect(outputsRead).toHaveBeenCalledTimes(2)
  })
})
