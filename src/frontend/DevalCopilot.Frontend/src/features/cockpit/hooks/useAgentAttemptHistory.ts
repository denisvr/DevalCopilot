import { useCallback, useEffect, useRef, useState } from 'react'
import type { AgentAttemptHistoryEntryResponse, AgentAttemptHistoryResponse } from '../../../api/clients'
import { agentAttemptHistoryClient } from '../../../api/clients'

/** The server also caps a page at 20; this is the size this inspector asks for. */
export const HISTORY_PAGE_SIZE = 10

const HISTORY_ERROR_MESSAGE = 'The Agent attempt history could not be loaded.'

export type AgentAttemptHistoryState =
  | { status: 'loading'; items: readonly AgentAttemptHistoryEntryResponse[] }
  | {
      status: 'loaded'
      items: readonly AgentAttemptHistoryEntryResponse[]
      hasMore: boolean
    }
  // A failed first page or a failed later page: already-loaded rows stay visible, and a retry
  // re-requests the same cursor, so no page is skipped or duplicated.
  | { status: 'error'; items: readonly AgentAttemptHistoryEntryResponse[]; message: string }

export interface UseAgentAttemptHistoryResult {
  state: AgentAttemptHistoryState
  /** Loads the next older page (or retries a failed page) using the server-provided cursor only. */
  loadMore: () => void
}

/**
 * Pages one run's Agent attempts, newest first, using the server's stable exclusive
 * before-attempt-number cursor. The first page is requested when this hook mounts, so the parent
 * mounts it only once the owner opens the history; it resets to a fresh first page whenever `runId`
 * changes, and a response for a previous run or a previous mount can never be applied (a generation
 * counter guards every continuation). Holds only bounded metadata rows — never artifact text — and
 * never writes anything to browser storage or the URL.
 */
export function useAgentAttemptHistory(runId: string): UseAgentAttemptHistoryResult {
  const [state, setState] = useState<AgentAttemptHistoryState>({ status: 'loading', items: [] })
  const generationRef = useRef(0)
  const loadingRef = useRef(false)
  // The cursor the next request must send; null means "first page".
  const cursorRef = useRef<number | null>(null)
  const [renderedRunId, setRenderedRunId] = useState(runId)

  if (renderedRunId !== runId) {
    setRenderedRunId(runId)
    setState({ status: 'loading', items: [] })
  }

  const requestPage = useCallback(
    (generation: number, before: number | null, previous: readonly AgentAttemptHistoryEntryResponse[]) => {
      loadingRef.current = true
      agentAttemptHistoryClient()
        .getAgentAttemptHistory(runId, before, HISTORY_PAGE_SIZE)
        .then((page: AgentAttemptHistoryResponse) => {
          if (generationRef.current !== generation) {
            return
          }

          loadingRef.current = false
          const hasMore = page.hasMore === true && page.nextBeforeAttemptNumber !== undefined
          cursorRef.current = hasMore ? (page.nextBeforeAttemptNumber ?? null) : null
          setState({ status: 'loaded', items: [...previous, ...(page.items ?? [])], hasMore })
        })
        .catch(() => {
          if (generationRef.current === generation) {
            loadingRef.current = false
            setState({ status: 'error', items: previous, message: HISTORY_ERROR_MESSAGE })
          }
        })
    },
    [runId],
  )

  useEffect(() => {
    generationRef.current += 1
    cursorRef.current = null
    requestPage(generationRef.current, null, [])
    return () => {
      // Invalidates any in-flight response for this run/mount.
      generationRef.current += 1
      loadingRef.current = false
    }
  }, [requestPage])

  const loadMore = useCallback(() => {
    if (loadingRef.current || state.status === 'loading') {
      return
    }

    if (state.status === 'loaded' && !state.hasMore) {
      return
    }

    const previous = state.items
    // A failed FIRST page has no rows and the cursor is still null, so retrying it asks for the
    // first page again; a failed later page retries the same cursor.
    setState({ status: 'loading', items: previous })
    requestPage(generationRef.current, cursorRef.current, previous)
  }, [requestPage, state])

  return { state, loadMore }
}
