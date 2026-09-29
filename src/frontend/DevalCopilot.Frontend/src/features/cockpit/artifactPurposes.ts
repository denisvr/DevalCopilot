import type { AgentAttemptArtifactMetadataResponse } from '../../api/clients'

export const ARTIFACT_PURPOSE_LABELS: Record<string, string> = {
  AgentContextManifest: 'Context manifest',
  AgentStandardOutput: 'Standard output',
  AgentStandardError: 'Standard error',
  AgentFinalResponse: 'Final response',
}

/** The purposes the artifact metadata actually offers, restricted to the closed allowlist. */
export function availableArtifactPurposes(artifacts: readonly AgentAttemptArtifactMetadataResponse[]): string[] {
  return artifacts
    .map((artifact) => artifact.purpose)
    .filter((purpose): purpose is string => Boolean(purpose) && purpose! in ARTIFACT_PURPOSE_LABELS)
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
