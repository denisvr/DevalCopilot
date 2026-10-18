import type { AgentAttemptArtifactMetadataResponse } from '../../api/clients'

export const ARTIFACT_PURPOSE_LABELS: Readonly<Record<string, string>> = {
  AgentContextManifest: 'Context manifest',
  AgentStandardOutput: 'Standard output',
  AgentStandardError: 'Standard error',
  AgentFinalResponse: 'Final response',
}

/**
 * The closed route segment of each allowlisted recorded purpose. Both sealed-window routes (the message-linked
 * `GetSealedAgentArtifactWindowEndpoint` and the attempt-history `GetAgentAttemptArtifactWindowEndpoint`) accept exactly these
 * segments and answer anything else, including the recorded purpose name itself, with a 404.
 */
const ARTIFACT_PURPOSE_ROUTE_SEGMENTS: Readonly<Record<string, string>> = {
  AgentContextManifest: 'context-manifest',
  AgentStandardOutput: 'stdout',
  AgentStandardError: 'stderr',
  AgentFinalResponse: 'final-response',
}

/** Whether a value names one of the four allowlisted purposes as an own key (never an inherited name such as `toString`). */
export function isAllowlistedArtifactPurpose(purpose: unknown): purpose is string {
  return typeof purpose === 'string' && Object.hasOwn(ARTIFACT_PURPOSE_LABELS, purpose)
}

/** The sealed-window route segment of an allowlisted recorded purpose, or null for any other value (which must start no request). */
export function artifactPurposeRouteSegment(purpose: string): string | null {
  return Object.hasOwn(ARTIFACT_PURPOSE_ROUTE_SEGMENTS, purpose) ? ARTIFACT_PURPOSE_ROUTE_SEGMENTS[purpose] : null
}

/** The distinct allowlisted purposes the artifact metadata actually names, in its order; anything else is never offered. */
export function availableArtifactPurposes(artifacts: readonly AgentAttemptArtifactMetadataResponse[]): string[] {
  const purposes: string[] = []
  for (const artifact of artifacts) {
    const purpose: unknown = artifact?.purpose
    if (isAllowlistedArtifactPurpose(purpose) && !purposes.includes(purpose)) {
      purposes.push(purpose)
    }
  }

  return purposes
}

/**
 * The two truthful caveats this viewer can make, matched to how each purpose is actually
 * produced and recorded (`Artifact.Sensitivity`, set at the point each purpose is written — see
 * `CreateCodexPlanningAttemptCommandHandler` and its sibling claim handlers for
 * `AgentContextManifest`'s `HostConstructedContent`, and `RecordAgentAttemptResultCommandHandler`
 * for the other three purposes' `RedactedBestEffort`). This branches on the already-selected,
 * closed-allowlist purpose string the component already holds — it is not a new sensitivity API
 * or wire field, and no schema changed to support it.
 */
export function sensitivityCaveatFor(purpose: string): string {
  if (purpose === 'AgentContextManifest') {
    return (
      "Historical text composed entirely by DevalCopilot's own application code from " +
      'already-curated durable fields — never raw, unexamined provider output. This reduces but ' +
      'does not eliminate the chance it names something sensitive (for example a file path or ' +
      'identifier from this run); it is not a live view of current source state.'
    )
  }

  return (
    'Historical raw provider text, passed through a fixed, documented best-effort redaction ' +
    'pattern list before capture — never a guarantee. An unrecognized or unusual secret format ' +
    'may still remain in it; it is not a live tail of a running attempt.'
  )
}
