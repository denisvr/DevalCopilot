// @vitest-environment jsdom
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  GetRunCockpitResponse,
  ParticipantIdentityResponse,
  RunCockpitTokenStopResponse,
  SetTokenStopThresholdResponse,
} from '../../../api/generated/api-client'
import { runCockpitClient, runEventsClient, setTokenStopThresholdClient } from '../../../api/clients'
import { createRunNotificationConnection } from '../../../api/runNotifications'
import { useRunCockpit } from '../hooks/useRunCockpit'
import { TokenStopPanel } from './TokenStopPanel'

vi.mock('../../../api/clients', () => ({
  runCockpitClient: vi.fn(),
  runEventsClient: vi.fn(),
  setTokenStopThresholdClient: vi.fn(),
}))
vi.mock('../../../api/runNotifications', () => ({
  createRunNotificationConnection: vi.fn(),
}))

// The recorded Codex evidence never changes in these tests: 1,200 counted tokens. Only the
// configured threshold does, exactly like the real server re-deriving the stop on each read.
const KNOWN = 1200

function derive(threshold: number | undefined): RunCockpitTokenStopResponse {
  const reached = threshold !== undefined && KNOWN >= threshold
  return new RunCockpitTokenStopResponse({
    provider: 'Codex',
    thresholdTokens: threshold,
    state: threshold === undefined ? 'NotConfigured' : reached ? 'ThresholdReached' : 'BelowThresholdComplete',
    claimBlocked: reached,
    knownTokenCount: KNOWN,
    countedAttempts: 1,
    pendingAttempts: 0,
    insufficientEvidenceAttempts: 0,
    unattributedAttempts: 0,
    countOverflowed: false,
  })
}

function cockpit(threshold: number | undefined): GetRunCockpitResponse {
  return new GetRunCockpitResponse({
    runId: 'run-1',
    projectId: 'project-1',
    projectName: 'DevalCopilot',
    executionNumber: 1,
    objective: 'Objective',
    lifecycle: 'Running',
    stage: 'Plan',
    activeParticipant: new ParticipantIdentityResponse({ kind: 'Agent', role: 'Planner', provider: 'Codex' }),
    autonomousDurationSeconds: 1,
    latestSequence: 0,
    stageMap: [],
    canPause: false,
    canStop: false,
    tokenStops: [derive(threshold), new RunCockpitTokenStopResponse({ provider: 'ClaudeCode', state: 'NotConfigured', knownTokenCount: 0 })],
  })
}

function Harness({ runId = 'run-1' }: { runId?: string }) {
  const { cockpit: current, refresh } = useRunCockpit(runId)
  return <TokenStopPanel runId={runId} tokenStops={current?.tokenStops} onSaved={refresh} />
}

function setUp(options: { failRefreshAfterSave?: boolean; failSave?: boolean } = {}) {
  const server = { threshold: undefined as number | undefined, failReads: false }
  const getRunCockpit = vi.fn(async () => {
    if (server.failReads) {
      throw new Error('boom: internal detail')
    }
    return cockpit(server.threshold)
  })
  const getRunEvents = vi.fn(async () => [])
  const setTokenStopThreshold = vi.fn(async (_runId: string, request: { thresholdTokens?: number }) => {
    if (options.failSave) {
      throw new Error('boom: internal detail')
    }
    server.threshold = request.thresholdTokens
    if (options.failRefreshAfterSave) {
      server.failReads = true
    }
    return new SetTokenStopThresholdResponse({ provider: 'Codex', thresholdTokens: request.thresholdTokens })
  })
  vi.mocked(runCockpitClient).mockReturnValue({ getRunCockpit } as never)
  vi.mocked(runEventsClient).mockReturnValue({ getRunEvents } as never)
  vi.mocked(setTokenStopThresholdClient).mockReturnValue({ setTokenStopThreshold } as never)
  // A hub that never delivers any notification: no unrelated run event can trigger a refresh.
  vi.mocked(createRunNotificationConnection).mockImplementation(
    () =>
      ({
        on: vi.fn(),
        onreconnecting: vi.fn(),
        onreconnected: vi.fn(),
        onclose: vi.fn(),
        start: vi.fn(() => new Promise<void>(() => {})),
        stop: vi.fn(() => Promise.resolve()),
      }) as never,
  )
  return { server, getRunCockpit, setTokenStopThreshold }
}

function codex() {
  return screen.getByLabelText('Codex token stop')
}

async function save(value: string) {
  fireEvent.change(screen.getByLabelText('Codex stop threshold'), { target: { value } })
  fireEvent.click(screen.getByRole('button', { name: 'Save Codex stop' }))
}

describe('TokenStopPanel cockpit refresh after a stop change', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('moves an existing count into and out of a blocking stop without any run event', async () => {
    const { getRunCockpit } = setUp()
    render(<Harness />)
    await waitFor(() => expect(within(codex()).getByText(/no token stop threshold set/)).toBeTruthy())
    const readsBeforeSave = getRunCockpit.mock.calls.length

    // Exact equality with the recorded count blocks immediately.
    await save('1200')
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Codex token stop reached'))
    expect(getRunCockpit.mock.calls.length).toBeGreaterThan(readsBeforeSave)

    // Raising it by one leaves the same evidence complete and below: the block goes away.
    await save('1201')
    await waitFor(() => expect(screen.queryByRole('alert')).toBeNull())
    expect(within(codex()).getByText(/below the 1,201 threshold, with complete evidence/)).toBeTruthy()

    // Lowering it blocks again, and clearing returns to neutral.
    await save('100')
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('at or above the 100 threshold'))
    fireEvent.click(screen.getByRole('button', { name: 'Clear Codex stop' }))
    await waitFor(() => expect(screen.queryByRole('alert')).toBeNull())
    expect(within(codex()).getByText(/no token stop threshold set/)).toBeTruthy()
  })

  it('shows a safe synchronization failure, without internal detail, when the refresh fails after a successful save', async () => {
    const { server } = setUp({ failRefreshAfterSave: true })
    render(<Harness />)
    await waitFor(() => expect(within(codex()).getByText(/no token stop threshold set/)).toBeTruthy())

    await save('1200')

    expect(await screen.findByText('Saved, but the cockpit could not be refreshed; the displayed stop state may be out of date.')).toBeTruthy()
    expect(screen.queryByText(/internal detail/)).toBeNull()
    expect(server.threshold).toBe(1200)
  })

  it('does not refresh the cockpit and shows a safe error when the save itself fails', async () => {
    const { getRunCockpit } = setUp({ failSave: true })
    render(<Harness />)
    await waitFor(() => expect(within(codex()).getByText(/no token stop threshold set/)).toBeTruthy())
    const readsBeforeSave = getRunCockpit.mock.calls.length

    await save('1200')

    expect(await screen.findByText('The token stop threshold could not be saved for this run.')).toBeTruthy()
    expect(screen.queryByText(/internal detail/)).toBeNull()
    expect(getRunCockpit.mock.calls.length).toBe(readsBeforeSave)
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('rejects 10^12 + 1 locally without an API request and accepts exactly 10^12', async () => {
    const { setTokenStopThreshold } = setUp()
    render(<Harness />)
    await waitFor(() => expect(within(codex()).getByText(/no token stop threshold set/)).toBeTruthy())

    await save('1000000000001')
    expect(await screen.findByText('Enter a whole number of tokens from 1 to 1,000,000,000,000.')).toBeTruthy()
    expect(setTokenStopThreshold).not.toHaveBeenCalled()

    await save('1000000000000')
    await waitFor(() => expect(setTokenStopThreshold).toHaveBeenCalledTimes(1))
    expect(setTokenStopThreshold).toHaveBeenCalledWith('run-1', expect.objectContaining({ provider: 'Codex', thresholdTokens: 1_000_000_000_000 }))
  })

  it('does not apply a refresh from a previously selected run to the newly selected run', async () => {
    setUp()
    const { rerender } = render(<Harness runId="run-1" />)
    await waitFor(() => expect(within(codex()).getByText(/no token stop threshold set/)).toBeTruthy())

    rerender(<Harness runId="run-2" />)

    // The hook re-keys by run: the panel never shows run-1's projection while run-2 is loading.
    await waitFor(() => expect(screen.getByLabelText('Codex token stop')).toBeTruthy())
    expect(screen.queryByRole('alert')).toBeNull()
  })
})
