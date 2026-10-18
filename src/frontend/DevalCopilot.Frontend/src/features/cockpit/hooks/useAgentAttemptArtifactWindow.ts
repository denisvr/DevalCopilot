import { useCallback } from 'react'
import { agentAttemptArtifactWindowClient } from '../../../api/clients'
import { artifactPurposeRouteSegment } from '../artifactPurposes'
import { useSealedArtifactWindow } from './useSealedArtifactWindow'
import type { UseSealedAgentArtifactWindowResult } from './useSealedArtifactWindow'

/** The attempt the windows belong to: its selection lifetime and its exact run and identity. */
export interface AgentAttemptArtifactWindowTarget {
  selectionKey: string
  runId: string
  attemptId: string
}

/**
 * Sealed Agent-artifact windows for an attempt selected from a run's attempt history, scoped to exactly one selection and
 * `(runId, attemptId, purpose)` triple and independent of any collaboration message. The request maps the allowlisted recorded purpose to
 * the route's own closed segment (`GetAgentAttemptArtifactWindowEndpoint` answers anything else with a 404) and sends nothing for any other
 * value. Shares its validation, accumulation, per-lifetime reset and retry-without-duplication behavior with the message-linked hook through
 * `useSealedArtifactWindow`.
 */
export function useAgentAttemptArtifactWindow(
  { selectionKey, runId, attemptId }: AgentAttemptArtifactWindowTarget,
  purpose: string | null,
): UseSealedAgentArtifactWindowResult {
  const request = useCallback(
    (requestedPurpose: string, fromOffset: number, maxBytes: number) => {
      const segment = artifactPurposeRouteSegment(requestedPurpose)
      return segment === null
        ? Promise.reject(new Error('The artifact purpose is not allowlisted.'))
        : agentAttemptArtifactWindowClient().getAgentAttemptArtifactWindow(runId, attemptId, segment, fromOffset, maxBytes)
    },
    [runId, attemptId],
  )

  return useSealedArtifactWindow(JSON.stringify([selectionKey, runId, attemptId]), purpose, request)
}
