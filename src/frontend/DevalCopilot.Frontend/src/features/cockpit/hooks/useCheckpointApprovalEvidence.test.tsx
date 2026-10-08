import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { checkpointApprovalEvidenceClient, recordCheckpointReviewClient } from '../../../api/clients'
import { ApiException, CheckpointApprovalEvidenceMemberResponse, CheckpointApprovalEvidenceResponse } from '../../../api/generated/api-client'
import { useCheckpointApprovalEvidence } from './useCheckpointApprovalEvidence'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  checkpointApprovalEvidenceClient: vi.fn(),
  recordCheckpointReviewClient: vi.fn(),
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

interface Source {
  projectId: string
  workspaceId: string
  checkpointId: string
  checkpointNumber: number
  fingerprintSha256: string
}

const sourceOf = (marker: string): Source => ({
  projectId: `project-${marker}`,
  workspaceId: `workspace-${marker}`,
  checkpointId: `checkpoint-${marker}`,
  checkpointNumber: 4,
  fingerprintSha256: marker.repeat(64).slice(0, 64),
})

function bundleOf(source: Source, members = ['1', '2'], executionSuffix = '') {
  return new CheckpointApprovalEvidenceResponse({
    ...source,
    members: members.map(number => new CheckpointApprovalEvidenceMemberResponse({
      verificationCommandId: `${source.checkpointId}-command-${number}`,
      commandNumber: Number(number),
      recipeLabel: `Recipe ${number}`,
      verificationExecutionId: `${source.checkpointId}-execution-${number}${executionSuffix}`,
      executionNumber: Number(number) * 10,
    })),
  })
}

const problem = (status: number, code?: string) => new ApiException('raw', status, JSON.stringify({ errors: [{ code, detail: 'host text' }] }), {}, null)

function install(read: (projectId: string, checkpointId: string) => Promise<CheckpointApprovalEvidenceResponse>, post: () => Promise<unknown> = () => Promise.resolve({ reviewId: 'review', decision: 'Approved' })) {
  const getCheckpointApprovalEvidence = vi.fn(read)
  const recordCheckpointReview = vi.fn(post)
  vi.mocked(checkpointApprovalEvidenceClient).mockReturnValue({ getCheckpointApprovalEvidence } as unknown as ReturnType<typeof checkpointApprovalEvidenceClient>)
  vi.mocked(recordCheckpointReviewClient).mockReturnValue({ recordCheckpointReview } as unknown as ReturnType<typeof recordCheckpointReviewClient>)
  return { getCheckpointApprovalEvidence, recordCheckpointReview }
}

interface Props {
  source: Source | null
  generation?: number
  onApproved?: () => unknown
}

const mount = (initial: Props) =>
  renderHook((props: Props) => useCheckpointApprovalEvidence(props.source, props.generation ?? 0, props.onApproved), { initialProps: initial })

describe('useCheckpointApprovalEvidence', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('reads the complete bundle once for the committed source and is current only after the read settled', async () => {
    const gate = deferred<CheckpointApprovalEvidenceResponse>()
    const { getCheckpointApprovalEvidence } = install(() => gate.promise)
    const a = sourceOf('a')
    const { result } = mount({ source: a })

    await waitFor(() => expect(getCheckpointApprovalEvidence).toHaveBeenCalledTimes(1))
    expect(getCheckpointApprovalEvidence).toHaveBeenCalledWith('project-a', 'checkpoint-a')
    expect(result.current.current).toBe(false)
    expect(result.current.bundle).toBeNull()

    await act(async () => {
      gate.resolve(bundleOf(a))
    })

    await waitFor(() => expect(result.current.current).toBe(true))
    expect(result.current.bundle?.members.map(member => member.executionId)).toEqual(['checkpoint-a-execution-1', 'checkpoint-a-execution-2'])
  })

  it('reads nothing while there is no source', async () => {
    const { getCheckpointApprovalEvidence } = install(() => Promise.reject(new Error('unexpected')))
    const { result } = mount({ source: null })

    await act(async () => {
      await Promise.resolve()
    })

    expect(getCheckpointApprovalEvidence).not.toHaveBeenCalled()
    expect(result.current.current).toBe(false)
    let started = true
    await act(async () => {
      started = await result.current.approve()
    })
    expect(started).toBe(false)
  })

  it.each([
    ['a refused read', () => Promise.reject(problem(409, 'approval_evidence.verification_incomplete')), 'incomplete'],
    ['too many recipes', () => Promise.reject(problem(409, 'approval_evidence.too_many_recipes')), 'tooMany'],
    ['no recipes', () => Promise.reject(problem(409, 'approval_evidence.no_enabled_recipes')), 'none'],
    ['a transport failure', () => Promise.reject(new Error('network')), 'unavailable'],
    ['a malformed body', () => Promise.resolve({ not: 'a bundle' } as unknown as CheckpointApprovalEvidenceResponse), 'unavailable'],
  ])('withholds the action after %s and keeps no bundle', async (_name, read, refusal) => {
    install(read)
    const { result } = mount({ source: sourceOf('a') })

    await waitFor(() => expect(result.current.readFailed).toBe(true))

    expect(result.current.current).toBe(false)
    expect(result.current.bundle).toBeNull()
    expect(result.current.refusal).toBe(refusal)
    let started = true
    await act(async () => {
      started = await result.current.approve()
    })
    expect(started).toBe(false)
    expect(vi.mocked(recordCheckpointReviewClient)).not.toHaveBeenCalled()
  })

  it('refuses a body that names another project, checkpoint or fingerprint as an inconsistent read', async () => {
    const a = sourceOf('a')
    install(() => Promise.resolve(bundleOf(sourceOf('b'))))
    const { result } = mount({ source: a })

    await waitFor(() => expect(result.current.readFailed).toBe(true))

    expect(result.current.current).toBe(false)
    expect(result.current.bundle).toBeNull()
  })

  it('submits exactly the displayed bundle once as a Human approval and refreshes follow-ups through the owner callback', async () => {
    const a = sourceOf('a')
    const { recordCheckpointReview } = install(() => Promise.resolve(bundleOf(a)))
    const onApproved = vi.fn()
    const { result } = mount({ source: a, onApproved })
    await waitFor(() => expect(result.current.current).toBe(true))

    let accepted = false
    await act(async () => {
      accepted = await result.current.approve()
    })

    expect(accepted).toBe(true)
    expect(recordCheckpointReview).toHaveBeenCalledTimes(1)
    const [projectId, request] = recordCheckpointReview.mock.calls[0] as unknown as [string, Record<string, unknown>]
    expect(projectId).toBe('project-a')
    expect(request).toMatchObject({
      gitCheckpointId: 'checkpoint-a',
      actorKind: 'Human',
      decision: 'Approved',
      verificationExecutionIds: ['checkpoint-a-execution-1', 'checkpoint-a-execution-2'],
    })
    expect(request.verificationExecutionId).toBeUndefined()
    await waitFor(() => expect(onApproved).toHaveBeenCalledTimes(1))
    expect(result.current.accepted).toBe(true)
    expect(result.current.approving).toBe(false)
    expect(result.current.error).toBeNull()
  })

  it('starts no second request while one is pending and never retries a failure automatically', async () => {
    const a = sourceOf('a')
    const gate = deferred<unknown>()
    const { recordCheckpointReview } = install(() => Promise.resolve(bundleOf(a)), () => gate.promise)
    const { result } = mount({ source: a })
    await waitFor(() => expect(result.current.current).toBe(true))

    let first!: Promise<boolean>
    let second = true
    await act(async () => {
      first = result.current.approve()
      second = await result.current.approve()
    })
    expect(second).toBe(false)
    expect(recordCheckpointReview).toHaveBeenCalledTimes(1)
    expect(result.current.approving).toBe(true)

    await act(async () => {
      gate.reject(problem(409, 'reviews.approval_requires_complete_verification_set'))
      await first
    })

    expect(await first).toBe(false)
    expect(result.current.approving).toBe(false)
    expect(result.current.accepted).toBe(false)
    expect(result.current.error).toContain('Refresh evidence')
    expect(result.current.error).not.toContain('host text')
    await act(async () => {
      await new Promise(resolve => setTimeout(resolve, 30))
    })
    expect(recordCheckpointReview).toHaveBeenCalledTimes(1)
  })

  it('keeps an accepted approval accepted when the owner follow-up throws, rejects or never resolves, and never retries', async () => {
    const a = sourceOf('a')
    for (const onApproved of [() => { throw new Error('boom') }, () => Promise.reject(new Error('refresh failed')), () => new Promise<void>(() => undefined)]) {
      const { recordCheckpointReview } = install(() => Promise.resolve(bundleOf(a)))
      const { result, unmount } = mount({ source: a, onApproved })
      await waitFor(() => expect(result.current.current).toBe(true))

      let accepted = false
      await act(async () => {
        accepted = await result.current.approve()
      })

      expect(accepted).toBe(true)
      expect(result.current.accepted).toBe(true)
      expect(result.current.error).toBeNull()
      expect(recordCheckpointReview).toHaveBeenCalledTimes(1)
      unmount()
    }
  })

  it('does not offer a second approval of the same bundle after acceptance, and a changed bundle is a new decision', async () => {
    const a = sourceOf('a')
    let suffix = ''
    const { recordCheckpointReview } = install(() => Promise.resolve(bundleOf(a, ['1', '2'], suffix)))
    const { result, rerender } = mount({ source: a })
    await waitFor(() => expect(result.current.current).toBe(true))
    await act(async () => {
      await result.current.approve()
    })
    expect(result.current.accepted).toBe(true)

    suffix = '-rerun'
    rerender({ source: a, generation: 1 })
    await waitFor(() => expect(result.current.bundle?.members[0].executionId).toBe('checkpoint-a-execution-1-rerun'))
    await waitFor(() => expect(result.current.current).toBe(true))

    expect(result.current.accepted).toBe(false)
    await act(async () => {
      await result.current.approve()
    })
    expect(recordCheckpointReview).toHaveBeenCalledTimes(2)
  })

  it('ignores an older overlapping read that settles after a newer one', async () => {
    const a = sourceOf('a')
    const first = deferred<CheckpointApprovalEvidenceResponse>()
    const second = deferred<CheckpointApprovalEvidenceResponse>()
    const reads = [first, second]
    install(() => reads.shift()!.promise)
    const { result, rerender } = mount({ source: a, generation: 0 })
    await waitFor(() => expect(vi.mocked(checkpointApprovalEvidenceClient)).toHaveBeenCalledTimes(1))
    rerender({ source: a, generation: 1 })
    await waitFor(() => expect(vi.mocked(checkpointApprovalEvidenceClient)).toHaveBeenCalledTimes(2))

    await act(async () => {
      second.resolve(bundleOf(a, ['1', '2'], '-newer'))
    })
    await waitFor(() => expect(result.current.current).toBe(true))
    await act(async () => {
      first.resolve(bundleOf(a, ['1', '2'], '-older'))
    })

    expect(result.current.bundle?.members[0].executionId).toBe('checkpoint-a-execution-1-newer')
    expect(result.current.current).toBe(true)
  })

  it('does not call a bundle current while a newer generation is being read, and starts no request from it', async () => {
    const a = sourceOf('a')
    const second = deferred<CheckpointApprovalEvidenceResponse>()
    const reads: (() => Promise<CheckpointApprovalEvidenceResponse>)[] = [() => Promise.resolve(bundleOf(a)), () => second.promise]
    const { recordCheckpointReview } = install(() => reads.shift()!())
    const { result, rerender } = mount({ source: a, generation: 0 })
    await waitFor(() => expect(result.current.current).toBe(true))
    const retained = result.current.approve

    rerender({ source: a, generation: 1 })
    await waitFor(() => expect(result.current.loading).toBe(true))

    expect(result.current.current).toBe(false)
    let started = true
    await act(async () => {
      started = await retained()
    })
    expect(started).toBe(false)
    expect(recordCheckpointReview).not.toHaveBeenCalled()
  })

  it('does not call a bundle current after its refresh failed and withholds the action', async () => {
    const a = sourceOf('a')
    const reads: (() => Promise<CheckpointApprovalEvidenceResponse>)[] = [() => Promise.resolve(bundleOf(a)), () => Promise.reject(new Error('down'))]
    install(() => reads.shift()!())
    const { result, rerender } = mount({ source: a, generation: 0 })
    await waitFor(() => expect(result.current.current).toBe(true))

    rerender({ source: a, generation: 1 })

    await waitFor(() => expect(result.current.readFailed).toBe(true))
    expect(result.current.current).toBe(false)
    expect(result.current.bundle).toBeNull()
  })

  it('gives A to B to A three lifetimes: an older source read never lands in a later visit and a retained handler starts nothing', async () => {
    const a = sourceOf('a')
    const b = sourceOf('b')
    const firstA = deferred<CheckpointApprovalEvidenceResponse>()
    const reads = new Map<string, ReturnType<typeof deferred<CheckpointApprovalEvidenceResponse>>[]>()
    reads.set('checkpoint-a', [firstA, deferred<CheckpointApprovalEvidenceResponse>()])
    reads.set('checkpoint-b', [deferred<CheckpointApprovalEvidenceResponse>()])
    const { recordCheckpointReview } = install((_project, checkpoint) => reads.get(checkpoint)!.shift()!.promise)
    const secondA = reads.get('checkpoint-a')![1]
    const { result, rerender } = mount({ source: a })
    await waitFor(() => expect(vi.mocked(checkpointApprovalEvidenceClient)).toHaveBeenCalledTimes(1))
    const retainedFromFirstA = result.current.approve

    rerender({ source: b })
    await waitFor(() => expect(vi.mocked(checkpointApprovalEvidenceClient)).toHaveBeenCalledTimes(2))
    rerender({ source: a })
    await waitFor(() => expect(vi.mocked(checkpointApprovalEvidenceClient)).toHaveBeenCalledTimes(3))

    // The first visit to A answers late with a bundle that differs from what the second visit will read.
    await act(async () => {
      firstA.resolve(bundleOf(a, ['1', '2'], '-stale'))
    })
    expect(result.current.bundle).toBeNull()
    expect(result.current.current).toBe(false)

    await act(async () => {
      secondA.resolve(bundleOf(a, ['1', '2'], '-fresh'))
    })
    await waitFor(() => expect(result.current.current).toBe(true))
    expect(result.current.bundle?.members[0].executionId).toBe('checkpoint-a-execution-1-fresh')

    let started = true
    await act(async () => {
      started = await retainedFromFirstA()
    })
    expect(started).toBe(false)
    expect(recordCheckpointReview).not.toHaveBeenCalled()
  })

  it.each([
    ['a new checkpoint', (source: Source): Source => ({ ...source, checkpointId: 'checkpoint-a2' })],
    ['a changed fingerprint of the same checkpoint', (source: Source): Source => ({ ...source, fingerprintSha256: 'e'.repeat(64) })],
  ])('treats %s of the same project as a new lifetime: the earlier read never lands in it', async (_name, change) => {
    const a = sourceOf('a')
    const changed = change(a)
    const second = deferred<CheckpointApprovalEvidenceResponse>()
    const reads = [() => Promise.resolve(bundleOf(a)), () => second.promise]
    install(() => reads.shift()!())
    const { result, rerender } = mount({ source: a })
    await waitFor(() => expect(result.current.current).toBe(true))
    expect(result.current.bundle?.checkpointId).toBe(a.checkpointId)

    rerender({ source: changed })

    // The new lifetime starts empty: the earlier source's bundle is never carried over, not even as cached history, and the first
    // render of the new source cannot approve anything.
    expect(result.current.bundle).toBeNull()
    expect(result.current.current).toBe(false)
    expect(result.current.accepted).toBe(false)
    await waitFor(() => expect(vi.mocked(checkpointApprovalEvidenceClient)).toHaveBeenCalledTimes(2))
    expect(result.current.bundle).toBeNull()
    expect(result.current.readFailed).toBe(false)
  })

  it('ends an accepted approval quietly when the source is replaced mid-request: no report, no follow-up, and the next source is not blocked', async () => {
    const a = sourceOf('a')
    const b = sourceOf('b')
    const post = deferred<unknown>()
    const { recordCheckpointReview } = install(
      (_project, checkpoint) => Promise.resolve(bundleOf(checkpoint === 'checkpoint-a' ? a : b)),
      () => post.promise,
    )
    const onApproved = vi.fn()
    const { result, rerender } = mount({ source: a, onApproved })
    await waitFor(() => expect(result.current.current).toBe(true))

    let pending!: Promise<boolean>
    await act(async () => {
      pending = result.current.approve()
    })
    expect(recordCheckpointReview).toHaveBeenCalledTimes(1)
    rerender({ source: b, onApproved })
    await waitFor(() => expect(result.current.bundle?.checkpointId).toBe('checkpoint-b'))
    await waitFor(() => expect(result.current.current).toBe(true))
    expect(result.current.approving).toBe(false)

    await act(async () => {
      post.resolve({ reviewId: 'accepted-for-a', decision: 'Approved' })
      expect(await pending).toBe(false)
    })

    expect(onApproved).not.toHaveBeenCalled()
    expect(result.current.accepted).toBe(false)
    expect(result.current.error).toBeNull()
    expect(result.current.approving).toBe(false)
  })

  it('ignores every continuation after unmount', async () => {
    const a = sourceOf('a')
    const read = deferred<CheckpointApprovalEvidenceResponse>()
    install(() => read.promise)
    const { result, unmount } = mount({ source: a })
    await waitFor(() => expect(vi.mocked(checkpointApprovalEvidenceClient)).toHaveBeenCalledTimes(1))
    const retained = result.current.approve

    unmount()
    await act(async () => {
      read.resolve(bundleOf(a))
      await Promise.resolve()
    })

    expect(await retained()).toBe(false)
    expect(vi.mocked(recordCheckpointReviewClient)).not.toHaveBeenCalled()
  })

  it('does not report or follow up an acceptance that settles after unmount', async () => {
    const a = sourceOf('a')
    const post = deferred<unknown>()
    install(() => Promise.resolve(bundleOf(a)), () => post.promise)
    const onApproved = vi.fn()
    const { result, unmount } = mount({ source: a, onApproved })
    await waitFor(() => expect(result.current.current).toBe(true))
    let pending!: Promise<boolean>
    await act(async () => {
      pending = result.current.approve()
    })

    unmount()
    post.resolve({ reviewId: 'accepted', decision: 'Approved' })

    expect(await pending).toBe(false)
    expect(onApproved).not.toHaveBeenCalled()
  })

  it('refuses a retained handler of an earlier bundle after the same source reads a different member set', async () => {
    const a = sourceOf('a')
    const suffixes = ['', '-rerun']
    const { recordCheckpointReview } = install(() => Promise.resolve(bundleOf(a, ['1', '2'], suffixes.shift())))
    const { result, rerender } = mount({ source: a, generation: 0 })
    await waitFor(() => expect(result.current.current).toBe(true))
    const retained = result.current.approve

    rerender({ source: a, generation: 1 })
    await waitFor(() => expect(result.current.bundle?.members[0].executionId).toBe('checkpoint-a-execution-1-rerun'))
    await waitFor(() => expect(result.current.current).toBe(true))

    let started = true
    await act(async () => {
      started = await retained()
    })
    expect(started).toBe(false)
    expect(recordCheckpointReview).not.toHaveBeenCalled()
  })
})

// The approval action belongs to the lifetime of the committed bundle: replacing the members ends it (a return to an earlier member
// set is a new lifetime, never a revival), and an accepted server operation stays real whatever happens to the lifetime.
describe('useCheckpointApprovalEvidence bundle lifetime', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  // Mounts a hook whose read returns the member set named by `memberSet.suffix`, with the first approval left pending.
  async function mountWithPendingApproval(post: ReturnType<typeof deferred<unknown>>, onApproved = vi.fn()) {
    const a = sourceOf('a')
    const memberSet = { suffix: '' }
    const installed = install(() => Promise.resolve(bundleOf(a, ['1', '2'], memberSet.suffix)), () => post.promise)
    const mounted = mount({ source: a, generation: 0, onApproved })
    await waitFor(() => expect(mounted.result.current.current).toBe(true))
    let pending!: Promise<boolean>
    await act(async () => {
      pending = mounted.result.current.approve()
    })
    expect(installed.recordCheckpointReview).toHaveBeenCalledTimes(1)
    const replaceMembers = async (suffix: string, generation: number) => {
      memberSet.suffix = suffix
      mounted.rerender({ source: a, generation, onApproved })
      await waitFor(() => expect(mounted.result.current.bundle?.members[0].executionId).toBe(`checkpoint-a-execution-1${suffix}`))
      await waitFor(() => expect(mounted.result.current.current).toBe(true))
    }
    return { ...mounted, ...installed, a, pending: () => pending, replaceMembers, onApproved }
  }

  it('does not write a failure of an obsolete pending approval onto the replacement members', async () => {
    const post = deferred<unknown>()
    const { result, replaceMembers, pending, onApproved } = await mountWithPendingApproval(post)

    await replaceMembers('-new-members', 1)
    expect(result.current.approving).toBe(false)
    await act(async () => {
      post.reject(problem(422, 'reviews.approval_requires_complete_verification_set'))
      expect(await pending()).toBe(false)
    })

    expect(result.current.error).toBeNull()
    expect(result.current.accepted).toBe(false)
    expect(result.current.approving).toBe(false)
    expect(onApproved).not.toHaveBeenCalled()
  })

  it('neither marks the replacement accepted nor refreshes it when an obsolete pending approval is accepted', async () => {
    const post = deferred<unknown>()
    const { result, replaceMembers, pending, onApproved, recordCheckpointReview } = await mountWithPendingApproval(post)

    await replaceMembers('-new-members', 1)
    await act(async () => {
      post.resolve({ reviewId: 'accepted-for-the-old-members', decision: 'Approved' })
      expect(await pending()).toBe(false)
    })

    expect(result.current.accepted).toBe(false)
    expect(result.current.error).toBeNull()
    expect(onApproved).not.toHaveBeenCalled()
    // The old decision is real on the server, and the replacement is a separate decision that can still be made.
    expect(recordCheckpointReview).toHaveBeenCalledTimes(1)
    await act(async () => {
      expect(await result.current.approve()).toBe(true)
    })
    expect(recordCheckpointReview).toHaveBeenCalledTimes(2)
    const [, request] = recordCheckpointReview.mock.calls[1] as unknown as [string, { verificationExecutionIds: string[] }]
    expect(request.verificationExecutionIds).toEqual([
      'checkpoint-a-execution-1-new-members',
      'checkpoint-a-execution-2-new-members',
    ])
  })

  it('does not revive an earlier callback when the same source goes bundle A to B to A', async () => {
    const a = sourceOf('a')
    const memberSet = { suffix: '' }
    const { recordCheckpointReview } = install(() => Promise.resolve(bundleOf(a, ['1', '2'], memberSet.suffix)))
    const { result, rerender } = mount({ source: a, generation: 0 })
    await waitFor(() => expect(result.current.current).toBe(true))
    const retainedFromFirstA = result.current.approve

    memberSet.suffix = '-B'
    rerender({ source: a, generation: 1 })
    await waitFor(() => expect(result.current.bundle?.members[0].executionId).toBe('checkpoint-a-execution-1-B'))
    memberSet.suffix = ''
    rerender({ source: a, generation: 2 })
    await waitFor(() => expect(result.current.bundle?.members[0].executionId).toBe('checkpoint-a-execution-1'))
    await waitFor(() => expect(result.current.current).toBe(true))

    // The returned members equal the first ones, yet this is a later lifetime.
    let started = true
    await act(async () => {
      started = await retainedFromFirstA()
    })
    expect(started).toBe(false)
    expect(recordCheckpointReview).not.toHaveBeenCalled()
    await act(async () => {
      expect(await result.current.approve()).toBe(true)
    })
    expect(recordCheckpointReview).toHaveBeenCalledTimes(1)
  })

  it('does not let an obsolete A completion clear the pending guard of the newer A after A to B to A', async () => {
    const a = sourceOf('a')
    const memberSet = { suffix: '' }
    const first = deferred<unknown>()
    const second = deferred<unknown>()
    const posts = [first, second]
    const { recordCheckpointReview } = install(() => Promise.resolve(bundleOf(a, ['1', '2'], memberSet.suffix)), () => posts.shift()!.promise)
    const onApproved = vi.fn()
    const { result, rerender } = mount({ source: a, generation: 0, onApproved })
    await waitFor(() => expect(result.current.current).toBe(true))
    let olderPending!: Promise<boolean>
    await act(async () => {
      olderPending = result.current.approve()
    })

    memberSet.suffix = '-B'
    rerender({ source: a, generation: 1, onApproved })
    await waitFor(() => expect(result.current.bundle?.members[0].executionId).toBe('checkpoint-a-execution-1-B'))
    memberSet.suffix = ''
    rerender({ source: a, generation: 2, onApproved })
    await waitFor(() => expect(result.current.bundle?.members[0].executionId).toBe('checkpoint-a-execution-1'))
    await waitFor(() => expect(result.current.current).toBe(true))

    let newerPending!: Promise<boolean>
    await act(async () => {
      newerPending = result.current.approve()
    })
    expect(recordCheckpointReview).toHaveBeenCalledTimes(2)
    expect(result.current.approving).toBe(true)

    // The older request completes while the newer one is still pending: it clears nothing of the newer lifetime.
    await act(async () => {
      first.reject(problem(500))
      expect(await olderPending).toBe(false)
    })
    expect(result.current.approving).toBe(true)
    expect(result.current.error).toBeNull()
    let duplicate = true
    await act(async () => {
      duplicate = await result.current.approve()
    })
    expect(duplicate).toBe(false)
    expect(recordCheckpointReview).toHaveBeenCalledTimes(2)

    await act(async () => {
      second.resolve({ reviewId: 'accepted', decision: 'Approved' })
      expect(await newerPending).toBe(true)
    })
    expect(result.current.accepted).toBe(true)
    await waitFor(() => expect(onApproved).toHaveBeenCalledTimes(1))
  })

  it('keeps a newer submission authoritative while an older approval of other members completes', async () => {
    const post = deferred<unknown>()
    const newerPost = deferred<unknown>()
    const { result, replaceMembers, pending, onApproved, recordCheckpointReview } = await mountWithPendingApproval(post)
    recordCheckpointReview.mockImplementationOnce(() => newerPost.promise)

    await replaceMembers('-new-members', 1)
    let newerPending!: Promise<boolean>
    await act(async () => {
      newerPending = result.current.approve()
    })
    expect(result.current.approving).toBe(true)

    await act(async () => {
      post.resolve({ reviewId: 'old', decision: 'Approved' })
      expect(await pending()).toBe(false)
    })
    expect(result.current.approving).toBe(true)
    expect(result.current.accepted).toBe(false)
    expect(onApproved).not.toHaveBeenCalled()

    await act(async () => {
      newerPost.reject(problem(422, 'reviews.checkpoint_not_current'))
      expect(await newerPending).toBe(false)
    })
    expect(result.current.approving).toBe(false)
    expect(result.current.error).toBe('The source is no longer current or is busy. Use Refresh evidence before approving.')
  })

  it('does not submit the same accepted bundle again through a retained callback', async () => {
    const a = sourceOf('a')
    const onApproved = vi.fn()
    const { recordCheckpointReview } = install(() => Promise.resolve(bundleOf(a)))
    const { result } = mount({ source: a, onApproved })
    await waitFor(() => expect(result.current.current).toBe(true))
    const retained = result.current.approve

    await act(async () => {
      expect(await retained()).toBe(true)
    })
    expect(result.current.accepted).toBe(true)
    let again = true
    await act(async () => {
      again = await retained()
    })
    expect(again).toBe(false)
    await act(async () => {
      expect(await result.current.approve()).toBe(false)
    })
    expect(recordCheckpointReview).toHaveBeenCalledTimes(1)
    await waitFor(() => expect(onApproved).toHaveBeenCalledTimes(1))
  })

  it('does not submit an accepted bundle again through a retained callback even when its refresh read an equal bundle', async () => {
    const a = sourceOf('a')
    const { recordCheckpointReview } = install(() => Promise.resolve(bundleOf(a)))
    const { result, rerender } = mount({ source: a, generation: 0 })
    await waitFor(() => expect(result.current.current).toBe(true))
    const retained = result.current.approve
    await act(async () => {
      expect(await retained()).toBe(true)
    })

    rerender({ source: a, generation: 1 })
    await waitFor(() => expect(vi.mocked(checkpointApprovalEvidenceClient)).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(result.current.current).toBe(true))

    expect(result.current.accepted).toBe(true)
    await act(async () => {
      expect(await retained()).toBe(false)
    })
    expect(recordCheckpointReview).toHaveBeenCalledTimes(1)
  })

  it('checks ownership again before the deferred refresh callback runs', async () => {
    const post = deferred<unknown>()
    const { unmount, pending, onApproved } = await mountWithPendingApproval(post)

    // The acceptance is committed first; the refresh callback is deferred by a microtask, and the lifetime ends before it runs.
    await act(async () => {
      post.resolve({ reviewId: 'accepted', decision: 'Approved' })
      queueMicrotask(unmount)
      await pending()
    })

    expect(onApproved).not.toHaveBeenCalled()
  })

  it('keeps one pending approval with an unchanged bundle when a refresh reads equal members', async () => {
    const post = deferred<unknown>()
    const { result, rerender, a, pending, onApproved } = await mountWithPendingApproval(post)

    rerender({ source: a, generation: 1, onApproved })
    await waitFor(() => expect(vi.mocked(checkpointApprovalEvidenceClient)).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(result.current.current).toBe(true))
    expect(result.current.approving).toBe(true)

    await act(async () => {
      post.resolve({ reviewId: 'accepted', decision: 'Approved' })
      expect(await pending()).toBe(true)
    })

    expect(result.current.approving).toBe(false)
    expect(result.current.accepted).toBe(true)
    await waitFor(() => expect(onApproved).toHaveBeenCalledTimes(1))
  })

  it('keeps a failure of an unchanged bundle visible and lets the human retry it explicitly', async () => {
    const a = sourceOf('a')
    const posts = [() => Promise.reject(problem(422, 'reviews.approval_requires_passed_verification')), () => Promise.resolve({ reviewId: 'ok', decision: 'Approved' })]
    const { recordCheckpointReview } = install(() => Promise.resolve(bundleOf(a)), () => posts.shift()!())
    const { result, rerender } = mount({ source: a, generation: 0 })
    await waitFor(() => expect(result.current.current).toBe(true))

    await act(async () => {
      expect(await result.current.approve()).toBe(false)
    })
    expect(result.current.error).toContain('The enabled checks changed')
    rerender({ source: a, generation: 1 })
    await waitFor(() => expect(vi.mocked(checkpointApprovalEvidenceClient)).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(result.current.current).toBe(true))
    expect(result.current.error).toContain('The enabled checks changed')
    expect(recordCheckpointReview).toHaveBeenCalledTimes(1)

    await act(async () => {
      expect(await result.current.approve()).toBe(true)
    })
    expect(recordCheckpointReview).toHaveBeenCalledTimes(2)
    expect(result.current.error).toBeNull()
  })

  it('ends a pending approval at unmount: no error, no refresh and nothing written by the late failure', async () => {
    const post = deferred<unknown>()
    const { unmount, pending, onApproved } = await mountWithPendingApproval(post)

    unmount()
    post.reject(problem(500))

    expect(await pending()).toBe(false)
    expect(onApproved).not.toHaveBeenCalled()
  })
})

// The bundle is read and owned for the complete observed source: workspace and checkpoint number are part of its identity.
describe('useCheckpointApprovalEvidence full source identity', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it.each([
    ['a foreign workspace', { workspaceId: 'workspace-foreign' }],
    ['an absent workspace', { workspaceId: undefined }],
    ['a different checkpoint number', { checkpointNumber: 999 }],
    ['an absent checkpoint number', { checkpointNumber: undefined }],
  ])('withholds the bundle and starts no approval for %s', async (_name, overrides) => {
    const a = sourceOf('a')
    const { recordCheckpointReview } = install(() => Promise.resolve(new CheckpointApprovalEvidenceResponse({ ...bundleOf(a), ...overrides })))
    const { result } = mount({ source: a })

    await waitFor(() => expect(result.current.readFailed).toBe(true))

    expect(result.current.bundle).toBeNull()
    expect(result.current.current).toBe(false)
    let started = true
    await act(async () => {
      started = await result.current.approve()
    })
    expect(started).toBe(false)
    expect(recordCheckpointReview).not.toHaveBeenCalled()
  })

  it.each([
    ['workspace', (source: Source): Source => ({ ...source, workspaceId: 'workspace-a2' })],
    ['checkpoint number', (source: Source): Source => ({ ...source, checkpointNumber: 5 })],
  ])('treats a replaced %s of the same checkpoint as a new lifetime that never inherits the earlier read or approval', async (_name, change) => {
    const a = sourceOf('a')
    const changed = change(a)
    const post = deferred<unknown>()
    const second = deferred<CheckpointApprovalEvidenceResponse>()
    const reads = [() => Promise.resolve(bundleOf(a)), () => second.promise]
    const { recordCheckpointReview } = install(() => reads.shift()!(), () => post.promise)
    const onApproved = vi.fn()
    const { result, rerender } = mount({ source: a, onApproved })
    await waitFor(() => expect(result.current.current).toBe(true))
    const retained = result.current.approve
    let pending!: Promise<boolean>
    await act(async () => {
      pending = retained()
    })

    rerender({ source: changed, onApproved })

    expect(result.current.bundle).toBeNull()
    expect(result.current.approving).toBe(false)
    await waitFor(() => expect(vi.mocked(checkpointApprovalEvidenceClient)).toHaveBeenCalledTimes(2))
    await act(async () => {
      post.resolve({ reviewId: 'accepted-for-the-earlier-source', decision: 'Approved' })
      expect(await pending).toBe(false)
    })
    expect(onApproved).not.toHaveBeenCalled()
    await act(async () => {
      second.resolve(bundleOf(a))
    })
    // The response still names the earlier identity, so it is not this source's bundle.
    await waitFor(() => expect(result.current.readFailed).toBe(true))
    expect(result.current.bundle).toBeNull()
    expect(await retained()).toBe(false)
    expect(recordCheckpointReview).toHaveBeenCalledTimes(1)
  })

  it('accepts a bundle that repeats every admitted fact of its source', async () => {
    const a = sourceOf('a')
    install(() => Promise.resolve(bundleOf(a)))
    const { result } = mount({ source: a })

    await waitFor(() => expect(result.current.current).toBe(true))

    expect(result.current.bundle).toMatchObject({ projectId: a.projectId, workspaceId: a.workspaceId, checkpointId: a.checkpointId, checkpointNumber: a.checkpointNumber })
  })
})
