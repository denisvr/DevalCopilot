import { useCallback, useEffect, useLayoutEffect, useRef } from 'react'
import {
  projectVerificationHistoryClient,
  projectVerificationInspectionClient,
  projectVerificationWindowClient,
} from '../../../api/clients'
import type { VerificationStream } from '../agentAttemptVerification'
import {
  decideProjectVerificationWindowAnswer,
  recordedDescriptorOf,
  toProjectVerificationHistory,
  toProjectVerificationInspection,
} from '../projectVerificationInspection'
import type {
  ProjectVerificationHistory,
  ProjectVerificationHistoryItem,
  ProjectVerificationInspection,
} from '../projectVerificationInspection'
import { startBoundedRead } from './boundedRead'
import type { BoundedReadAnswer, BoundedReadFailure } from './boundedRead'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'
import type { OwnedLifetime } from './useOwnedLifetime'
import { useRecordedStreamWindow } from './useRecordedStreamWindow'
import type { UseRecordedStreamWindowResult } from './useRecordedStreamWindow'

/**
 * `read`: the request failed. `not-found`: the host no longer knows this project or execution. `timeout`: it did not answer within ten
 * seconds. `invalid`: the answer is not coherent for the request (or differs from the immutable facts the page listed).
 */
export type ProjectVerificationInspectionFailure = BoundedReadFailure | 'invalid'

/** The one stream of the selected execution chosen for a manual read; only a Recorded descriptor can be chosen. */
export interface ProjectVerificationStreamSelection {
  stream: VerificationStream
  /** The visit of this choice within its execution visit: Hide and a later choice of the same stream is a new visit with a new number. */
  visit: number
  descriptor: NonNullable<ReturnType<typeof recordedDescriptorOf>>
}

export interface UseProjectVerificationInspectionResult {
  /** Whether the region of the current project is open. */
  open: boolean
  /** The lifetime of the current page visit (null while closed): the selection and everything below it belongs to it. */
  pageKey: string | null
  pending: boolean
  /** The validated page of this visit; never together with a failure. */
  page: ProjectVerificationHistory | null
  failure: ProjectVerificationInspectionFailure | null
  /** Opens the region and reads the newest page once; an open region is left as it is. */
  show: () => void
  /** Closes the opening that created this callback, discarding its page, selection, window and any read in flight. */
  close: () => void
  /** Replaces the displayed page by the next older one, by the displayed cursor; the selection of the earlier page ends first. */
  older: () => void
  /** Reads the newest page again as a new visit: the page, selection and content of the earlier visit end before the read starts. */
  reload: () => void
  /** Reads the same page again after a failure of this page visit; nothing else repeats a read. */
  retryPage: () => void
  /** The execution chosen on this page, as the accepted page lists it, or null. */
  selectedItem: ProjectVerificationHistoryItem | null
  /** The lifetime of the chosen execution visit (null: none). */
  executionKey: string | null
  executionPending: boolean
  inspection: ProjectVerificationInspection | null
  executionFailure: ProjectVerificationInspectionFailure | null
  /** Chooses one execution of the accepted page and reads only its metadata; an unknown execution is ignored. */
  selectExecution: (executionId: string) => void
  /** Clears the choice of the execution visit that created this callback; a later visit is never cleared by it. */
  clearExecution: () => void
  /** Reads the same execution again after a failure of this execution visit; nothing else repeats a read. */
  retryExecution: () => void
  /** The stream chosen for a manual read, or null. */
  selection: ProjectVerificationStreamSelection | null
  /** Chooses a stream whose accepted descriptor is Recorded; anything else is ignored. */
  selectStream: (stream: VerificationStream) => void
  /** Clears the choice of the stream visit that created this callback; a later visit, even of the same stream, is never cleared by it. */
  clearStream: () => void
  /** The manual windows of the chosen stream (idle until its explicit Load). */
  window: UseRecordedStreamWindowResult
}

interface OpeningFrame {
  open: boolean
  serial: number
  visit: number
  /** The cursor of this page visit: null reads the newest page. */
  before: number | null
}

interface PageFrame {
  pending: boolean
  page: ProjectVerificationHistory | null
  failure: ProjectVerificationInspectionFailure | null
}

interface ExecutionChoiceFrame {
  chosen: { executionId: string; visit: number } | null
  visits: number
}

interface ExecutionFrame {
  pending: boolean
  inspection: ProjectVerificationInspection | null
  failure: ProjectVerificationInspectionFailure | null
}

interface StreamChoiceFrame {
  chosen: { stream: VerificationStream; visit: number } | null
  visits: number
}

const createClosed = (): OpeningFrame => ({ open: false, serial: 0, visit: 0, before: null })
const createPendingPage = (): PageFrame => ({ pending: true, page: null, failure: null })
const createNoExecution = (): ExecutionChoiceFrame => ({ chosen: null, visits: 0 })
const createPendingExecution = (): ExecutionFrame => ({ pending: true, inspection: null, failure: null })
const createNoStream = (): StreamChoiceFrame => ({ chosen: null, visits: 0 })

function settledPage(answer: BoundedReadAnswer, decode: (raw: unknown) => ProjectVerificationHistory | null): PageFrame {
  if ('failure' in answer) {
    return { pending: false, page: null, failure: answer.failure }
  }

  const page = decode(answer.raw)
  return page === null ? { pending: false, page: null, failure: 'invalid' } : { pending: false, page, failure: null }
}

function settledExecution(
  answer: BoundedReadAnswer,
  decode: (raw: unknown) => ProjectVerificationInspection | null,
): ExecutionFrame {
  if ('failure' in answer) {
    return { pending: false, inspection: null, failure: answer.failure }
  }

  const inspection = decode(answer.raw)
  return inspection === null ? { pending: false, inspection: null, failure: 'invalid' } : { pending: false, inspection, failure: null }
}

/**
 * The explicit recorded inspection of one project's verification history (ADR-0069): one replaceable 20-item page, one chosen
 * execution's metadata and one chosen stream's manual windows. Lifetimes nest and each owns its callbacks and results: the project,
 * each opening, each page visit (an open, an older page, a reload or a retry), each execution visit and each stream visit. A replacement
 * derives its first frame in the same render, so another project, a return to an earlier one (A-B-A), closing, reopening, another page,
 * a reload or unmounting can never show or settle anything of an earlier lifetime, and a retained callback of an ended lifetime starts
 * and clears nothing. Changing the page or reloading ends the selection and its content in the same render that starts the read, so
 * nothing of the earlier visit is shown while the new answer is pending.
 *
 * Nothing is read until the owner opens the region; opening reads only the newest page, choosing an execution reads only its metadata and
 * only an explicit stream choice and Load reads a window. Every page and metadata read carries a real ten-second AbortSignal bound that
 * also ends with its visit, and so does every window request. Nothing is polled, drained, retried automatically, prefetched, cached
 * across lifetimes, accumulated beyond the displayed page, logged, stored, put in the URL or exported, and nothing here sends a
 * command or touches the live verification list or its output viewer.
 */
export function useProjectVerificationInspection(projectId: string | null): UseProjectVerificationInspectionResult {
  const projectOwner = useOwnedLifetime(projectId)
  const [opening, commitOpening] = useOwnedState(projectOwner, createClosed)
  const isOpen = projectId !== null && opening.open
  const pageKey = isOpen ? JSON.stringify([projectId, opening.serial, opening.visit]) : null
  const pageOwner = useOwnedLifetime(pageKey)
  const [pageFrame, commitPage] = useOwnedState(pageOwner, createPendingPage)
  const [executionChoice, commitExecutionChoice] = useOwnedState(pageOwner, createNoExecution)

  const displayedPage = useRef<{ owner: OwnedLifetime; frame: PageFrame } | null>(null)
  useLayoutEffect(() => {
    displayedPage.current = { owner: pageOwner, frame: pageFrame }
  })

  const show = useCallback(() => {
    if (!projectOwner.isActive() || projectOwner.key === null) {
      return
    }

    commitOpening((previous) => (previous.open ? previous : { open: true, serial: previous.serial + 1, visit: 1, before: null }))
  }, [projectOwner, commitOpening])

  const { serial, visit, before } = opening
  const close = useCallback(() => {
    if (!pageOwner.isActive() || pageOwner.key === null) {
      return
    }

    commitOpening((previous) => (previous.serial === serial && previous.open ? { ...previous, open: false } : previous))
  }, [pageOwner, serial, commitOpening])

  // Decided against the queued value: only the visit this callback was rendered for is replaced, once.
  const replaceVisit = useCallback(
    (nextBefore: number | null) => {
      commitOpening((previous) =>
        previous.open && previous.serial === serial && previous.visit === visit
          ? { ...previous, visit: previous.visit + 1, before: nextBefore }
          : previous,
      )
    },
    [serial, visit, commitOpening],
  )

  const reload = useCallback(() => {
    if (!pageOwner.isActive() || pageOwner.key === null) {
      return
    }

    replaceVisit(null)
  }, [pageOwner, replaceVisit])

  const older = useCallback(() => {
    const current = displayedPage.current
    if (!pageOwner.isActive() || pageOwner.key === null || current === null || current.owner !== pageOwner || current.frame.pending) {
      return
    }

    const page = current.frame.page
    if (page?.status !== 'Recorded' || !page.hasMore || page.nextBeforeExecutionNumber === null) {
      return
    }

    replaceVisit(page.nextBeforeExecutionNumber)
  }, [pageOwner, replaceVisit])

  const retryPage = useCallback(() => {
    const current = displayedPage.current
    if (!pageOwner.isActive() || pageOwner.key === null || current === null || current.owner !== pageOwner) {
      return
    }

    if (current.frame.failure === null || current.frame.pending) {
      return
    }

    replaceVisit(before)
  }, [pageOwner, replaceVisit, before])

  useEffect(() => {
    if (pageOwner.key === null || projectId === null) {
      return undefined
    }

    const isCurrent = pageOwner.begin('page')
    commitPage(createPendingPage)
    const read = startBoundedRead((signal) => projectVerificationHistoryClient(signal).readPage(projectId, before))
    void read.answer.then((answer) => {
      if (isCurrent()) {
        commitPage(() =>
          settledPage(answer, (raw) => toProjectVerificationHistory(raw, { projectId, beforeExecutionNumber: before })),
        )
      }
    })
    return () => read.cancel()
  }, [pageOwner, projectId, before, commitPage])

  const page = isOpen ? pageFrame.page : null
  const items = page?.status === 'Recorded' ? page.items : null

  const selectExecution = useCallback(
    (executionId: string) => {
      const current = displayedPage.current
      if (!pageOwner.isActive() || pageOwner.key === null || current === null || current.owner !== pageOwner) {
        return
      }

      const shown = current.frame.page
      if (shown?.status !== 'Recorded' || !shown.items.some((item) => item.executionId === executionId)) {
        return
      }

      // Decided against the queued choice, so a Hide queued earlier in the same batch makes this a new visit.
      commitExecutionChoice((previous) =>
        previous.chosen?.executionId === executionId
          ? previous
          : { chosen: { executionId, visit: previous.visits + 1 }, visits: previous.visits + 1 },
      )
    },
    [pageOwner, commitExecutionChoice],
  )

  const chosenExecution = isOpen && items !== null ? executionChoice.chosen : null
  const selectedItem =
    chosenExecution === null || items === null ? null : (items.find((item) => item.executionId === chosenExecution.executionId) ?? null)
  const executionVisit = selectedItem === null ? null : (chosenExecution?.visit ?? null)
  const executionKey =
    selectedItem === null || pageKey === null ? null : JSON.stringify([pageKey, selectedItem.executionId, executionVisit])
  const executionOwner = useOwnedLifetime(executionKey)
  const [executionFrame, commitExecution] = useOwnedState(executionOwner, createPendingExecution)
  const [streamChoice, commitStreamChoice] = useOwnedState(executionOwner, createNoStream)

  const clearExecution = useCallback(() => {
    if (executionVisit === null || !pageOwner.isActive() || pageOwner.key === null) {
      return
    }

    commitExecutionChoice((previous) => (previous.chosen?.visit === executionVisit ? { ...previous, chosen: null } : previous))
  }, [pageOwner, executionVisit, commitExecutionChoice])

  const displayedExecution = useRef<{ owner: OwnedLifetime; frame: ExecutionFrame } | null>(null)
  const indexedItem = useRef(selectedItem)
  const executionRead = useRef<{ owner: OwnedLifetime; cancel: () => void } | null>(null)
  useLayoutEffect(() => {
    displayedExecution.current = { owner: executionOwner, frame: executionFrame }
    indexedItem.current = selectedItem
  })

  const readExecution = useCallback(() => {
    const item = indexedItem.current
    if (!executionOwner.isActive() || executionOwner.key === null || projectId === null || item === null) {
      return
    }

    executionRead.current?.cancel()
    const isCurrent = executionOwner.begin('execution')
    const read = startBoundedRead((signal) => projectVerificationInspectionClient(signal).readExecution(projectId, item.executionId))
    executionRead.current = { owner: executionOwner, cancel: read.cancel }
    commitExecution(createPendingExecution)
    void read.answer.then((answer) => {
      if (isCurrent()) {
        commitExecution(() => settledExecution(answer, (raw) => toProjectVerificationInspection(raw, { projectId, item })))
      }
    })
  }, [executionOwner, projectId, commitExecution])

  useEffect(() => {
    readExecution()
    return () => {
      if (executionRead.current?.owner === executionOwner) {
        executionRead.current.cancel()
        executionRead.current = null
      }
    }
  }, [readExecution, executionOwner])

  const retryExecution = useCallback(() => {
    const current = displayedExecution.current
    if (!executionOwner.isActive() || executionOwner.key === null || current === null || current.owner !== executionOwner) {
      return
    }

    if (current.frame.failure === null || current.frame.pending) {
      return
    }

    readExecution()
  }, [executionOwner, readExecution])

  const inspection = selectedItem === null ? null : executionFrame.inspection

  const selectStream = useCallback(
    (stream: VerificationStream) => {
      const current = displayedExecution.current
      if (!executionOwner.isActive() || executionOwner.key === null || current === null || current.owner !== executionOwner) {
        return
      }

      if (recordedDescriptorOf(current.frame.inspection, stream) === null) {
        return
      }

      // Decided against the queued choice, so a Hide queued earlier in the same batch makes this a new visit.
      commitStreamChoice((previous) =>
        previous.chosen?.stream === stream ? previous : { chosen: { stream, visit: previous.visits + 1 }, visits: previous.visits + 1 },
      )
    },
    [executionOwner, commitStreamChoice],
  )

  const chosenStream = inspection?.status === 'Recorded' ? streamChoice.chosen : null
  const descriptor = chosenStream === null ? null : recordedDescriptorOf(inspection, chosenStream.stream)
  const selection = chosenStream !== null && descriptor !== null ? { ...chosenStream, descriptor } : null

  const streamVisit = selection === null ? null : (chosenStream?.visit ?? null)
  const clearStream = useCallback(() => {
    if (streamVisit === null || !executionOwner.isActive() || executionOwner.key === null) {
      return
    }

    commitStreamChoice((previous) => (previous.chosen?.visit === streamVisit ? { ...previous, chosen: null } : previous))
  }, [executionOwner, streamVisit, commitStreamChoice])

  const windowOwnerKey =
    selection === null || executionKey === null ? null : JSON.stringify([executionKey, selection.stream, selection.visit])
  const windowCancels = useRef(new Set<() => void>())
  useEffect(
    () => () => {
      // The stream visit ended (or the view unmounted): its requests in flight end with it, not only their continuations.
      for (const cancel of windowCancels.current) {
        cancel()
      }

      windowCancels.current.clear()
    },
    [windowOwnerKey],
  )

  const executionId = selectedItem?.executionId ?? ''
  const stream = selection?.stream ?? 'stdout'
  const streamWindow = useRecordedStreamWindow({
    ownerKey: windowOwnerKey,
    descriptor: selection?.descriptor ?? null,
    read: (fromOffset, maxBytes) => {
      const read = startBoundedRead((signal) =>
        projectVerificationWindowClient(signal).readWindow(projectId ?? '', executionId, stream, fromOffset, maxBytes),
      )
      windowCancels.current.add(read.cancel)
      return read.answer.then((answer) => {
        windowCancels.current.delete(read.cancel)
        if ('raw' in answer) {
          return answer.raw
        }

        // The window machine maps a 404 to its own not-found state and any other failure, a timeout included, to a read failure.
        throw answer.failure === 'not-found' ? { status: 404 } : new Error(`The window read ended: ${answer.failure}`)
      })
    },
    decide: (answer, request) =>
      decideProjectVerificationWindowAnswer(answer, {
        projectId: projectId ?? '',
        executionId,
        stream,
        descriptor: request.descriptor,
        fromOffset: request.fromOffset,
        maxBytes: request.maxBytes,
      }),
  })

  return {
    open: isOpen,
    pageKey,
    pending: isOpen && pageFrame.pending,
    page,
    failure: isOpen ? pageFrame.failure : null,
    show,
    close,
    older,
    reload,
    retryPage,
    selectedItem,
    executionKey,
    executionPending: selectedItem !== null && executionFrame.pending,
    inspection,
    executionFailure: selectedItem === null ? null : executionFrame.failure,
    selectExecution,
    clearExecution,
    retryExecution,
    selection,
    selectStream,
    clearStream,
    window: streamWindow,
  }
}
