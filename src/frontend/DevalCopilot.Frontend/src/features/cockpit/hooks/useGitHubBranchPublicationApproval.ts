import { useCallback, useRef } from 'react'
import { RecordGitHubBranchPublicationApprovalRequest, gitHubBranchPublicationApprovalClient, recordGitHubBranchPublicationApprovalClient } from '../../../api/clients'
import { ApiException } from '../../../api/generated/api-client'
import { publicationApprovalScopeKey, toPublicationApprovalReading, toRecordedPublicationApproval } from '../gitHubBranchPublicationApproval'
import type { PublicationApprovalReading, PublicationApprovalScope } from '../gitHubBranchPublicationApproval'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'
import type { OwnedLifetime } from './useOwnedLifetime'

export type GitHubPublicationApprovalLoadPhase =
  | { kind: 'idle' }
  | { kind: 'pending' }
  /** `read`: the request failed. `invalid`: the host answered, but not with a coherent answer for exactly this scope. */
  | { kind: 'failed'; reason: 'read' | 'invalid' }
  | { kind: 'result'; reading: PublicationApprovalReading }

export type GitHubPublicationApprovalWritePhase =
  | { kind: 'idle' }
  | { kind: 'pending' }
  /** `refused`: the host answered with a definite refusal. `ambiguous`: the outcome is unknown. Either way an explicit reload must come first. */
  | { kind: 'failed'; reason: 'refused' | 'ambiguous' }

export interface UseGitHubBranchPublicationApprovalResult {
  /** Whether the region is expanded; false (collapsed) for every newly committed scope. */
  open: boolean
  load: GitHubPublicationApprovalLoadPhase
  write: GitHubPublicationApprovalWritePhase
  /** True only when the opening may approve right now: the current settled ApprovalNotRecorded read and nothing in flight. */
  canApprove: boolean
  show: () => void
  /** Closes the opening that created this callback, discarding its answers and anything still in flight. */
  close: () => void
  /** The one explicit read of the recorded approval (load or reload). It disarms approving synchronously. */
  reload: () => void
  /** The one explicit write. A no-op unless `canApprove` for the render that created this callback. */
  approve: () => void
}

interface ScopeFrame {
  open: boolean
  serial: number
}

interface OpeningFrame {
  load: GitHubPublicationApprovalLoadPhase
  write: GitHubPublicationApprovalWritePhase
  /** Advanced by every change of what an approval depends on; mirrored synchronously by a ref so a retained callback can be recognized. */
  epoch: number
}

const IDLE_LOAD: GitHubPublicationApprovalLoadPhase = { kind: 'idle' }
const PENDING_LOAD: GitHubPublicationApprovalLoadPhase = { kind: 'pending' }
const IDLE_WRITE: GitHubPublicationApprovalWritePhase = { kind: 'idle' }
const PENDING_WRITE: GitHubPublicationApprovalWritePhase = { kind: 'pending' }

const createScopeFrame = (): ScopeFrame => ({ open: false, serial: 0 })
const createOpeningFrame = (): OpeningFrame => ({ load: IDLE_LOAD, write: IDLE_WRITE, epoch: 0 })

const isDefiniteRefusal = (caught: unknown) =>
  ApiException.isApiException(caught) && [400, 404, 409, 413].includes((caught as ApiException).status)

/**
 * The explicit approval of ONE future normal push of one recorded delivery (ADR-0048). Two owned lifetimes: the committed scope (the exact
 * project, run, operation, commit, branch and target; collapsed by default; another scope, a return to an earlier one or the unmount ends
 * it) and each opening of that scope (closing and reopening is a new lifetime that starts with nothing read or approved). Nothing reads or
 * writes except `reload()` and `approve()`: not mount, opening, a scope change, a reconnect, a timer or a background refresh, nothing is
 * retried, and nothing is defaulted from another panel, an observation or browser storage.
 *
 * One in-flight guard covers both actions, so two activations in one tick, or a second one while one is pending, send one request. A
 * completion that belongs to an ended lifetime is ignored. An approval is armed only by a settled ApprovalNotRecorded read of this opening
 * and every read and write disarms it synchronously, so a callback retained from an earlier render (including A to B to A and same-batch
 * cases) can never submit facts it captured. One request identifier is created by the first approval that actually sends and is kept for
 * the opening, so a recovery retry after an explicit reload of the same scope repeats it. After any failed, refused or ambiguous write the
 * old reading is cleared (Not loaded) and a fixed alert stays: an explicit reload must come before another approval. Every answer is
 * accepted only whole and only for exactly this scope.
 */
export function useGitHubBranchPublicationApproval(scope: PublicationApprovalScope | null): UseGitHubBranchPublicationApprovalResult {
  const scopeKey = scope === null ? null : publicationApprovalScopeKey(scope)
  const scopeOwner = useOwnedLifetime(scopeKey)
  const [scopeFrame, commitScope] = useOwnedState(scopeOwner, createScopeFrame)
  const open = scopeKey !== null && scopeFrame.open
  const serial = scopeFrame.serial
  const owner = useOwnedLifetime(open ? JSON.stringify([scopeKey, serial]) : null)
  const [opening, commit] = useOwnedState(owner, createOpeningFrame)

  const inFlight = useRef<OwnedLifetime | null>(null)
  const closed = useRef(new WeakSet<OwnedLifetime>())
  // What an approval may use, set only by the settled read of this opening and cleared synchronously by every action.
  const armed = useRef<{ owner: OwnedLifetime; approvable: boolean; bindingId: string | null; nodeId: string | null } | null>(null)
  const epochs = useRef(new WeakMap<OwnedLifetime, number>())
  const requestIds = useRef(new WeakMap<OwnedLifetime, string>())

  const facts = useCallback(() => {
    if (armed.current?.owner !== owner) {
      armed.current = { owner, approvable: false, bindingId: null, nodeId: null }
    }

    return armed.current
  }, [owner])

  const advance = useCallback(() => {
    const next = (epochs.current.get(owner) ?? 0) + 1
    epochs.current.set(owner, next)
    return next
  }, [owner])

  const usable = useCallback(
    (candidate: OwnedLifetime) => candidate.isActive() && candidate.key !== null && !closed.current.has(candidate) && inFlight.current !== candidate,
    [],
  )

  const show = useCallback(() => {
    if (!scopeOwner.isActive() || scopeOwner.key === null) {
      return
    }

    commitScope((previous) => (previous.open ? previous : { open: true, serial: previous.serial + 1 }))
  }, [scopeOwner, commitScope])

  const close = useCallback(() => {
    if (owner.key !== null) {
      closed.current.add(owner)
    }

    commitScope((previous) => (previous.open && previous.serial === serial ? { open: false, serial: previous.serial } : previous))
  }, [owner, commitScope, serial])

  const reload = useCallback(() => {
    if (scope === null || !usable(owner)) {
      return
    }

    const isCurrent = owner.begin('channel')
    inFlight.current = owner
    const current = facts()
    current.approvable = false
    const startEpoch = advance()
    const settle = (next: GitHubPublicationApprovalLoadPhase) => {
      if (!isCurrent()) {
        return
      }

      inFlight.current = null
      const reading = next.kind === 'result' && next.reading.kind === 'ApprovalNotRecorded' ? next.reading : null
      current.approvable = reading !== null
      current.bindingId = reading?.binding.id ?? null
      current.nodeId = reading?.binding.repositoryNodeId ?? null
      const epoch = advance()
      // A coherent read is the way out of an unknown write outcome: only it clears a failed write.
      commit((previous) => ({
        ...previous,
        load: next,
        write: next.kind === 'result' && previous.write.kind === 'failed' ? IDLE_WRITE : previous.write,
        epoch,
      }))
    }

    commit((previous) => ({ ...previous, load: PENDING_LOAD, epoch: startEpoch }))
    gitHubBranchPublicationApprovalClient()
      .getGitHubBranchPublicationApproval(scope.source.runId)
      .then(
        (response) => {
          const reading = toPublicationApprovalReading(response, scope)
          settle(reading === null ? { kind: 'failed', reason: 'invalid' } : { kind: 'result', reading })
        },
        () => settle({ kind: 'failed', reason: 'read' }),
      )
  }, [scope, owner, commit, usable, facts, advance])

  const canApprove = open && opening.load.kind === 'result' && opening.load.reading.kind === 'ApprovalNotRecorded' && opening.write.kind === 'idle'

  const renderEpoch = opening.epoch
  const approve = useCallback(() => {
    const current = armed.current
    if (
      scope === null
      || !canApprove
      || !usable(owner)
      || current?.owner !== owner
      || !current.approvable
      || current.bindingId === null
      || current.nodeId === null
      || (epochs.current.get(owner) ?? 0) !== renderEpoch
    ) {
      return
    }

    const bindingId = current.bindingId
    const repositoryNodeId = current.nodeId
    let approvalId = requestIds.current.get(owner)
    if (approvalId === undefined) {
      approvalId = crypto.randomUUID()
      requestIds.current.set(owner, approvalId)
    }

    const submittedId = approvalId
    const isCurrent = owner.begin('channel')
    inFlight.current = owner
    // Nothing is armed again until an explicit reload settles for this opening.
    current.approvable = false
    current.bindingId = null
    current.nodeId = null
    const startEpoch = advance()
    const settle = (write: GitHubPublicationApprovalWritePhase, load?: GitHubPublicationApprovalLoadPhase) => {
      if (!isCurrent()) {
        return
      }

      inFlight.current = null
      const epoch = advance()
      commit((previous) => ({
        ...previous,
        write,
        // An unsuccessful write leaves the earlier read stale: only an explicit reload supplies the next authoritative reading.
        load: load ?? (write.kind === 'failed' ? IDLE_LOAD : previous.load),
        epoch,
      }))
    }

    commit((previous) => ({ ...previous, write: PENDING_WRITE, epoch: startEpoch }))
    recordGitHubBranchPublicationApprovalClient()
      .recordGitHubBranchPublicationApproval(
        scope.source.runId,
        new RecordGitHubBranchPublicationApprovalRequest({
          approvalId: submittedId,
          operationId: scope.source.operationId,
          commitSha: scope.source.commitSha,
          branchName: scope.source.branchName,
          targetId: scope.target.id,
          bindingId,
          repositoryNodeId,
        }),
      )
      .then(
        (response) => {
          const recorded = toRecordedPublicationApproval(response, scope, { approvalId: submittedId, bindingId, repositoryNodeId })
          if (recorded === null) {
            settle({ kind: 'failed', reason: 'ambiguous' })
          } else {
            settle(IDLE_WRITE, { kind: 'result', reading: { kind: 'Available', binding: recorded.binding, approval: recorded.approval } })
          }
        },
        (caught: unknown) => settle({ kind: 'failed', reason: isDefiniteRefusal(caught) ? 'refused' : 'ambiguous' }),
      )
  }, [scope, canApprove, owner, commit, usable, renderEpoch, advance])

  return {
    open,
    load: open ? opening.load : IDLE_LOAD,
    write: open ? opening.write : IDLE_WRITE,
    canApprove,
    show,
    close,
    reload,
    approve,
  }
}
