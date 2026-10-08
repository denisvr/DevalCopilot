import { useCallback, useEffect, useLayoutEffect, useRef } from 'react'
import { checkpointApprovalEvidenceClient, recordCheckpointReviewClient, RecordCheckpointReviewRequest } from '../../../api/clients'
import {
  approvalSourceKey,
  classifyBundleRefusal,
  describeApprovalFailure,
  toApprovalBundle,
} from './checkpointApprovalBundle'
import type { ApprovalBundle, ApprovalBundleRefusal, ApprovalBundleSource } from './checkpointApprovalBundle'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'
import type { OwnedLifetime } from './useOwnedLifetime'

// What the source's lifetime owns: the reads. `bundleKey` is the key of the newest bundle committed in this lifetime and `epoch`
// counts its replacements, so each committed bundle (including a return to an earlier one) is a distinct lifetime of its own.
interface SourceFrame {
  bundle: ApprovalBundle | null
  // The refresh generation the newest accepted successful read belongs to, whether the newest accepted read failed (and why), and
  // whether a read is in flight.
  readGeneration: number | null
  readFailed: boolean
  refusal: ApprovalBundleRefusal | null
  loading: boolean
  bundleKey: string | null
  epoch: number
}

// What one committed bundle's lifetime owns: the approval of exactly those members.
interface BundleFrame {
  approving: boolean
  // Whether the server accepted this bundle's approval; one committed bundle is approved once from here.
  accepted: boolean
  error: string | null
}

function createSourceFrame(): SourceFrame {
  return { bundle: null, readGeneration: null, readFailed: false, refusal: null, loading: false, bundleKey: null, epoch: 0 }
}

function createBundleFrame(): BundleFrame {
  return { approving: false, accepted: false, error: null }
}

/** The complete verification set a human may approve for one exact source (project, workspace, checkpoint, checkpoint number and
 * fingerprint), and the one action that approves exactly that set.
 *
 * Reads belong to the committed source's lifetime: a different source, a return to an earlier one (A to B to A), or unmounting ends
 * it, and an older overlapping read or a continuation of an ended lifetime changes nothing. The approval action belongs to a
 * narrower lifetime, the committed bundle's: replacing the members ends it, and returning to an earlier member set begins a new
 * one, so equal keys never revive an obsolete callback. A pending approval of a replaced bundle reports nothing, clears no newer
 * guard and refreshes nothing, yet the server's acceptance stays real. The bundle is `current` only when the newest accepted read
 * of the displayed generation succeeded, was consistent with the source and no read is in flight; a cached bundle stays visible but
 * is never offered while it is pending or its refresh failed. `approve` submits exactly the bundle of the render that committed
 * it, once: a handler retained from an earlier or an already accepted bundle starts no request. A decision the server accepted is
 * never reported as failed because a later refresh failed, and nothing is retried automatically. */
export function useCheckpointApprovalEvidence(
  source: ApprovalBundleSource | null,
  refreshGeneration = 0,
  onApproved?: () => unknown,
) {
  const key = source ? approvalSourceKey(source) : null
  const owner = useOwnedLifetime(key)
  const [frame, commit] = useOwnedState(owner, createSourceFrame)
  const bundleOwner = useOwnedLifetime(key === null ? null : JSON.stringify([key, frame.epoch]))
  const [approval, commitApproval] = useOwnedState(bundleOwner, createBundleFrame)
  const projectId = source?.projectId
  const workspaceId = source?.workspaceId
  const checkpointId = source?.checkpointId
  const checkpointNumber = source?.checkpointNumber
  const fingerprintSha256 = source?.fingerprintSha256

  const generation = useRef(refreshGeneration)
  const onApprovedRef = useRef(onApproved)
  // The bundle lifetime that has a request in flight, and the ones whose approval the server accepted (a retained handler of
  // either starts nothing). Both are keyed by lifetime object, so a replaced bundle's completion can never touch a newer one's.
  const approvingLifetime = useRef<OwnedLifetime | null>(null)
  const acceptedLifetimes = useRef(new WeakSet<OwnedLifetime>())
  const committed = useRef({ current: false })
  const bundle = frame.bundle
  const current = frame.readGeneration === refreshGeneration && !frame.loading && !frame.readFailed && bundle !== null

  useLayoutEffect(() => {
    generation.current = refreshGeneration
    onApprovedRef.current = onApproved
    committed.current = { current }
  })

  const refresh = useCallback(async () => {
    if (!projectId || !workspaceId || !checkpointId || checkpointNumber === undefined || !fingerprintSha256 || !owner.isActive()) {
      return
    }

    const isCurrent = owner.begin('read')
    const readGeneration = generation.current
    commit((previous) => ({ ...previous, loading: true }))
    try {
      const response = await checkpointApprovalEvidenceClient().getCheckpointApprovalEvidence(projectId, checkpointId)
      if (isCurrent()) {
        const next = toApprovalBundle(response, { projectId, workspaceId, checkpointId, checkpointNumber, fingerprintSha256 })
        commit((previous) => next
          ? {
            ...previous,
            bundle: next,
            readGeneration,
            readFailed: false,
            refusal: null,
            // A different member set is a replacement, even when it is one the lifetime had before; an equal one is the same bundle.
            bundleKey: next.key,
            epoch: previous.bundleKey !== null && previous.bundleKey !== next.key ? previous.epoch + 1 : previous.epoch,
          }
          : { ...previous, bundle: null, readGeneration, readFailed: true, refusal: 'unavailable' })
      }
    } catch (caught: unknown) {
      if (isCurrent()) {
        commit((previous) => ({ ...previous, bundle: null, readGeneration, readFailed: true, refusal: classifyBundleRefusal(caught) }))
      }
    } finally {
      if (isCurrent()) {
        commit((previous) => ({ ...previous, loading: false }))
      }
    }
  }, [owner, projectId, workspaceId, checkpointId, checkpointNumber, fingerprintSha256, commit])

  useEffect(() => {
    queueMicrotask(() => void refresh())
  }, [refresh, refreshGeneration])

  const approve = useCallback(async () => {
    if (
      !bundle
      || !owner.isActive()
      || !bundleOwner.isActive()
      || !committed.current.current
      || approvingLifetime.current === bundleOwner
      || acceptedLifetimes.current.has(bundleOwner)
    ) {
      return false
    }

    approvingLifetime.current = bundleOwner
    const isCurrent = bundleOwner.begin('approve')
    commitApproval((previous) => ({ ...previous, approving: true, error: null }))
    try {
      await recordCheckpointReviewClient().recordCheckpointReview(
        bundle.projectId,
        new RecordCheckpointReviewRequest({
          gitCheckpointId: bundle.checkpointId,
          actorKind: 'Human',
          decision: 'Approved',
          verificationExecutionIds: bundle.members.map(member => member.executionId),
        }),
      )
    } catch (caught: unknown) {
      if (isCurrent()) {
        commitApproval((previous) => ({ ...previous, error: describeApprovalFailure(caught) }))
      }
      return false
    } finally {
      if (approvingLifetime.current === bundleOwner) {
        approvingLifetime.current = null
      }

      if (isCurrent()) {
        commitApproval((previous) => ({ ...previous, approving: false }))
      }
    }

    // The server accepted the decision. It stays real whatever follows, and this bundle is never submitted again from any handler.
    // A replaced bundle or source neither reports nor refreshes, and a failure of the follow-up read is the review list's own
    // concern: it never relabels this acceptance or retries it.
    acceptedLifetimes.current.add(bundleOwner)
    if (!isCurrent()) {
      return false
    }

    commitApproval((previous) => ({ ...previous, accepted: true }))
    void Promise.resolve()
      .then(() => (isCurrent() ? onApprovedRef.current?.() : undefined))
      .catch(() => undefined)
    return true
  }, [owner, bundleOwner, commitApproval, bundle])

  return {
    bundle,
    current,
    loading: frame.loading,
    readFailed: frame.readFailed,
    refusal: frame.refusal,
    approving: approval.approving,
    accepted: bundle !== null && approval.accepted,
    error: approval.error,
    refresh,
    approve,
  }
}
