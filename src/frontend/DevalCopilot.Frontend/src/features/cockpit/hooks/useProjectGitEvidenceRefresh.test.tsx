import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { captureGitWorkspaceCheckpointClient, projectGitEvidenceClient } from '../../../api/clients'
import { GetProjectGitEvidenceResponse } from '../../../api/generated/api-client'
import { useProjectGitEvidence } from './useProjectGitEvidence'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  projectGitEvidenceClient: vi.fn(),
  captureGitWorkspaceCheckpointClient: vi.fn(),
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

const checkpoint = (number: number) => new GetProjectGitEvidenceResponse({ checkpointId: `checkpoint-${number}`, checkpointNumber: number })

function install(read: () => Promise<GetProjectGitEvidenceResponse>, capture = vi.fn().mockResolvedValue({})) {
  vi.mocked(projectGitEvidenceClient).mockReturnValue({ getProjectGitEvidence: vi.fn(read) } as unknown as ReturnType<typeof projectGitEvidenceClient>)
  vi.mocked(captureGitWorkspaceCheckpointClient).mockReturnValue({ captureGitWorkspaceCheckpoint: capture } as unknown as ReturnType<typeof captureGitWorkspaceCheckpointClient>)
  return capture
}

// Records every committed render so a frame that still calls stale metadata current cannot hide behind a later, correct one.
function recorded(initial: { generation: number; onCaptured?: () => void }) {
  const frames: { generation: number; current: boolean; checkpointId: string | undefined }[] = []
  const rendered = renderHook(
    (props: { generation: number; onCaptured?: () => void }) => {
      const result = useProjectGitEvidence('project-1', true, props.generation, props.onCaptured)
      frames.push({ generation: props.generation, current: result.current, checkpointId: result.evidence?.checkpointId })
      return result
    },
    { initialProps: initial },
  )
  return { ...rendered, frames }
}

describe('useProjectGitEvidence refresh generations', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('is current only after the read of the present generation, and never in a committed frame between the request and its read', async () => {
    let latest = checkpoint(1)
    install(() => Promise.resolve(latest))
    const { result, rerender, frames } = recorded({ generation: 0 })
    expect(result.current.current).toBe(false)
    await waitFor(() => expect(result.current.current).toBe(true))
    const settled = frames.length

    latest = checkpoint(2)
    rerender({ generation: 1 })

    await waitFor(() => expect(result.current.evidence?.checkpointId).toBe('checkpoint-2'))
    await waitFor(() => expect(result.current.current).toBe(true))
    const afterRequest = frames.slice(settled)
    expect(afterRequest[0]).toMatchObject({ generation: 1, current: false, checkpointId: 'checkpoint-1' })
    expect(afterRequest.filter((frame) => frame.generation === 1 && frame.current).every((frame) => frame.checkpointId === 'checkpoint-2')).toBe(true)
  })

  it('stays not current after a failed read, keeps the previous metadata, and recovers with the next successful read', async () => {
    let failing = false
    let latest = checkpoint(1)
    install(() => (failing ? Promise.reject(new Error('unavailable')) : Promise.resolve(latest)))
    const { result, rerender } = recorded({ generation: 0 })
    await waitFor(() => expect(result.current.current).toBe(true))

    failing = true
    rerender({ generation: 1 })
    await waitFor(() => expect(result.current.error).not.toBeNull())
    expect(result.current.current).toBe(false)
    expect(result.current.evidence?.checkpointId).toBe('checkpoint-1')

    failing = false
    latest = checkpoint(2)
    rerender({ generation: 2 })
    await waitFor(() => expect(result.current.current).toBe(true))
    expect(result.current.evidence?.checkpointId).toBe('checkpoint-2')
    expect(result.current.error).toBeNull()
  })

  it('ignores an older overlapping read of an earlier generation', async () => {
    const older = deferred<GetProjectGitEvidenceResponse>()
    const newer = deferred<GetProjectGitEvidenceResponse>()
    const reads = [Promise.resolve(checkpoint(1)), older.promise, newer.promise]
    install(() => reads.shift() ?? Promise.resolve(checkpoint(9)))
    const { result, rerender } = recorded({ generation: 0 })
    await waitFor(() => expect(result.current.current).toBe(true))

    rerender({ generation: 1 })
    await waitFor(() => expect(reads).toHaveLength(1))
    rerender({ generation: 2 })
    await waitFor(() => expect(reads).toHaveLength(0))
    await act(async () => newer.resolve(checkpoint(3)))
    await act(async () => older.resolve(checkpoint(2)))

    expect(result.current.evidence?.checkpointId).toBe('checkpoint-3')
    expect(result.current.current).toBe(true)
  })

  it('reports a successful capture once, before the evidence is read again, and reports no failed capture', async () => {
    const capture = install(() => Promise.resolve(checkpoint(1)))
    const onCaptured = vi.fn()
    const { result } = recorded({ generation: 0, onCaptured })
    await waitFor(() => expect(result.current.current).toBe(true))

    capture.mockRejectedValueOnce(new Error('refused'))
    await act(async () => result.current.capture())
    expect(onCaptured).not.toHaveBeenCalled()

    await act(async () => result.current.capture())
    expect(capture).toHaveBeenCalledTimes(2)
    expect(onCaptured).toHaveBeenCalledTimes(1)
  })
})
