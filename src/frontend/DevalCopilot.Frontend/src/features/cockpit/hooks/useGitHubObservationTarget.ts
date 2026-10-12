import { useCallback, useRef } from 'react'
import { RecordGitHubObservationTargetRequest, gitHubObservationTargetClient, recordGitHubObservationTargetClient } from '../../../api/clients'
import { ApiException } from '../../../api/generated/api-client'
import { observationSourceKey, toObservationReading, toRecordedObservation } from '../gitHubObservationTarget'
import type { ObservationReading, ObservationSource } from '../gitHubObservationTarget'
import { isGitHubOwner, isGitHubRepository } from '../gitHubRepositoryNames'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'
import type { OwnedLifetime } from './useOwnedLifetime'

export type GitHubObservationLoadPhase =
  | { kind: 'idle' }
  | { kind: 'pending' }
  /** `read`: the request failed. `invalid`: the host answered, but not with a coherent answer for exactly this source. */
  | { kind: 'failed'; reason: 'read' | 'invalid' }
  | { kind: 'result'; reading: ObservationReading }

export type GitHubObservationWritePhase =
  | { kind: 'idle' }
  | { kind: 'pending' }
  /** `refused`: the host answered with a definite refusal. `ambiguous`: the outcome is unknown. Either way a reload must come first. */
  | { kind: 'failed'; reason: 'refused' | 'ambiguous' }

export interface UseGitHubObservationTargetResult {
  /** Whether the region is expanded; false (collapsed) for every newly committed source. */
  open: boolean
  load: GitHubObservationLoadPhase
  write: GitHubObservationWritePhase
  owner: string
  repository: string
  /** True only when the opening may record right now: a settled NotRecorded and recordable reading, valid names and nothing in flight. */
  canRecord: boolean
  /** Opens the region without reading anything. */
  show: () => void
  /** Closes the opening that created this callback, discarding its draft, answers and anything still in flight. */
  close: () => void
  /** The one explicit read (load or reload). A no-op while collapsed, while one is in flight, while recording or from an ended lifetime. */
  reload: () => void
  setOwner: (value: string) => void
  setRepository: (value: string) => void
  /** The one explicit write. A no-op unless `canRecord`; a second activation in flight, or in the same batch, sends one request. */
  record: () => void
}

// What the committed source owns: whether its region is expanded, and how many times it was opened, so each opening is a lifetime.
interface SourceFrame {
  open: boolean
  serial: number
}

// What one opening owns: the draft, the last coherent reading and the write outcome. Closing discards all of it.
interface OpeningFrame {
  load: GitHubObservationLoadPhase
  write: GitHubObservationWritePhase
  owner: string
  repository: string
  /** Advanced by every edit, so a callback created for an earlier draft can be recognized. Mirrored synchronously by a ref. */
  revision: number
}

const IDLE_LOAD: GitHubObservationLoadPhase = { kind: 'idle' }
const PENDING_LOAD: GitHubObservationLoadPhase = { kind: 'pending' }
const IDLE_WRITE: GitHubObservationWritePhase = { kind: 'idle' }
const PENDING_WRITE: GitHubObservationWritePhase = { kind: 'pending' }

const createSourceFrame = (): SourceFrame => ({ open: false, serial: 0 })
const createOpeningFrame = (): OpeningFrame => ({ load: IDLE_LOAD, write: IDLE_WRITE, owner: '', repository: '', revision: 0 })

const isDefiniteRefusal = (caught: unknown) =>
  ApiException.isApiException(caught) && [400, 404, 409].includes((caught as ApiException).status)

/**
 * The explicit, local Human selection of the immutable GitHub observation scope of one completed local delivery (ADR-0040). Two
 * owned lifetimes: the committed source (run, operation, commit and branch; collapsed by default; another source, a return to an
 * earlier one, no source or the unmount ends it) and each opening of that source (closing and reopening is a new lifetime with a
 * blank draft and nothing loaded). Nothing reads or writes except `reload()` and `record()`: not mount, opening, a source change,
 * a reconnect, a timer or a background refresh, and nothing is defaulted from an origin or an authentication observation.
 *
 * A completion that belongs to an ended lifetime, or that was superseded, is ignored (the host request itself still happened). A
 * second activation while one is in flight, two activations in the same batch, and any activation after `close` in the same batch
 * send nothing. Recording is armed only by a settled, coherent, recordable NotRecorded reading of this opening and is disarmed
 * synchronously by the next read or write: after any failed or incoherent recording the outcome is unknown, so nothing can be
 * recorded again, not even by a callback retained from an earlier render, until an explicit reload has read what the host recorded.
 * Every answer is accepted only whole and only for exactly this source, and an accepted recording must repeat exactly the names
 * that were submitted.
 */
export function useGitHubObservationTarget(source: ObservationSource | null): UseGitHubObservationTargetResult {
  const sourceKey = source === null ? null : observationSourceKey(source)
  const sourceOwner = useOwnedLifetime(sourceKey)
  const [sourceFrame, commitSource] = useOwnedState(sourceOwner, createSourceFrame)
  const open = sourceKey !== null && sourceFrame.open
  const serial = sourceFrame.serial
  const owner = useOwnedLifetime(open ? JSON.stringify([sourceKey, serial]) : null)
  const [opening, commit] = useOwnedState(owner, createOpeningFrame)

  // The one read or write in flight, keyed by its lifetime, so a callback retained from an earlier lifetime can never act on a later
  // one and two calls in the same tick send one request.
  const inFlight = useRef<{ owner: OwnedLifetime; isCurrent: () => boolean } | null>(null)
  // Whether the opening may record: set only by a settled recordable reading and cleared synchronously by every read and write.
  const armed = useRef<{ owner: OwnedLifetime; value: boolean } | null>(null)
  // Openings closed by `close()` in this batch: the lifetime only ends when the closing commits, so a later call of the same batch
  // must still be refused.
  const closed = useRef(new WeakSet<OwnedLifetime>())
  // The draft revision of each opening, advanced synchronously by every edit (the rendered state advances with it). A record callback is
  // bound to the revision of the render that created it, so a callback retained from an earlier draft (an owner or repository change,
  // A to B to A, or an edit in the same batch) can never submit the names it captured; only a callback created for the current draft can.
  const revisions = useRef(new WeakMap<OwnedLifetime, number>())

  const usable = useCallback(
    (candidate: OwnedLifetime) =>
      candidate.isActive() && candidate.key !== null && !closed.current.has(candidate) && inFlight.current?.owner !== candidate,
    [],
  )

  const show = useCallback(() => {
    if (!sourceOwner.isActive() || sourceOwner.key === null) {
      return
    }

    commitSource((previous) => (previous.open ? previous : { open: true, serial: previous.serial + 1 }))
  }, [sourceOwner, commitSource])

  const close = useCallback(() => {
    if (owner.key !== null) {
      closed.current.add(owner)
    }

    commitSource((previous) => (previous.open && previous.serial === serial ? { open: false, serial: previous.serial } : previous))
  }, [owner, commitSource, serial])

  const reload = useCallback(() => {
    if (source === null || !usable(owner)) {
      return
    }

    const isCurrent = owner.begin('channel')
    inFlight.current = { owner, isCurrent }
    armed.current = { owner, value: false }
    const settle = (next: GitHubObservationLoadPhase) => {
      if (isCurrent()) {
        inFlight.current = null
        armed.current = { owner, value: next.kind === 'result' && next.reading.kind === 'NotRecorded' && next.reading.recordable }
        // A coherent reading is the way out of an unknown recording outcome: only it clears a failed write.
        commit((previous) => ({
          ...previous,
          load: next,
          write: next.kind === 'result' && previous.write.kind === 'failed' ? IDLE_WRITE : previous.write,
        }))
      }
    }

    commit((previous) => ({ ...previous, load: PENDING_LOAD }))
    gitHubObservationTargetClient()
      .getGitHubObservationTarget(source.runId)
      .then(
        (response) => {
          const reading = toObservationReading(response, source)
          settle(reading === null ? { kind: 'failed', reason: 'invalid' } : { kind: 'result', reading })
        },
        () => settle({ kind: 'failed', reason: 'read' }),
      )
  }, [source, owner, commit, usable])

  const canRecord =
    open
    && opening.load.kind === 'result'
    && opening.load.reading.kind === 'NotRecorded'
    && opening.load.reading.recordable
    && opening.write.kind === 'idle'
    && isGitHubOwner(opening.owner)
    && isGitHubRepository(opening.repository)

  const draftOwner = opening.owner
  const draftRepository = opening.repository
  const draftRevision = opening.revision
  const record = useCallback(() => {
    if (
      source === null
      || !canRecord
      || !usable(owner)
      || armed.current?.owner !== owner
      || !armed.current.value
      || (revisions.current.get(owner) ?? 0) !== draftRevision
    ) {
      return
    }

    const submitted = { owner: draftOwner, repository: draftRepository }
    const isCurrent = owner.begin('channel')
    inFlight.current = { owner, isCurrent }
    armed.current = { owner, value: false }
    const settle = (next: GitHubObservationWritePhase, reading?: ObservationReading) => {
      if (isCurrent()) {
        inFlight.current = null
        commit((previous) => ({ ...previous, write: next, load: reading ? { kind: 'result', reading } : previous.load }))
      }
    }

    commit((previous) => ({ ...previous, write: PENDING_WRITE }))
    recordGitHubObservationTargetClient()
      .recordGitHubObservationTarget(
        source.runId,
        new RecordGitHubObservationTargetRequest({
          operationId: source.operationId,
          commitSha: source.commitSha,
          owner: submitted.owner,
          repository: submitted.repository,
        }),
      )
      .then(
        (response) => {
          const target = toRecordedObservation(response, source, submitted)
          if (target === null) {
            settle({ kind: 'failed', reason: 'ambiguous' })
          } else {
            settle(IDLE_WRITE, { kind: 'Available', target })
          }
        },
        (caught: unknown) => settle({ kind: 'failed', reason: isDefiniteRefusal(caught) ? 'refused' : 'ambiguous' }),
      )
  }, [source, canRecord, owner, commit, usable, draftOwner, draftRepository, draftRevision])

  const setOwner = useCallback(
    (value: string) => {
      if (owner.isActive() && !closed.current.has(owner)) {
        revisions.current.set(owner, (revisions.current.get(owner) ?? 0) + 1)
        commit((previous) =>
          previous.write.kind === 'pending'
            ? { ...previous, revision: previous.revision + 1 }
            : { ...previous, owner: value, revision: previous.revision + 1 },
        )
      }
    },
    [owner, commit],
  )

  const setRepository = useCallback(
    (value: string) => {
      if (owner.isActive() && !closed.current.has(owner)) {
        revisions.current.set(owner, (revisions.current.get(owner) ?? 0) + 1)
        commit((previous) =>
          previous.write.kind === 'pending'
            ? { ...previous, revision: previous.revision + 1 }
            : { ...previous, repository: value, revision: previous.revision + 1 },
        )
      }
    },
    [owner, commit],
  )

  return {
    open,
    load: open ? opening.load : IDLE_LOAD,
    write: open ? opening.write : IDLE_WRITE,
    owner: open ? opening.owner : '',
    repository: open ? opening.repository : '',
    canRecord,
    show,
    close,
    reload,
    setOwner,
    setRepository,
    record,
  }
}
