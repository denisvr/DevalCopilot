import { useCallback } from 'react'
import { sealedAgentArtifactWindowClient } from '../../../api/clients'
import { artifactPurposeRouteSegment } from '../artifactPurposes'
import { useSealedArtifactWindow } from './useSealedArtifactWindow'
import type { UseSealedAgentArtifactWindowResult } from './useSealedArtifactWindow'

export type { SealedAgentArtifactWindowState, UseSealedAgentArtifactWindowResult } from './useSealedArtifactWindow'

/**
 * Sealed Agent-artifact windows for the artifact of the attempt behind one collaboration message,
 * scoped to exactly one `(runId, messageId, purpose)` triple. The request maps the allowlisted
 * recorded purpose to the route's own closed segment (`GetSealedAgentArtifactWindowEndpoint`
 * answers anything else with a 404) and sends nothing for any other value. The validation,
 * accumulation, reset-on-change, and retry-without-duplication behavior lives in
 * `useSealedArtifactWindow`, shared with the attempt-history route so both behave identically.
 */
export function useSealedAgentArtifactWindow(
  runId: string,
  messageId: string,
  purpose: string | null,
): UseSealedAgentArtifactWindowResult {
  const request = useCallback(
    (requestedPurpose: string, fromOffset: number, maxBytes: number) => {
      const segment = artifactPurposeRouteSegment(requestedPurpose)
      return segment === null
        ? Promise.reject(new Error('The artifact purpose is not allowlisted.'))
        : sealedAgentArtifactWindowClient().getSealedAgentArtifactWindow(runId, messageId, segment, fromOffset, maxBytes)
    },
    [runId, messageId],
  )

  return useSealedArtifactWindow(`${runId}:${messageId}`, purpose, request)
}
