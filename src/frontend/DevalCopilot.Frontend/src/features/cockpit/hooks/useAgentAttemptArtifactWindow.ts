import { useCallback } from 'react'
import { agentAttemptArtifactWindowClient } from '../../../api/clients'
import { useSealedArtifactWindow } from './useSealedArtifactWindow'
import type { UseSealedAgentArtifactWindowResult } from './useSealedArtifactWindow'

/**
 * The attempt route's own closed purpose segments (`GetAgentAttemptArtifactWindowEndpoint` answers only these, anything else is a 404).
 * The evidence metadata names a purpose by its recorded name, so the request maps that allowlisted name to its segment and sends nothing
 * for any other value.
 */
const ROUTE_SEGMENTS: Readonly<Record<string, string>> = {
  AgentContextManifest: 'context-manifest',
  AgentStandardOutput: 'stdout',
  AgentStandardError: 'stderr',
  AgentFinalResponse: 'final-response',
}

/** The attempt the windows belong to: its selection lifetime and its exact run and identity. */
export interface AgentAttemptArtifactWindowTarget {
  selectionKey: string
  runId: string
  attemptId: string
}

/**
 * Sealed Agent-artifact windows for an attempt selected from a run's attempt history, scoped to exactly one selection and
 * `(runId, attemptId, purpose)` triple and independent of any collaboration message. Shares its accumulation, per-lifetime reset and
 * retry-without-duplication behavior with the message-linked hook through `useSealedArtifactWindow`.
 */
export function useAgentAttemptArtifactWindow(
  { selectionKey, runId, attemptId }: AgentAttemptArtifactWindowTarget,
  purpose: string | null,
): UseSealedAgentArtifactWindowResult {
  const request = useCallback(
    (requestedPurpose: string, fromOffset: number, maxBytes: number) => {
      const segment = Object.hasOwn(ROUTE_SEGMENTS, requestedPurpose) ? ROUTE_SEGMENTS[requestedPurpose] : null
      return segment === null
        ? Promise.reject(new Error('The artifact purpose is not allowlisted.'))
        : agentAttemptArtifactWindowClient().getAgentAttemptArtifactWindow(runId, attemptId, segment, fromOffset, maxBytes)
    },
    [runId, attemptId],
  )

  return useSealedArtifactWindow(JSON.stringify([selectionKey, runId, attemptId]), purpose, request)
}
