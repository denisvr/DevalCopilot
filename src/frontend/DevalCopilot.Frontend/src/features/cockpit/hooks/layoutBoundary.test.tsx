import { useLayoutEffect } from 'react'
import { act, render } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { runCockpitClient, runEventsClient, verificationExecutionOutputClient } from '../../../api/clients'
import { createRunNotificationConnection } from '../../../api/runNotifications'
import {
  GetRunCockpitResponse,
  ParticipantIdentityResponse,
  VerificationExecutionOutputQueryResult,
} from '../../../api/generated/api-client'
import { useRunCockpit } from './useRunCockpit'
import { useVerificationExecutionOutput } from './useVerificationExecutionOutput'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  runCockpitClient: vi.fn(),
  runEventsClient: vi.fn(),
  verificationExecutionOutputClient: vi.fn(),
}))
vi.mock('../../../api/runNotifications', () => ({ createRunNotificationConnection: vi.fn() }))

const cockpit = (runId: string) =>
  new GetRunCockpitResponse({
    runId,
    projectId: `project-of-${runId}`,
    objective: `objective of ${runId}`,
    lifecycle: 'Running',
    stage: 'Plan',
    activeParticipant: new ParticipantIdentityResponse({ kind: 'Agent', role: 'Planner', provider: 'Codex' }),
    latestSequence: 0,
    stageMap: [],
  })

afterEach(() => {
  vi.useRealTimers()
})

// A consumer layout effect of the replacement runs in the commit that ended the previous owner, before any
// passive effect cleanup: the owner is already over, whatever the generation bookkeeping still says.
describe('ownership ends at the replacement commit', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('starts no catch-up from a retained refresh, notification or reconnect of the ended run, and releases the waiter with false', async () => {
    const getRunCockpit = vi.fn().mockImplementation((id: string) => Promise.resolve(cockpit(id)))
    const hubHandlers: Record<string, (...args: unknown[]) => void>[] = []
    vi.mocked(runCockpitClient).mockReturnValue({ getRunCockpit } as unknown as ReturnType<typeof runCockpitClient>)
    vi.mocked(runEventsClient).mockReturnValue({ getRunEvents: vi.fn().mockResolvedValue([]) } as unknown as ReturnType<
      typeof runEventsClient
    >)
    vi.mocked(createRunNotificationConnection).mockImplementation(() => {
      const handlers: Record<string, (...args: unknown[]) => void> = {}
      hubHandlers.push(handlers)
      return {
        on: (name: string, handler: (...args: unknown[]) => void) => (handlers[name] = handler),
        onreconnecting: vi.fn(),
        onreconnected: (handler: () => void) => (handlers.reconnected = handler),
        onclose: vi.fn(),
        start: vi.fn(() => new Promise(() => {})),
        stop: vi.fn(() => Promise.resolve()),
      } as unknown as ReturnType<typeof createRunNotificationConnection>
    })

    const held: { refresh: (() => Promise<boolean>) | null; discarded?: Promise<boolean> } = { refresh: null }
    function Consumer({ runId }: { runId: string }) {
      const result = useRunCockpit(runId)
      const { refresh } = result
      useLayoutEffect(() => {
        if (runId === 'run-a') {
          held.refresh = refresh
        }
        if (runId === 'run-b') {
          held.discarded = held.refresh!()
          hubHandlers[0].runAdvanced?.({ runId: 'run-a', latestSequence: 99 })
          hubHandlers[0].reconnected?.()
        }
      }, [runId, refresh])
      return null
    }

    const view = render(<Consumer runId="run-a" />)
    await act(async () => {})
    const calls = () => getRunCockpit.mock.calls.map((call) => call[0])
    expect(calls()).toEqual(['run-a'])

    view.rerender(<Consumer runId="run-b" />)
    expect(await held.discarded).toBe(false)
    await act(async () => {})
    expect(calls()).toEqual(['run-a', 'run-b'])
  })
})

describe('scoped output polling at the replacement commit', () => {
  it('requests nothing for the ended execution from a timer that fires before passive cleanup', async () => {
    vi.useFakeTimers()
    const getVerificationExecutionOutput = vi
      .fn()
      .mockResolvedValue(new VerificationExecutionOutputQueryResult({ text: 'more', isFinal: false, nextOffset: 4 }))
    vi.mocked(verificationExecutionOutputClient).mockReturnValue({ getVerificationExecutionOutput } as unknown as ReturnType<
      typeof verificationExecutionOutputClient
    >)

    function Consumer({ executionId }: { executionId: string }) {
      useVerificationExecutionOutput('project-a', executionId, 'stdout', true)
      useLayoutEffect(() => {
        if (executionId === 'execution-b') {
          vi.advanceTimersByTime(300)
        }
      }, [executionId])
      return null
    }
    const view = render(<Consumer executionId="execution-a" />)
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(getVerificationExecutionOutput).toHaveBeenCalledTimes(1)

    view.rerender(<Consumer executionId="execution-b" />)
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(getVerificationExecutionOutput.mock.calls.map((call) => call[1])).toEqual(['execution-a', 'execution-b'])
  })
})
