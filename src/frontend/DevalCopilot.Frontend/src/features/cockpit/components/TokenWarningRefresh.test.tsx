// @vitest-environment jsdom
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  GetRunCockpitResponse,
  ParticipantIdentityResponse,
  RunCockpitTokenWarningResponse,
  SetTokenWarningThresholdResponse,
} from '../../../api/generated/api-client'
import { runCockpitClient, runEventsClient, setTokenWarningThresholdClient } from '../../../api/clients'
import { createRunNotificationConnection } from '../../../api/runNotifications'
import { useRunCockpit } from '../hooks/useRunCockpit'
import { TokenWarningPanel } from './TokenWarningPanel'

vi.mock('../../../api/clients', () => ({
  runCockpitClient: vi.fn(),
  runEventsClient: vi.fn(),
  setTokenWarningThresholdClient: vi.fn(),
}))
vi.mock('../../../api/runNotifications', () => ({
  createRunNotificationConnection: vi.fn(),
}))

// The recorded Codex evidence never changes in these tests: 1,200 counted tokens. Only the
// configured threshold does, exactly like the real server re-deriving the warning on each read.
const KNOWN = 1200

function derive(threshold: number | undefined): RunCockpitTokenWarningResponse {
  return new RunCockpitTokenWarningResponse({
    provider: 'Codex',
    thresholdTokens: threshold,
    state: threshold === undefined ? 'NotConfigured' : KNOWN >= threshold ? 'ThresholdReached' : 'BelowThresholdComplete',
    knownTokenCount: KNOWN,
    countedAttempts: 1,
    pendingAttempts: 0,
    insufficientEvidenceAttempts: 0,
    unattributedAttempts: 0,
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
    tokenWarnings: [derive(threshold), new RunCockpitTokenWarningResponse({ provider: 'ClaudeCode', state: 'NotConfigured', knownTokenCount: 0 })],
  })
}

function Harness() {
  const { cockpit: current, refresh } = useRunCockpit('run-1')
  return <TokenWarningPanel runId="run-1" tokenWarnings={current?.tokenWarnings} onSaved={refresh} />
}

function setUp(options: { failRefreshAfterSave?: boolean } = {}) {
  const server = { threshold: undefined as number | undefined, failReads: false }
  const getRunCockpit = vi.fn(async () => {
    if (server.failReads) {
      throw new Error('boom: internal detail')
    }
    return cockpit(server.threshold)
  })
  const getRunEvents = vi.fn(async () => [])
  const setTokenWarningThreshold = vi.fn(async (_runId: string, request: { thresholdTokens?: number }) => {
    server.threshold = request.thresholdTokens
    if (options.failRefreshAfterSave) {
      server.failReads = true
    }
    return new SetTokenWarningThresholdResponse({ provider: 'Codex', thresholdTokens: request.thresholdTokens })
  })
  vi.mocked(runCockpitClient).mockReturnValue({ getRunCockpit } as never)
  vi.mocked(runEventsClient).mockReturnValue({ getRunEvents } as never)
  vi.mocked(setTokenWarningThresholdClient).mockReturnValue({ setTokenWarningThreshold } as never)
  // A hub that never delivers any notification: no unrelated run event can trigger a refresh.
  const emitted: string[] = []
  vi.mocked(createRunNotificationConnection).mockImplementation(
    () =>
      ({
        on: (event: string) => {
          emitted.push(event)
        },
        onreconnecting: vi.fn(),
        onreconnected: vi.fn(),
        onclose: vi.fn(),
        start: vi.fn(() => new Promise<void>(() => {})),
        stop: vi.fn(() => Promise.resolve()),
      }) as never,
  )
  return { server, getRunCockpit, setTokenWarningThreshold }
}

function codex() {
  return screen.getByLabelText('Codex token-activity warning')
}

async function save(value: string) {
  fireEvent.change(screen.getByLabelText('Codex warning threshold'), { target: { value } })
  fireEvent.click(screen.getByRole('button', { name: 'Save Codex threshold' }))
}

describe('TokenWarningPanel cockpit refresh after a threshold change', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('moves an existing count into and out of ThresholdReached without any run event', async () => {
    const { getRunCockpit } = setUp()
    render(<Harness />)
    await waitFor(() => expect(within(codex()).getByText(/no token-activity warning threshold set \(1,200 reported tokens recorded so far\)/)).toBeTruthy())
    const readsBeforeSave = getRunCockpit.mock.calls.length

    // Exact equality with the recorded count warns immediately.
    await save('1200')
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('at or above the 1,200 threshold'))
    expect(getRunCockpit.mock.calls.length).toBeGreaterThan(readsBeforeSave)

    // Raising it by one leaves the same evidence complete and below: the alert goes away.
    await save('1201')
    await waitFor(() => expect(screen.queryByRole('alert')).toBeNull())
    expect(within(codex()).getByText(/below the 1,201 threshold, with complete evidence/)).toBeTruthy()

    // Lowering it warns again, and clearing returns to neutral.
    await save('100')
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('at or above the 100 threshold'))
    fireEvent.click(screen.getByRole('button', { name: 'Clear Codex threshold' }))
    await waitFor(() => expect(screen.queryByRole('alert')).toBeNull())
    expect(within(codex()).getByText(/no token-activity warning threshold set/)).toBeTruthy()
  })

  it('shows a safe synchronization failure, without internal detail, when the refresh fails after a successful save', async () => {
    const { server } = setUp({ failRefreshAfterSave: true })
    render(<Harness />)
    await waitFor(() => expect(within(codex()).getByText(/no token-activity warning threshold set/)).toBeTruthy())

    await save('1200')

    expect(await screen.findByText('Saved, but the cockpit could not be refreshed; the displayed warning may be out of date.')).toBeTruthy()
    expect(screen.queryByText(/internal detail/)).toBeNull()
    expect(server.threshold).toBe(1200)
  })

  it('rejects 10^12 + 1 locally without an API request and accepts exactly 10^12', async () => {
    const { setTokenWarningThreshold } = setUp()
    render(<Harness />)
    await waitFor(() => expect(within(codex()).getByText(/no token-activity warning threshold set/)).toBeTruthy())

    await save('1000000000001')
    expect(await screen.findByText('Enter a whole number of tokens from 1 to 1,000,000,000,000.')).toBeTruthy()
    expect(setTokenWarningThreshold).not.toHaveBeenCalled()

    await save('1000000000000')
    await waitFor(() => expect(setTokenWarningThreshold).toHaveBeenCalledTimes(1))
    expect(setTokenWarningThreshold).toHaveBeenCalledWith('run-1', expect.objectContaining({ provider: 'Codex', thresholdTokens: 1_000_000_000_000 }))
  })
})
