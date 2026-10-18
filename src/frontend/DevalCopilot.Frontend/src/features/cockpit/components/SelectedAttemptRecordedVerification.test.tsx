import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  agentAttemptArtifactWindowClient,
  agentAttemptEvidenceClient,
  agentAttemptHistoryClient,
  agentAttemptInputsClient,
  agentAttemptVerificationEvidenceClient,
  agentAttemptVerificationWindowClient,
} from '../../../api/clients'
import { AgentAttemptHistoryResponse } from '../../../api/generated/api-client'
import { attemptEntryFor, attemptEvidenceFor, attemptIdFor, attemptPageOf } from '../agentAttemptHistoryFixture'
import { recordedInputsFor } from '../agentAttemptInputsFixture'
import type { VerificationContract } from '../agentAttemptVerificationFixture'
import {
  artifactIdFor,
  executionIdFor,
  notRecordedStream,
  recordedStream,
  recordedVerificationFor,
  refusedVerificationWindow,
  unavailableStream,
  unavailableVerificationFor,
  verificationMemberFor,
  verificationWindowAnswer,
} from '../agentAttemptVerificationFixture'
import { AgentAttemptHistoryPanel } from './AgentAttemptHistoryPanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  agentAttemptHistoryClient: vi.fn(),
  agentAttemptEvidenceClient: vi.fn(),
  agentAttemptArtifactWindowClient: vi.fn(),
  agentAttemptInputsClient: vi.fn(),
  agentAttemptVerificationEvidenceClient: vi.fn(),
  agentAttemptVerificationWindowClient: vi.fn(),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}

type MetadataRead = (runId: string, attemptId: string) => Promise<unknown>
type WindowRead = (
  runId: string,
  attemptId: string,
  executionId: string,
  stream: 'stdout' | 'stderr',
  fromOffset: number,
  maxBytes: number,
) => Promise<unknown>

const numberOf = (attemptId: string) => Number(attemptId.split('-').pop())
const CONTRACTS: Record<number, VerificationContract> = { 1: 'VerificationDiagnosis', 2: 'ImplementationReview' }

let historyRead: ReturnType<typeof vi.fn>
let evidenceRead: ReturnType<typeof vi.fn>
let artifactWindowRead: ReturnType<typeof vi.fn>
let inputsRead: ReturnType<typeof vi.fn>
let metadataRead: ReturnType<typeof vi.fn<MetadataRead>>
let windowRead: ReturnType<typeof vi.fn<WindowRead>>

function installHistory(entries: (runId: string) => AgentAttemptHistoryResponse, evidence?: MetadataRead) {
  historyRead = vi.fn((runId: string) => Promise.resolve(entries(runId)))
  evidenceRead = vi.fn(
    evidence
      ?? ((runId: string, attemptId: string) =>
        Promise.resolve(
          attemptEvidenceFor(runId, numberOf(attemptId), ['AgentContextManifest'], {
            role: 'CodeReviewer',
            responseContract: CONTRACTS[numberOf(attemptId)],
          }),
        )),
  )
  artifactWindowRead = vi.fn()
  inputsRead = vi.fn((runId: string, attemptId: string) => Promise.resolve(recordedInputsFor(runId, numberOf(attemptId), 1)))
  vi.mocked(agentAttemptHistoryClient).mockReturnValue({ getAgentAttemptHistory: historyRead } as never)
  vi.mocked(agentAttemptEvidenceClient).mockReturnValue({ getAgentAttemptEvidence: evidenceRead } as never)
  vi.mocked(agentAttemptArtifactWindowClient).mockReturnValue({ getAgentAttemptArtifactWindow: artifactWindowRead } as never)
  vi.mocked(agentAttemptInputsClient).mockReturnValue({ readAgentAttemptInputs: inputsRead } as never)
}

const codeReviewerPage = (runId: string) =>
  new AgentAttemptHistoryResponse({
    items: [2, 1].map((number) => attemptEntryFor(runId, number, { role: 'CodeReviewer', responseContract: CONTRACTS[number] })),
    hasMore: false,
  })

function install(metadata: MetadataRead, windowAnswer?: WindowRead, evidence?: MetadataRead) {
  installHistory(codeReviewerPage, evidence)
  metadataRead = vi.fn(metadata)
  windowRead = vi.fn(windowAnswer ?? (() => Promise.reject(new Error('no window expected'))))
  vi.mocked(agentAttemptVerificationEvidenceClient).mockReturnValue({ readVerificationEvidence: metadataRead } as never)
  vi.mocked(agentAttemptVerificationWindowClient).mockReturnValue({ readVerificationWindow: windowRead } as never)
}

const recorded =
  (statuses: readonly ('Passed' | 'Failed')[]) =>
  (runId: string, attemptId: string) =>
    Promise.resolve(recordedVerificationFor(runId, numberOf(attemptId), statuses))

/** Metadata whose stdout of every member records `total` bytes, which an honest window answer must repeat as its total length. */
const recordedWithStdoutTotal =
  (statuses: readonly ('Passed' | 'Failed')[], total: number) =>
  (runId: string, attemptId: string) => {
    const answer = recordedVerificationFor(runId, numberOf(attemptId), statuses)
    answer.members = answer.members.map((member, position) => ({
      ...member,
      standardOutput: recordedStream(artifactIdFor(runId, numberOf(attemptId), position, 'stdout'), total),
    }))
    return Promise.resolve(answer)
  }

/** A host that serves `text` ASCII bytes of member 0's stdout of attempt 2 (total length `text.length`). */
function servingText(text: string): WindowRead & { total: number } {
  const reader: WindowRead = (runId, attemptId, executionId, stream, fromOffset, maxBytes) => {
    const total = Buffer.byteLength(text)
    const slice = text.slice(fromOffset, Math.min(fromOffset + maxBytes, total))
    return Promise.resolve(
      verificationWindowAnswer(
        { runId, attemptId, executionId, stream, descriptor: recordedStream(artifactIdFor(runId, numberOf(attemptId), 0, stream), total) as never },
        slice,
        fromOffset,
        { totalLength: total },
      ),
    )
  }
  return Object.assign(reader, { total: Buffer.byteLength(text) })
}

const detail = () => screen.getByRole('region', { name: 'Selected Agent attempt evidence' })
const section = () => within(detail()).getByRole('region', { name: 'Recorded verification evidence' })
const toggle = () => fireEvent.click(within(section()).getByRole('button', { name: 'Recorded verification evidence' }))
const view = (name: string) => within(section()).getByRole('button', { name })

async function selectAttempt(opened: boolean, number: number) {
  if (!opened) {
    fireEvent.click(screen.getByRole('button', { name: 'Show Agent attempt history' }))
  }

  fireEvent.click(await screen.findByRole('button', { name: `Inspect attempt ${number}` }))
  await within(detail()).findByText(/Context manifest: 21 bytes/)
}

function expectCollapsed() {
  expect(within(section()).getByRole('button', { name: 'Recorded verification evidence' })).toHaveAttribute('aria-expanded', 'false')
  expect(within(section()).queryByText(/recorded verification run/)).toBeNull()
  expect(within(section()).queryByText(/Loading recorded verification evidence/)).toBeNull()
  expect(within(section()).queryByText(/Standard output/)).toBeNull()
}

describe('the recorded verification evidence of a selected attempt', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('is offered for both supported CodeReviewer contracts and for no other attempt', async () => {
    install(recorded(['Passed']))
    const { unmount } = render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(false, 2)
    expect(section()).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Inspect attempt 1' }))
    await within(detail()).findByText(/Context manifest: 21 bytes/)
    expect(section()).toBeInTheDocument()
    unmount()

    installHistory((runId) => attemptPageOf(runId, [2]), (runId, attemptId) => Promise.resolve(attemptEvidenceFor(runId, numberOf(attemptId))))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    fireEvent.click(screen.getByRole('button', { name: 'Show Agent attempt history' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Inspect attempt 2' }))
    await within(detail()).findByText(/Context manifest: 21 bytes/)
    expect(within(detail()).queryByRole('region', { name: 'Recorded verification evidence' })).toBeNull()
    expect(within(detail()).getByRole('region', { name: 'Recorded collaboration inputs' })).toBeInTheDocument()
  })

  it('is collapsed, reads the metadata once only when opened and shows the caveat, checkpoint and ordered members', async () => {
    install(recorded(['Passed', 'Passed', 'Passed']))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(false, 2)

    expectCollapsed()
    expect(metadataRead).not.toHaveBeenCalled()
    expect(windowRead).not.toHaveBeenCalled()

    toggle()
    expect(within(section()).getByText('Loading recorded verification evidence…')).toBeInTheDocument()
    expect(await within(section()).findByText(/3 recorded verification runs\./)).toBeInTheDocument()

    expect(metadataRead.mock.calls).toEqual([['run-1', attemptIdFor('run-1', 2)]])
    expect(within(section()).getByRole('button', { name: 'Recorded verification evidence' })).toHaveAttribute('aria-expanded', 'true')
    expect(within(section()).getByText(/do not prove which bytes the Agent saw/)).toBeInTheDocument()
    expect(within(section()).getByText(/a historical Passed is never a current approval/)).toBeInTheDocument()
    expect(within(section()).getByText(/Recorded checkpoint #3 \(run-1-checkpoint\)/)).toBeInTheDocument()
    const first = verificationMemberFor('run-1', 2, 0)
    expect(within(section()).getByText(
      `Check 1: Command 1 · Passed (exit code 0) · execution #1 · claimed ${first.claimedAtUtc} · dispatched ${first.dispatchedAtUtc} · completed ${first.completedAtUtc}`,
    )).toBeInTheDocument()
    expect(within(section()).getByText(/^Check 2: Command 2 · Passed/)).toBeInTheDocument()
    expect(within(section()).getByText(/^Check 3: Command 3 · Passed/)).toBeInTheDocument()
    expect(within(section()).getAllByText('Standard output: 120 bytes · not truncated at capture')).toHaveLength(3)
    expect(within(section()).getAllByText('Standard error: not recorded.')).toHaveLength(3)
    expect(windowRead).not.toHaveBeenCalled()
    expect(inputsRead).not.toHaveBeenCalled()
    expect(artifactWindowRead).not.toHaveBeenCalled()
    expect(evidenceRead).toHaveBeenCalledTimes(1)

    toggle()
    expectCollapsed()
    expect(metadataRead).toHaveBeenCalledTimes(1)
  })

  it('shows failed and passed members of a diagnosis and each truthful capture disclosure', async () => {
    install((runId, attemptId) => {
      const answer = recordedVerificationFor(runId, numberOf(attemptId), ['Failed', 'Passed'])
      answer.members[0] = verificationMemberFor(runId, 1, 0, 'Failed', {
        standardOutput: recordedStream('a-out', 10, true),
        standardError: recordedStream('a-err', 0, null),
      })
      answer.members[1] = verificationMemberFor(runId, 1, 1, 'Passed', {
        standardOutput: unavailableStream(),
        standardError: notRecordedStream(),
      })
      return Promise.resolve(answer)
    })
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(false, 1)

    toggle()
    await within(section()).findByText(/2 recorded verification runs\./)

    expect(within(section()).getByText(/^Check 1: Command 1 · Failed \(exit code 1\)/)).toBeInTheDocument()
    expect(within(section()).getByText(/^Check 2: Command 2 · Passed \(exit code 0\)/)).toBeInTheDocument()
    expect(within(section()).getByText('Standard output: 10 bytes · truncated at capture')).toBeInTheDocument()
    expect(within(section()).getByText('Standard error: 0 bytes · truncation unknown (recovered after a host interruption)')).toBeInTheDocument()
    expect(within(section()).getByText('Standard output: recorded metadata unavailable.')).toBeInTheDocument()
    expect(within(section()).getByText('Standard error: not recorded.')).toBeInTheDocument()
    // Only a Recorded descriptor offers a stream selection.
    expect(within(section()).getAllByRole('button', { name: /^View / })).toHaveLength(2)
    expect(within(section()).queryByRole('button', { name: /of check 2$/ })).toBeNull()
  })

  it('renders hostile command names literally and never as markup', async () => {
    install((runId, attemptId) => {
      const answer = recordedVerificationFor(runId, numberOf(attemptId), ['Passed'])
      answer.members[0] = verificationMemberFor(runId, 2, 0, 'Passed', { commandName: '<img src=x onerror=alert(1)> **bold**' })
      return Promise.resolve(answer)
    })
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(false, 2)

    toggle()

    expect(await within(section()).findByText(/Check 1: <img src=x onerror=alert\(1\)> \*\*bold\*\* · Passed/)).toBeInTheDocument()
    expect(section().querySelector('img')).toBeNull()
    expect(section().querySelector('strong')).toBeNull()
  })

  it.each([
    [
      'Unavailable',
      (runId: string, attemptId: string) => Promise.resolve(unavailableVerificationFor(runId, numberOf(attemptId))),
      /Recorded verification evidence unavailable.*This does not mean the attempt recorded no verification/,
    ],
    ['a malformed answer', () => Promise.resolve({ status: 'Recorded' }), /not coherent for this attempt, so none of it is shown/],
    ['an answer naming another attempt', () => Promise.resolve(recordedVerificationFor('run-1', 1, ['Passed'])), /not coherent for this attempt/],
    ['a failed request', () => Promise.reject(new Error('boom')), 'The recorded verification evidence could not be loaded.'],
  ])('states %s as its own state without a list', async (_, respond, text) => {
    install(respond as MetadataRead)
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(false, 2)

    toggle()

    expect(await within(section()).findByText(text)).toBeInTheDocument()
    expect(within(section()).queryByText(/recorded verification run/)).toBeNull()
    expect(within(section()).queryByRole('button', { name: /^View / })).toBeNull()
  })

  it('shows no partial list for a member with an impossible tuple, and retries only on request', async () => {
    const broken = recordedVerificationFor('run-1', 2, ['Passed', 'Passed'])
    broken.members[1] = { ...broken.members[1], status: 'Failed' }
    install(vi.fn<MetadataRead>().mockResolvedValueOnce(broken).mockImplementation(recorded(['Passed', 'Passed'])))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(false, 2)

    toggle()
    expect(await within(section()).findByText(/not coherent for this attempt/)).toBeInTheDocument()
    expect(within(section()).queryByText(/Check 1/)).toBeNull()
    await act(async () => new Promise((resolve) => setTimeout(resolve, 20)))
    expect(metadataRead).toHaveBeenCalledTimes(1)

    fireEvent.click(within(section()).getByRole('button', { name: 'Try the recorded verification evidence again' }))
    expect(await within(section()).findByText(/2 recorded verification runs\./)).toBeInTheDocument()
    expect(metadataRead).toHaveBeenCalledTimes(2)
  })

  it('works without the evidence answer, the inputs and any produced message, and reads nothing else', async () => {
    install(recorded(['Passed']), undefined, () => Promise.reject(new Error('evidence down')))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    fireEvent.click(screen.getByRole('button', { name: 'Show Agent attempt history' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Inspect attempt 2' }))
    expect(await within(detail()).findByText('The attempt evidence could not be loaded.')).toBeInTheDocument()

    toggle()

    expect(await within(section()).findByText(/1 recorded verification run\./)).toBeInTheDocument()
    expect(evidenceRead).toHaveBeenCalledTimes(1)
    expect(inputsRead).not.toHaveBeenCalled()
    expect(artifactWindowRead).not.toHaveBeenCalled()
  })

  describe('manual output windows', () => {
    const OUTPUT = 'line one\nline two\n'

    async function openedWith(
      windowAnswer: WindowRead,
      total = (windowAnswer as { total?: number }).total ?? 120,
      statuses: readonly ('Passed' | 'Failed')[] = ['Passed', 'Passed'],
    ) {
      install(recordedWithStdoutTotal(statuses, total), windowAnswer)
      render(<AgentAttemptHistoryPanel runId="run-1" />)
      await selectAttempt(false, 2)
      toggle()
      await within(section()).findByText(/recorded verification runs?\./)
    }

    it('selects a stream without reading, loads one 16 KiB window on request and shows its text with the cursor', async () => {
      await openedWith(servingText(OUTPUT))

      fireEvent.click(view('View standard output of check 1'))
      expect(windowRead).not.toHaveBeenCalled()
      expect(view('Hide standard output of check 1')).toHaveAttribute('aria-pressed', 'true')

      fireEvent.click(within(section()).getByRole('button', { name: 'Load standard output of check 1' }))
      await waitFor(() => expect(section().querySelector('pre')).not.toBeNull())

      expect(windowRead.mock.calls).toEqual([['run-1', attemptIdFor('run-1', 2), executionIdFor('run-1', 2, 0), 'stdout', 0, 16384]])
      expect(section().querySelector('pre')?.textContent).toBe(OUTPUT)
      expect(within(section()).getByText(/18 of 18 bytes read\. This is the end of the recorded output\./)).toBeInTheDocument()
      expect(within(section()).queryByRole('button', { name: 'Load next window' })).toBeNull()
    })

    it('loads further windows only on request and appends each exactly once', async () => {
      const text = 'a'.repeat(16384) + 'b'.repeat(16384) + 'c'.repeat(1000)
      await openedWith(servingText(text))
      fireEvent.click(view('View standard output of check 1'))
      fireEvent.click(within(section()).getByRole('button', { name: 'Load standard output of check 1' }))
      await waitFor(() => expect(section().querySelector('pre')).not.toBeNull())

      expect(windowRead).toHaveBeenCalledTimes(1)
      expect(within(section()).getByText(/16384 of 33768 bytes read\./)).toBeInTheDocument()
      fireEvent.click(within(section()).getByRole('button', { name: 'Load next window' }))
      await waitFor(() => expect(within(section()).getByText(/32768 of 33768 bytes read\./)).toBeInTheDocument())
      fireEvent.click(within(section()).getByRole('button', { name: 'Load next window' }))
      await waitFor(() => expect(within(section()).getByText(/33768 of 33768 bytes read\. This is the end/)).toBeInTheDocument())

      expect(windowRead.mock.calls.map((call) => [call[4], call[5]])).toEqual([[0, 16384], [16384, 16384], [32768, 16384]])
      expect(section().querySelector('pre')?.textContent).toBe(text)
    })

    it('renders hostile output as literal text and never as markup', async () => {
      const hostile = '<script>alert(1)</script> <b>bold</b> [link](javascript:alert(1))'
      await openedWith(servingText(hostile))
      fireEvent.click(view('View standard output of check 1'))
      fireEvent.click(within(section()).getByRole('button', { name: 'Load standard output of check 1' }))
      await waitFor(() => expect(section().querySelector('pre')).not.toBeNull())

      expect(section().querySelector('pre')?.textContent).toBe(hostile)
      expect(section().querySelector('script')).toBeNull()
      expect(section().querySelector('b')).toBeNull()
      expect(section().querySelector('a')).toBeNull()
    })

    it('refuses a window whose length contradicts the recorded metadata instead of calling it an empty output', async () => {
      install(recorded(['Passed']), (runId, attemptId, executionId, stream, fromOffset) =>
        Promise.resolve(
          verificationWindowAnswer(
            { runId, attemptId, executionId, stream, descriptor: recordedStream(artifactIdFor(runId, 2, 0, stream), 0) as never },
            '',
            fromOffset,
          ),
        ))
      render(<AgentAttemptHistoryPanel runId="run-1" />)
      await selectAttempt(false, 2)
      toggle()
      await within(section()).findByText(/1 recorded verification run\./)
      // The metadata says 120 bytes while the sealed answer says 0: the answer is not coherent, never an empty output.
      fireEvent.click(view('View standard output of check 1'))
      fireEvent.click(within(section()).getByRole('button', { name: 'Load standard output of check 1' }))
      expect(await within(section()).findByText(/not coherent for this request, so none of it is shown/)).toBeInTheDocument()
      expect(within(section()).queryByText(/sealed output is empty/)).toBeNull()
    })

    it('states a truly empty output when the recorded length is zero', async () => {
      install(
        (runId, attemptId) => {
          const answer = recordedVerificationFor(runId, numberOf(attemptId), ['Passed'])
          answer.members[0] = verificationMemberFor(runId, 2, 0, 'Passed', { standardOutput: recordedStream('empty-out', 0) })
          return Promise.resolve(answer)
        },
        (runId, attemptId, executionId, stream, fromOffset) =>
          Promise.resolve(
            verificationWindowAnswer(
              { runId, attemptId, executionId, stream, descriptor: recordedStream('empty-out', 0) as never },
              '',
              fromOffset,
            ),
          ),
      )
      render(<AgentAttemptHistoryPanel runId="run-1" />)
      await selectAttempt(false, 2)
      toggle()
      await within(section()).findByText(/1 recorded verification run\./)

      fireEvent.click(view('View standard output of check 1'))
      fireEvent.click(within(section()).getByRole('button', { name: 'Load standard output of check 1' }))

      expect(await within(section()).findByText('This sealed output is empty (0 bytes).')).toBeInTheDocument()
      expect(section().querySelector('pre')).toBeNull()
    })

    it.each([
      ['Missing', /sealed file could not be found, so none of it is shown/],
      ['IntegrityMismatch', /does not match its recorded length and hash, so none of it is shown/],
      ['Unavailable', /recorded metadata or storage could not be admitted/],
      ['NotRecorded', /no longer recorded for this verification run/],
    ] as const)('states %s as its own refusal with no text', async (status, text) => {
      await openedWith((runId, attemptId, executionId, stream) =>
        Promise.resolve(refusedVerificationWindow({ runId, attemptId, executionId, stream }, status)))
      fireEvent.click(view('View standard output of check 1'))
      fireEvent.click(within(section()).getByRole('button', { name: 'Load standard output of check 1' }))

      expect(await within(section()).findByText(text)).toBeInTheDocument()
      expect(section().querySelector('pre')).toBeNull()
      expect(within(section()).queryByRole('button', { name: /^Load/ })).toBeNull()
    })

    it('keeps the loaded text through a failed next window and retries only on request, appending once', async () => {
      const text = 'a'.repeat(16384) + 'b'.repeat(500)
      const serve = servingText(text)
      let calls = 0
      await openedWith((...args) => (++calls === 2 ? Promise.reject(new Error('boom')) : serve(...args)), serve.total)
      fireEvent.click(view('View standard output of check 1'))
      fireEvent.click(within(section()).getByRole('button', { name: 'Load standard output of check 1' }))
      await within(section()).findByText(/16384 of 16884 bytes read\./)

      fireEvent.click(within(section()).getByRole('button', { name: 'Load next window' }))
      expect(await within(section()).findByText('This output window could not be loaded.')).toBeInTheDocument()
      expect(section().querySelector('pre')?.textContent).toHaveLength(16384)
      await act(async () => new Promise((resolve) => setTimeout(resolve, 20)))
      expect(windowRead).toHaveBeenCalledTimes(2)

      fireEvent.click(within(section()).getByRole('button', { name: 'Retry' }))
      await within(section()).findByText(/16884 of 16884 bytes read\. This is the end/)
      expect(windowRead.mock.calls.map((call) => call[4])).toEqual([0, 16384, 16384])
      expect(section().querySelector('pre')?.textContent).toBe(text)
    })

    it('discloses the 256 KiB local view limit as such and offers no further window', async () => {
      const total = 400_000
      const descriptor = recordedStream('big-out', total) as never
      install(
        (runId, attemptId) => {
          const answer = recordedVerificationFor(runId, numberOf(attemptId), ['Passed'])
          answer.members[0] = verificationMemberFor(runId, 2, 0, 'Passed', { standardOutput: descriptor })
          return Promise.resolve(answer)
        },
        (runId, attemptId, executionId, stream, fromOffset, maxBytes) =>
          Promise.resolve(
            verificationWindowAnswer(
              { runId, attemptId, executionId, stream, descriptor },
              'x'.repeat(Math.min(maxBytes, total - fromOffset)),
              fromOffset,
            ),
          ),
      )
      render(<AgentAttemptHistoryPanel runId="run-1" />)
      await selectAttempt(false, 2)
      toggle()
      await within(section()).findByText(/1 recorded verification run\./)
      fireEvent.click(view('View standard output of check 1'))
      fireEvent.click(within(section()).getByRole('button', { name: 'Load standard output of check 1' }))
      for (let window = 1; window < 16; window++) {
        fireEvent.click(await within(section()).findByRole('button', { name: 'Load next window' }))
        await waitFor(() => expect(windowRead).toHaveBeenCalledTimes(window + 1))
      }

      expect(await within(section()).findByText(/262144 of 400000 bytes read\./)).toBeInTheDocument()
      expect(within(section()).getByText(/keeps at most 256 KiB of source bytes of one output, so the rest is not loaded here/)).toBeInTheDocument()
      expect(within(section()).getByText(/local view limit, not the end of the capture or of the file/)).toBeInTheDocument()
      expect(within(section()).queryByText(/This is the end of the recorded output/)).toBeNull()
      expect(within(section()).queryByRole('button', { name: 'Load next window' })).toBeNull()
      expect(windowRead).toHaveBeenCalledTimes(16)
      expect(windowRead.mock.calls.every((call) => call[5] === 16384)).toBe(true)
    })

    it('replaces the window when another stream or member is chosen, starting at zero without the earlier text', async () => {
      install(
        (runId, attemptId) => {
          const answer = recordedVerificationFor(runId, numberOf(attemptId), ['Passed', 'Passed'])
          answer.members[0] = verificationMemberFor(runId, 2, 0, 'Passed', { standardError: recordedStream('first-err', 5) })
          return Promise.resolve(answer)
        },
        (runId, attemptId, executionId, stream, fromOffset) => {
          const descriptor =
            executionId === executionIdFor(runId, 2, 0) && stream === 'stderr'
              ? recordedStream('first-err', 5)
              : recordedStream(artifactIdFor(runId, 2, Number(executionId.split('-').pop()), 'stdout'), 120)
          const text = `${executionId}/${stream}`.padEnd(descriptor.byteLength, '.').slice(0, descriptor.byteLength)
          return Promise.resolve(verificationWindowAnswer({ runId, attemptId, executionId, stream, descriptor: descriptor as never }, text.slice(fromOffset), fromOffset))
        },
      )
      render(<AgentAttemptHistoryPanel runId="run-1" />)
      await selectAttempt(false, 2)
      toggle()
      await within(section()).findByText(/2 recorded verification runs\./)

      fireEvent.click(view('View standard output of check 1'))
      fireEvent.click(within(section()).getByRole('button', { name: 'Load standard output of check 1' }))
      await waitFor(() => expect(section().querySelector('pre')).not.toBeNull())
      expect(section().querySelector('pre')?.textContent).toMatch(/^run-1-attempt-2-execution-0\/stdout/)

      fireEvent.click(view('View standard error of check 1'))
      expect(section().querySelector('pre')).toBeNull()
      expect(within(section()).getByRole('button', { name: 'Load standard error of check 1' })).toBeInTheDocument()
      expect(windowRead).toHaveBeenCalledTimes(1)

      fireEvent.click(view('View standard output of check 2'))
      expect(section().querySelector('pre')).toBeNull()
      fireEvent.click(within(section()).getByRole('button', { name: 'Load standard output of check 2' }))
      await waitFor(() => expect(section().querySelector('pre')).not.toBeNull())
      expect(windowRead.mock.calls.map((call) => [call[2], call[3], call[4]])).toEqual([
        [executionIdFor('run-1', 2, 0), 'stdout', 0],
        [executionIdFor('run-1', 2, 1), 'stdout', 0],
      ])
      expect(section().querySelector('pre')?.textContent).toMatch(/^run-1-attempt-2-execution-1\/stdout/)

      fireEvent.click(view('Hide standard output of check 2'))
      expect(section().querySelector('pre')).toBeNull()
    })

    it('closing and reopening the section starts without the earlier selection or text, and a late window answer settles nothing', async () => {
      const late = deferred<unknown>()
      let first = true
      const serve = servingText(OUTPUT)
      await openedWith((...args) => (first ? ((first = false), late.promise) : serve(...args)), serve.total)
      fireEvent.click(view('View standard output of check 1'))
      fireEvent.click(within(section()).getByRole('button', { name: 'Load standard output of check 1' }))
      expect(windowRead).toHaveBeenCalledTimes(1)

      toggle()
      expectCollapsed()
      toggle()
      await within(section()).findByText(/2 recorded verification runs\./)
      expect(within(section()).queryByRole('button', { name: /^Load/ })).toBeNull()
      expect(section().querySelector('pre')).toBeNull()

      await act(async () => {
        late.resolve(servingText(OUTPUT)('run-1', attemptIdFor('run-1', 2), executionIdFor('run-1', 2, 0), 'stdout', 0, 16384))
        await late.promise
      })
      expect(section().querySelector('pre')).toBeNull()

      fireEvent.click(view('View standard output of check 1'))
      fireEvent.click(within(section()).getByRole('button', { name: 'Load standard output of check 1' }))
      await waitFor(() => expect(section().querySelector('pre')).not.toBeNull())
      expect(windowRead.mock.calls.map((call) => call[4])).toEqual([0, 0])
    })

    it('hiding and viewing the same stream again starts a fresh window at zero, and a late answer of the hidden view settles nothing', async () => {
      const late = deferred<unknown>()
      const serve = servingText(OUTPUT)
      let first = true
      await openedWith((...args) => (first ? ((first = false), late.promise) : serve(...args)), serve.total)
      fireEvent.click(view('View standard output of check 1'))
      fireEvent.click(within(section()).getByRole('button', { name: 'Load standard output of check 1' }))
      expect(windowRead).toHaveBeenCalledTimes(1)

      fireEvent.click(view('Hide standard output of check 1'))
      fireEvent.click(view('View standard output of check 1'))
      await act(async () => {
        late.resolve(serve('run-1', attemptIdFor('run-1', 2), executionIdFor('run-1', 2, 0), 'stdout', 0, 16384))
        await late.promise
      })
      expect(section().querySelector('pre')).toBeNull()
      expect(within(section()).getByRole('button', { name: 'Load standard output of check 1' })).toBeInTheDocument()
      await act(async () => new Promise((resolve) => setTimeout(resolve, 20)))
      expect(windowRead).toHaveBeenCalledTimes(1)

      fireEvent.click(within(section()).getByRole('button', { name: 'Load standard output of check 1' }))
      await waitFor(() => expect(section().querySelector('pre')).not.toBeNull())
      expect(windowRead.mock.calls.map((call) => call[4])).toEqual([0, 0])
      expect(section().querySelector('pre')?.textContent).toBe(OUTPUT)

      fireEvent.click(view('Hide standard output of check 1'))
      fireEvent.click(view('View standard output of check 1'))
      expect(section().querySelector('pre')).toBeNull()
      expect(windowRead).toHaveBeenCalledTimes(2)
    })

    it('never reads a window by itself over time', async () => {
      await openedWith(servingText('a'.repeat(40_000)))
      fireEvent.click(view('View standard output of check 1'))
      fireEvent.click(within(section()).getByRole('button', { name: 'Load standard output of check 1' }))
      await waitFor(() => expect(section().querySelector('pre')).not.toBeNull())

      await act(async () => new Promise((resolve) => setTimeout(resolve, 30)))

      expect(windowRead).toHaveBeenCalledTimes(1)
      expect(metadataRead).toHaveBeenCalledTimes(1)
    })
  })

  it('uses no browser storage and puts nothing in the URL', async () => {
    const setItem = vi.spyOn(Storage.prototype, 'setItem')
    const location = window.location.href
    install(recordedWithStdoutTotal(['Passed'], 11), servingText('output text'))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(false, 2)
    toggle()
    await within(section()).findByText(/1 recorded verification run\./)
    fireEvent.click(view('View standard output of check 1'))
    fireEvent.click(within(section()).getByRole('button', { name: 'Load standard output of check 1' }))
    await waitFor(() => expect(section().querySelector('pre')).not.toBeNull())

    expect(setItem).not.toHaveBeenCalled()
    expect(window.localStorage.length).toBe(0)
    expect(window.sessionStorage.length).toBe(0)
    expect(window.location.href).toBe(location)
    setItem.mockRestore()
  })

  it('is independent of the open inputs section in both directions', async () => {
    install(recorded(['Passed']), servingText('output'))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(false, 2)
    fireEvent.click(within(within(detail()).getByRole('region', { name: 'Recorded collaboration inputs' })).getByRole('button', { name: 'Recorded collaboration inputs' }))
    await waitFor(() => expect(inputsRead).toHaveBeenCalledTimes(1))

    toggle()
    await within(section()).findByText(/1 recorded verification run\./)
    toggle()

    expect(metadataRead).toHaveBeenCalledTimes(1)
    expect(inputsRead).toHaveBeenCalledTimes(1)
    expect(within(within(detail()).getByRole('region', { name: 'Recorded collaboration inputs' })).getByRole('button', { name: 'Recorded collaboration inputs' })).toHaveAttribute('aria-expanded', 'true')
  })

  it('shows another attempt collapsed in its first frame, and no late answer of the first appears in the second or in a return to the first', async () => {
    const lateA = deferred<unknown>()
    let firstA = true
    install((runId, attemptId) =>
      numberOf(attemptId) === 2 && firstA ? ((firstA = false), lateA.promise) : recorded(['Passed'])(runId, attemptId))
    render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(false, 2)
    toggle()

    fireEvent.click(screen.getByRole('button', { name: 'Inspect attempt 1' }))
    expectCollapsed()
    await act(async () => {
      lateA.resolve(recordedVerificationFor('run-1', 2, ['Passed', 'Passed', 'Passed']))
      await lateA.promise
    })
    expectCollapsed()
    await within(detail()).findByText(/Context manifest: 21 bytes/)

    fireEvent.click(screen.getByRole('button', { name: 'Inspect attempt 2' }))
    await within(detail()).findByText(/Context manifest: 21 bytes/)
    expectCollapsed()
    toggle()
    expect(await within(section()).findByText(/1 recorded verification run\./)).toBeInTheDocument()
    expect(within(section()).queryByText(/3 recorded verification runs/)).toBeNull()
  })

  it('ends with the attempt or the run: closing the attempt or another run shows nothing of it, and late answers settle nothing', async () => {
    const late = deferred<unknown>()
    install(() => late.promise, servingText('output'))
    const rendered = render(<AgentAttemptHistoryPanel runId="run-1" />)
    await selectAttempt(false, 2)
    toggle()

    fireEvent.click(within(detail()).getByRole('button', { name: 'Close attempt' }))
    expect(screen.queryByRole('region', { name: 'Recorded verification evidence' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Inspect attempt 2' }))
    await within(detail()).findByText(/Context manifest: 21 bytes/)
    expectCollapsed()

    toggle()
    rendered.rerender(<AgentAttemptHistoryPanel runId="run-2" />)
    expect(screen.queryByRole('region', { name: 'Recorded verification evidence' })).toBeNull()
    await act(async () => {
      late.resolve(recordedVerificationFor('run-1', 2, ['Passed']))
      await late.promise
    })
    expect(screen.queryByText(/recorded verification run/)).toBeNull()
    expect(metadataRead).toHaveBeenCalledTimes(2)
  })
})
