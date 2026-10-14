export const RUN_ABANDONED_EVENT_TYPE = 'run.abandoned'
export const GITHUB_OBSERVATION_TARGET_EVENT_TYPE = 'run.github_observation_target_recorded'
export const GITHUB_REPOSITORY_IDENTITY_BOUND_EVENT_TYPE = 'run.github_repository_identity_bound'

const isText = (value: unknown): value is string => typeof value === 'string' && value.length > 0

/**
 * The durable event payload is bounded JSON; today it carries a plain-text summary or objective. The abandonment event carries only the
 * human reason, so it is worded from its event type: a reason is never taken from an arbitrary payload. The GitHub observation target event
 * is worded from its type and its own text members only, and says nothing of verification or authority.
 */
export function parseEventSummary(payloadJson: string, eventType?: string): string {
  try {
    const parsed = JSON.parse(payloadJson) as {
      summary?: string
      objective?: string
      reason?: unknown
      owner?: unknown
      repository?: unknown
      commitSha?: unknown
      repositoryNodeId?: unknown
    }
    if (eventType === RUN_ABANDONED_EVENT_TYPE && typeof parsed.reason === 'string') {
      return `Run abandoned by the owner. Reason: ${parsed.reason}`
    }
    if (
      eventType === GITHUB_OBSERVATION_TARGET_EVENT_TYPE
      && isText(parsed.owner)
      && isText(parsed.repository)
      && isText(parsed.commitSha)
    ) {
      return `GitHub observation target selected by the owner (local selection only): github.com/${parsed.owner}/${parsed.repository} for local commit ${parsed.commitSha}.`
    }
    if (
      eventType === GITHUB_REPOSITORY_IDENTITY_BOUND_EVENT_TYPE
      && isText(parsed.owner)
      && isText(parsed.repository)
      && isText(parsed.commitSha)
      && isText(parsed.repositoryNodeId)
    ) {
      return `GitHub repository identity bound by the owner (historical identity evidence only): github.com/${parsed.owner}/${parsed.repository} with repository ID ${parsed.repositoryNodeId} for local commit ${parsed.commitSha}.`
    }
    return parsed.summary ?? parsed.objective ?? payloadJson
  } catch {
    return payloadJson
  }
}
