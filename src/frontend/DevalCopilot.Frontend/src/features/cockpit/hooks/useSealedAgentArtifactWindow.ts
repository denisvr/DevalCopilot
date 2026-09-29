import { useCallback } from 'react'
import { sealedAgentArtifactWindowClient } from '../../../api/clients'
import { useSealedArtifactWindow } from './useSealedArtifactWindow'
import type { UseSealedAgentArtifactWindowResult } from './useSealedArtifactWindow'

export type { SealedAgentArtifactWindowState, UseSealedAgentArtifactWindowResult } from './useSealedArtifactWindow'

/**
 * Sealed Agent-artifact windows for the artifact of the attempt behind one collaboration message,
 * scoped to exactly one `(runId, messageId, purpose)` triple. The accumulation, reset-on-change,
 * and retry-without-duplication behavior lives in `useSealedArtifactWindow`, shared with the
 * attempt-history route so both behave identically.
 */
export function useSealedAgentArtifactWindow(
  runId: string,
  messageId: string,
  purpose: string | null,
): UseSealedAgentArtifactWindowResult {
  const request = useCallback(
    (requestedPurpose: string, fromOffset: number, maxBytes: number) =>
      sealedAgentArtifactWindowClient().getSealedAgentArtifactWindow(runId, messageId, requestedPurpose, fromOffset, maxBytes),
    [runId, messageId],
  )

  return useSealedArtifactWindow(`${runId}:${messageId}`, purpose, request)
}
