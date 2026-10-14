import { useCallback, useRef } from 'react'
import {
  BindGitHubRepositoryIdentityRequest,
  bindGitHubRepositoryIdentityClient,
  gitHubRepositoryIdentityBindingClient,
  gitHubRepositoryObservationClient,
} from '../../../api/clients'
import { ApiException } from '../../../api/generated/api-client'
import { toBindingReading, toBoundIdentity } from '../gitHubRepositoryIdentityBinding'
import type { RepositoryIdentityBindingReading } from '../gitHubRepositoryIdentityBinding'
import { repositoryObservationScopeKey, toRepositoryObservation } from '../gitHubRepositoryObservation'
import type { ObservedRepositoryIdentity, RepositoryObservationReading, RepositoryObservationScope } from '../gitHubRepositoryObservation'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'
import type { OwnedLifetime } from './useOwnedLifetime'

export type GitHubIdentityBindingLoadPhase =
  | { kind: 'idle' }
  | { kind: 'pending' }
  /** `read`: the request failed. `invalid`: the host answered, but not with a coherent answer for exactly this source and target. */
  | { kind: 'failed'; reason: 'read' | 'invalid' }
  | { kind: 'result'; reading: RepositoryIdentityBindingReading }

export type GitHubIdentityCandidatePhase =
  | { kind: 'idle' }
  | { kind: 'pending' }
  | { kind: 'failed'; reason: 'read' | 'invalid' }
  | { kind: 'result'; reading: RepositoryObservationReading }

export type GitHubIdentityBindingWritePhase =
  | { kind: 'idle' }
  | { kind: 'pending' }
  /** `refused`: the host answered with a definite refusal. `ambiguous`: the outcome is unknown. Either way a reload and a new check must come first. */
  | { kind: 'failed'; reason: 'refused' | 'ambiguous' }

export interface UseGitHubRepositoryIdentityBindingResult {
  /** Whether the region is expanded; false (collapsed) for every newly committed scope. */
  open: boolean
  load: GitHubIdentityBindingLoadPhase
  candidate: GitHubIdentityCandidatePhase
  write: GitHubIdentityBindingWritePhase
  /** The identity the explicit candidate check observed for exactly this scope, or null. It is what a bind would confirm. */
  candidateIdentity: ObservedRepositoryIdentity | null
  /** True only when the opening may bind right now: a settled bindable NotRecorded read, an Observed candidate and nothing in flight. */
  canBind: boolean
  show: () => void
  /** Closes the opening that created this callback, discarding its answers and anything still in flight. */
  close: () => void
  /** The one explicit read of the recorded binding (load or reload). It disarms binding synchronously. */
  reload: () => void
  /** The one explicit candidate observation through ADR-0041 (check or recheck). It disarms binding synchronously. */
  check: () => void
  /** The one explicit write. A no-op unless `canBind` for the render that created this callback. */
  bind: () => void
}

interface ScopeFrame {
  open: boolean
  serial: number
}

interface OpeningFrame {
  load: GitHubIdentityBindingLoadPhase
  candidate: GitHubIdentityCandidatePhase
  write: GitHubIdentityBindingWritePhase
  /** Advanced by every change of what a bind depends on; mirrored synchronously by a ref so a retained callback can be recognized. */
  epoch: number
}

const IDLE_LOAD: GitHubIdentityBindingLoadPhase = { kind: 'idle' }
const PENDING_LOAD: GitHubIdentityBindingLoadPhase = { kind: 'pending' }
const IDLE_CANDIDATE: GitHubIdentityCandidatePhase = { kind: 'idle' }
const PENDING_CANDIDATE: GitHubIdentityCandidatePhase = { kind: 'pending' }
const IDLE_WRITE: GitHubIdentityBindingWritePhase = { kind: 'idle' }
const PENDING_WRITE: GitHubIdentityBindingWritePhase = { kind: 'pending' }

const createScopeFrame = (): ScopeFrame => ({ open: false, serial: 0 })
const createOpeningFrame = (): OpeningFrame => ({ load: IDLE_LOAD, candidate: IDLE_CANDIDATE, write: IDLE_WRITE, epoch: 0 })

const isDefiniteRefusal = (caught: unknown) =>
  ApiException.isApiException(caught) && [400, 404, 409].includes((caught as ApiException).status)

const candidateIdentityOf = (candidate: GitHubIdentityCandidatePhase): ObservedRepositoryIdentity | null =>
  candidate.kind === 'result' && candidate.reading.status === 'Observed' ? candidate.reading.identity : null

/**
 * The explicit binding of the opaque repository ID of one recorded ADR-0040 target (ADR-0043). Two owned lifetimes: the committed scope
 * (the exact run, operation, commit, branch and target; collapsed by default; another scope, a return to an earlier one or the unmount
 * ends it) and each opening of that scope (closing and reopening is a new lifetime that starts with nothing read, checked or bound).
 * Nothing reads or writes except `reload()`, `check()` and `bind()`: not mount, opening, a scope change, a reconnect, a timer or a
 * background refresh, nothing is retried, and nothing is defaulted from another panel, an origin or browser storage.
 *
 * One in-flight guard covers all three actions, so two activations in one tick, or a second one while one is pending, send one request.
 * A completion that belongs to an ended lifetime, or that was superseded, is ignored. A bind is armed only by a settled coherent
 * bindable read and an Observed candidate of this opening, and every read, check and write disarms it synchronously, so a callback retained
 * from an earlier render (including A to B to A and same-batch cases, and two actions in the same millisecond) can never submit a candidate
 * it captured. After any failed, refused or ambiguous write both the read and the candidate are cleared: an explicit reload and a new
 * check must come before another bind. Every answer is accepted only whole and only for exactly this scope.
 */
export function useGitHubRepositoryIdentityBinding(scope: RepositoryObservationScope | null): UseGitHubRepositoryIdentityBindingResult {
  const scopeKey = scope === null ? null : repositoryObservationScopeKey(scope)
  const scopeOwner = useOwnedLifetime(scopeKey)
  const [scopeFrame, commitScope] = useOwnedState(scopeOwner, createScopeFrame)
  const open = scopeKey !== null && scopeFrame.open
  const serial = scopeFrame.serial
  const owner = useOwnedLifetime(open ? JSON.stringify([scopeKey, serial]) : null)
  const [opening, commit] = useOwnedState(owner, createOpeningFrame)

  const inFlight = useRef<OwnedLifetime | null>(null)
  const closed = useRef(new WeakSet<OwnedLifetime>())
  // What a bind may use, set only by settled answers of this opening and cleared synchronously by every action.
  const armed = useRef<{ owner: OwnedLifetime; bindable: boolean; nodeId: string | null; recovering: boolean } | null>(null)
  const epochs = useRef(new WeakMap<OwnedLifetime, number>())

  const facts = useCallback(() => {
    if (armed.current?.owner !== owner) {
      armed.current = { owner, bindable: false, nodeId: null, recovering: false }
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
    facts().bindable = false
    const startEpoch = advance()
    const settle = (next: GitHubIdentityBindingLoadPhase) => {
      if (!isCurrent()) {
        return
      }

      inFlight.current = null
      const current = facts()
      current.bindable = next.kind === 'result' && next.reading.kind === 'NotRecorded' && next.reading.bindable
      // The recovery reload of a failed write: any candidate checked before it is discarded, so only a check that settles after it can arm.
      const recoveredNow = current.recovering && next.kind === 'result'
      if (recoveredNow) {
        current.recovering = false
        current.nodeId = null
      }

      const epoch = advance()
      // A coherent read is the way out of an unknown write outcome: only it clears a failed write.
      commit((previous) => ({
        ...previous,
        load: next,
        write: next.kind === 'result' && previous.write.kind === 'failed' ? IDLE_WRITE : previous.write,
        candidate: recoveredNow ? IDLE_CANDIDATE : previous.candidate,
        epoch,
      }))
    }

    commit((previous) => ({ ...previous, load: PENDING_LOAD, epoch: startEpoch }))
    gitHubRepositoryIdentityBindingClient()
      .getGitHubRepositoryIdentityBinding(scope.source.runId)
      .then(
        (response) => {
          const reading = toBindingReading(response, scope)
          settle(reading === null ? { kind: 'failed', reason: 'invalid' } : { kind: 'result', reading })
        },
        () => settle({ kind: 'failed', reason: 'read' }),
      )
  }, [scope, owner, commit, usable, facts, advance])

  const check = useCallback(() => {
    if (scope === null || !usable(owner)) {
      return
    }

    const isCurrent = owner.begin('channel')
    inFlight.current = owner
    facts().nodeId = null
    const startEpoch = advance()
    const settle = (next: GitHubIdentityCandidatePhase) => {
      if (!isCurrent()) {
        return
      }

      inFlight.current = null
      facts().nodeId = candidateIdentityOf(next)?.repositoryNodeId ?? null
      const epoch = advance()
      commit((previous) => ({ ...previous, candidate: next, epoch }))
    }

    commit((previous) => ({ ...previous, candidate: PENDING_CANDIDATE, epoch: startEpoch }))
    gitHubRepositoryObservationClient()
      .getGitHubRepositoryObservation(scope.source.runId)
      .then(
        (response) => {
          const reading = toRepositoryObservation(response, scope)
          settle(reading === null ? { kind: 'failed', reason: 'invalid' } : { kind: 'result', reading })
        },
        () => settle({ kind: 'failed', reason: 'read' }),
      )
  }, [scope, owner, commit, usable, facts, advance])

  const candidateIdentity = open ? candidateIdentityOf(opening.candidate) : null
  const canBind =
    open
    && opening.load.kind === 'result'
    && opening.load.reading.kind === 'NotRecorded'
    && opening.load.reading.bindable
    && candidateIdentity !== null
    && opening.write.kind === 'idle'

  const renderEpoch = opening.epoch
  const submittedNodeId = candidateIdentity?.repositoryNodeId ?? null
  const bind = useCallback(() => {
    const current = armed.current
    if (
      scope === null
      || !canBind
      || submittedNodeId === null
      || !usable(owner)
      || current?.owner !== owner
      || !current.bindable
      || current.nodeId !== submittedNodeId
      || (epochs.current.get(owner) ?? 0) !== renderEpoch
    ) {
      return
    }

    const isCurrent = owner.begin('channel')
    inFlight.current = owner
    // Nothing is armed again until an explicit reload and a new check settle for this opening.
    current.bindable = false
    current.nodeId = null
    // Until the write is known to have recorded a coherent binding, recovery is reload, then a new settled check, then bind.
    current.recovering = true
    const startEpoch = advance()
    const settle = (write: GitHubIdentityBindingWritePhase, load?: GitHubIdentityBindingLoadPhase) => {
      if (!isCurrent()) {
        return
      }

      inFlight.current = null
      if (write.kind === 'idle') {
        current.recovering = false
      }

      const epoch = advance()
      commit((previous) => ({
        ...previous,
        write,
        // An unsuccessful write leaves the earlier read stale: only an explicit reload supplies the next authoritative reading.
        load: load ?? (write.kind === 'failed' ? IDLE_LOAD : previous.load),
        candidate: IDLE_CANDIDATE,
        epoch,
      }))
    }

    commit((previous) => ({ ...previous, write: PENDING_WRITE, epoch: startEpoch }))
    bindGitHubRepositoryIdentityClient()
      .bindGitHubRepositoryIdentity(
        scope.source.runId,
        new BindGitHubRepositoryIdentityRequest({
          operationId: scope.source.operationId,
          commitSha: scope.source.commitSha,
          targetId: scope.target.id,
          repositoryNodeId: submittedNodeId,
        }),
      )
      .then(
        (response) => {
          const binding = toBoundIdentity(response, scope, submittedNodeId)
          if (binding === null) {
            settle({ kind: 'failed', reason: 'ambiguous' })
          } else {
            settle(IDLE_WRITE, { kind: 'result', reading: { kind: 'Available', binding } })
          }
        },
        (caught: unknown) => settle({ kind: 'failed', reason: isDefiniteRefusal(caught) ? 'refused' : 'ambiguous' }),
      )
  }, [scope, canBind, submittedNodeId, owner, commit, usable, renderEpoch, advance])

  return {
    open,
    load: open ? opening.load : IDLE_LOAD,
    candidate: open ? opening.candidate : IDLE_CANDIDATE,
    write: open ? opening.write : IDLE_WRITE,
    candidateIdentity,
    canBind,
    show,
    close,
    reload,
    check,
    bind,
  }
}
