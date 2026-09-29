import { useCallback } from 'react'
import { agentAttemptArtifactWindowClient } from '../../../api/clients'
import { useSealedArtifactWindow } from './useSealedArtifactWindow'
import type { UseSealedAgentArtifactWindowResult } from './useSealedArtifactWindow'

/**
 * Sealed Agent-artifact windows for an attempt selected from the run's attempt history, scoped to
 * exactly one `(runId, attemptId, purpose)` triple and independent of any collaboration message.
 * Shares its accumulation, reset-on-change, and retry-without-duplication behavior with the
 * message-linked hook through `useSealedArtifactWindow`.
 */
export function useAgentAttemptArtifactWindow(
  runId: string,
  attemptId: string,
  purpose: string | null,
): UseSealedAgentArtifactWindowResult {
  const request = useCallback(
    (requestedPurpose: string, fromOffset: number, maxBytes: number) =>
      agentAttemptArtifactWindowClient().getAgentAttemptArtifactWindow(runId, attemptId, requestedPurpose, fromOffset, maxBytes),
    [runId, attemptId],
  )

  return useSealedArtifactWindow(`${runId}:${attemptId}`, purpose, request)
}
