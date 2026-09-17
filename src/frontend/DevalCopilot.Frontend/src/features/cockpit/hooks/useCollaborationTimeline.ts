import { useEffect, useState } from 'react'
import type { CollaborationMessageTimelineResponse } from '../../../api/clients'
import { collaborationTimelineClient } from '../../../api/clients'
import type { CollaborationTimelineCard } from '../types'

interface CollaborationTimelineState {
  runId: string
  eventSequence: number | undefined
  cards: CollaborationTimelineCard[]
  status: 'success' | 'error'
  error: string | null
}

export interface UseCollaborationTimelineResult {
  cards: CollaborationTimelineCard[]
  loading: boolean
  error: string | null
  hasSuccessfulResponse: boolean
}

function parseDetails(structuredContentJson: string | undefined): string[] {
  if (!structuredContentJson) {
    return []
  }

  try {
    const parsed: unknown = JSON.parse(structuredContentJson)
    if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
      return []
    }

    return Object.entries(parsed).flatMap(([field, value]) => (typeof value === 'string' ? [`${field}: ${value}`] : []))
  } catch {
    return []
  }
}

function toCard(message: CollaborationMessageTimelineResponse): CollaborationTimelineCard {
  return {
    sequence: message.sequence ?? 0,
    id: message.id ?? '',
    attemptId: message.attemptId ?? null,
    actor: message.actor ?? '',
    recipient: message.recipient ?? '',
    type: message.type ?? '',
    inReplyToMessageId: message.inReplyToMessageId ?? null,
    summary: message.summary ?? '',
    details: parseDetails(message.structuredContentJson),
    provenance: message.provenance ?? '',
    occurredAtUtc: (message.occurredAtUtc as unknown as string) ?? '',
  }
}

/**
 * Reads only bounded, project-owned protocol envelopes. The run event cursor remains the
 * live notification source; a newer event sequence requests a fresh bounded timeline.
 */
export function useCollaborationTimeline(runId: string | null, latestEventSequence: number | undefined) {
  const [timeline, setTimeline] = useState<CollaborationTimelineState>({
    runId: '',
    eventSequence: undefined,
    cards: [],
    status: 'success',
    error: null,
  })

  useEffect(() => {
    if (!runId) {
      return
    }

    let current = true
    collaborationTimelineClient()
      .getCollaborationTimeline(runId)
      .then((timeline) => {
        if (current) {
          setTimeline({
            runId,
            eventSequence: latestEventSequence,
            cards: timeline.map(toCard),
            status: 'success',
            error: null,
          })
        }
      })
      .catch(() => {
        if (current) {
          setTimeline((previous) => ({
            runId,
            eventSequence: latestEventSequence,
            cards: previous.runId === runId ? previous.cards : [],
            status: 'error',
            error: 'Collaboration timeline is unavailable.',
          }))
        }
      })

    return () => {
      current = false
    }
  }, [runId, latestEventSequence])

  const belongsToSelectedRun = timeline.runId === runId
  const loading = Boolean(runId) && (!belongsToSelectedRun || timeline.eventSequence !== latestEventSequence)

  return {
    cards: belongsToSelectedRun ? timeline.cards : [],
    loading,
    error: belongsToSelectedRun ? timeline.error : null,
    hasSuccessfulResponse: belongsToSelectedRun && timeline.status === 'success',
  } satisfies UseCollaborationTimelineResult
}
