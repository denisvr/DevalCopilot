// @vitest-environment jsdom
import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { CollaborationMessageTimelineResponse, ParticipantIdentityResponse } from '../../../api/generated/api-client'
import { collaborationTimelineClient } from '../../../api/clients'
import { useCollaborationTimeline } from './useCollaborationTimeline'

vi.mock('../../../api/clients', () => ({
  collaborationTimelineClient: vi.fn(),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

function timelineMessage(summary = 'A proposal'): CollaborationMessageTimelineResponse {
  return new CollaborationMessageTimelineResponse({
    sequence: 1,
    id: 'message-1',
    actor: new ParticipantIdentityResponse({ kind: 'Agent', role: 'Planner', provider: 'Codex' }),
    recipient: new ParticipantIdentityResponse({ kind: 'Agent', provider: 'ClaudeCode' }),
    type: 'Proposal',
    summary,
    structuredContentJson: '{"scope":"Ledger"}',
    provenance: 'Simulated',
    occurredAtUtc: new Date('2026-09-16T12:00:00Z'),
  })
}

describe('useCollaborationTimeline', () => {
  it('reports loading before the initial request succeeds', () => {
    const pending = deferred<CollaborationMessageTimelineResponse[]>()
    vi.mocked(collaborationTimelineClient).mockReturnValue({
      getCollaborationTimeline: vi.fn().mockReturnValue(pending.promise),
    } as unknown as ReturnType<typeof collaborationTimelineClient>)

    const { result } = renderHook(() => useCollaborationTimeline('run-1', 1))

    expect(result.current.loading).toBe(true)
    expect(result.current.hasSuccessfulResponse).toBe(false)
    expect(result.current.cards).toEqual([])
  })

  it('distinguishes a successful empty response from a failed request', async () => {
    vi.mocked(collaborationTimelineClient).mockReturnValue({
      getCollaborationTimeline: vi.fn().mockResolvedValue([]),
    } as unknown as ReturnType<typeof collaborationTimelineClient>)

    const { result } = renderHook(() => useCollaborationTimeline('run-1', 1))

    await waitFor(() => expect(result.current.hasSuccessfulResponse).toBe(true))
    expect(result.current.loading).toBe(false)
    expect(result.current.error).toBeNull()
    expect(result.current.cards).toEqual([])
  })

  it('reports an initial failure without presenting it as a successful empty timeline', async () => {
    vi.mocked(collaborationTimelineClient).mockReturnValue({
      getCollaborationTimeline: vi.fn().mockRejectedValue(new Error('network unavailable')),
    } as unknown as ReturnType<typeof collaborationTimelineClient>)

    const { result } = renderHook(() => useCollaborationTimeline('run-1', 1))

    await waitFor(() => expect(result.current.error).toBe('Collaboration timeline is unavailable.'))
    expect(result.current.hasSuccessfulResponse).toBe(false)
    expect(result.current.cards).toEqual([])
  })

  it('preserves loaded cards after a failed refresh', async () => {
    const getCollaborationTimeline = vi
      .fn()
      .mockResolvedValueOnce([timelineMessage()])
      .mockRejectedValueOnce(new Error('refresh unavailable'))
    vi.mocked(collaborationTimelineClient).mockReturnValue({
      getCollaborationTimeline,
    } as unknown as ReturnType<typeof collaborationTimelineClient>)

    const { result, rerender } = renderHook(({ sequence }) => useCollaborationTimeline('run-1', sequence), {
      initialProps: { sequence: 1 },
    })

    await waitFor(() => expect(result.current.cards[0]?.summary).toBe('A proposal'))
    rerender({ sequence: 2 })
    await waitFor(() => expect(result.current.error).toBe('Collaboration timeline is unavailable.'))
    expect(result.current.cards[0]?.summary).toBe('A proposal')
    expect(result.current.hasSuccessfulResponse).toBe(false)
  })

  it('isolates state across a run switch and ignores the previous run response', async () => {
    const runOne = deferred<CollaborationMessageTimelineResponse[]>()
    const runTwo = deferred<CollaborationMessageTimelineResponse[]>()
    const getCollaborationTimeline = vi.fn().mockReturnValueOnce(runOne.promise).mockReturnValueOnce(runTwo.promise)
    vi.mocked(collaborationTimelineClient).mockReturnValue({
      getCollaborationTimeline,
    } as unknown as ReturnType<typeof collaborationTimelineClient>)

    const { result, rerender } = renderHook(({ runId }) => useCollaborationTimeline(runId, 1), {
      initialProps: { runId: 'run-1' },
    })
    rerender({ runId: 'run-2' })

    await act(async () => {
      runOne.resolve([timelineMessage('Run one message')])
    })
    expect(result.current.cards).toEqual([])
    expect(result.current.loading).toBe(true)

    await act(async () => {
      runTwo.resolve([timelineMessage('Run two message')])
    })
    await waitFor(() => expect(result.current.cards[0]?.summary).toBe('Run two message'))
  })

  it('never copies bounded timeline content into browser-persisted state or the URL', async () => {
    localStorage.clear()
    sessionStorage.clear()
    document.cookie = ''
    const sentinel = 'SENTINEL-collaboration-timeline-do-not-persist-me'
    vi.mocked(collaborationTimelineClient).mockReturnValue({
      getCollaborationTimeline: vi.fn().mockResolvedValue([timelineMessage(sentinel)]),
    } as unknown as ReturnType<typeof collaborationTimelineClient>)

    const { result } = renderHook(() => useCollaborationTimeline('run-1', 1))

    await waitFor(() => expect(result.current.cards[0]?.summary).toBe(sentinel))
    expect(Object.keys(localStorage)).toHaveLength(0)
    expect(Object.keys(sessionStorage)).toHaveLength(0)
    expect(document.cookie).not.toContain(sentinel)
    expect(window.location.href).not.toContain(sentinel)
  })
})
