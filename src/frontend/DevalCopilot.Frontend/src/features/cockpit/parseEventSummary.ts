import { summarizeAgentSupervisionEvent } from './agentSupervisionCopy'
import { PLANNING_SEQUENCE_REASON_CODES, PLANNING_SEQUENCE_STATES } from './planningSequence'
import type { PlanningSequenceReasonCode, PlanningSequenceState } from './planningSequence'
import {
  PLANNING_SEQUENCE_EVENT_SUMMARIES,
  PLANNING_SEQUENCE_REASON_PHRASE,
  describePlanningSequenceEventState,
} from './planningSequenceCopy'

export const RUN_ABANDONED_EVENT_TYPE = 'run.abandoned'
export const GITHUB_OBSERVATION_TARGET_EVENT_TYPE = 'run.github_observation_target_recorded'
export const GITHUB_REPOSITORY_IDENTITY_BOUND_EVENT_TYPE = 'run.github_repository_identity_bound'
export const GITHUB_BRANCH_PUBLICATION_APPROVED_EVENT_TYPE = 'run.github_branch_publication_approved'

const isText = (value: unknown): value is string => typeof value === 'string' && value.length > 0

const PLANNING_SEQUENCE_EVENT_PREFIX = 'planning_sequence.'

/**
 * The planning-sequence events (ADR-0055) are worded from their fixed event type only: no payload text is ever shown. The ended event
 * names its closed state and reason when both are members of the contract's vocabularies, and is the plain sentence otherwise.
 */
function summarizePlanningSequenceEvent(eventType: string, parsed: { state?: unknown; reason?: unknown }): string | null {
  const kind = eventType.slice(PLANNING_SEQUENCE_EVENT_PREFIX.length)
  if (!Object.hasOwn(PLANNING_SEQUENCE_EVENT_SUMMARIES, kind)) {
    return null
  }

  const summary = PLANNING_SEQUENCE_EVENT_SUMMARIES[kind as keyof typeof PLANNING_SEQUENCE_EVENT_SUMMARIES]
  if (
    kind === 'ended'
    && typeof parsed.state === 'string'
    && (PLANNING_SEQUENCE_STATES as readonly string[]).includes(parsed.state)
    && typeof parsed.reason === 'string'
    && (PLANNING_SEQUENCE_REASON_CODES as readonly string[]).includes(parsed.reason)
  ) {
    const state = describePlanningSequenceEventState(parsed.state as PlanningSequenceState)
    const reason = PLANNING_SEQUENCE_REASON_PHRASE[parsed.reason as PlanningSequenceReasonCode]
    return `The planning sequence ended: ${state}; ${reason}.`
  }

  return summary
}

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
      state?: unknown
      owner?: unknown
      repository?: unknown
      commitSha?: unknown
      repositoryNodeId?: unknown
      branchName?: unknown
      target?: unknown
      phase?: unknown
    }
    const supervisionSummary =
      eventType === undefined ? null : summarizeAgentSupervisionEvent(eventType, { reason: parsed.reason, phase: parsed.phase })
    if (supervisionSummary !== null) {
      return supervisionSummary
    }
    if (eventType?.startsWith(PLANNING_SEQUENCE_EVENT_PREFIX)) {
      const planningSummary = summarizePlanningSequenceEvent(eventType, parsed)
      if (planningSummary !== null) {
        return planningSummary
      }
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
    if (eventType === GITHUB_BRANCH_PUBLICATION_APPROVED_EVENT_TYPE && parsed.target && typeof parsed.target === 'object') {
      const { owner, repository } = parsed.target as { owner?: unknown; repository?: unknown }
      if (isText(owner) && isText(repository) && isText(parsed.branchName) && isText(parsed.commitSha)) {
        return `GitHub branch publication approved by the owner (recorded consent for one future push only; nothing was pushed): github.com/${owner}/${repository} branch ${parsed.branchName} at local commit ${parsed.commitSha}.`
      }
    }
    return parsed.summary ?? parsed.objective ?? payloadJson
  } catch {
    // A planning-sequence event is worded from its fixed type even when its payload cannot be read; the payload is never echoed.
    const planningSummary = eventType?.startsWith(PLANNING_SEQUENCE_EVENT_PREFIX) ? summarizePlanningSequenceEvent(eventType, {}) : null
    const supervisionSummary = eventType === undefined ? null : summarizeAgentSupervisionEvent(eventType, {})
    return planningSummary ?? supervisionSummary ?? payloadJson
  }
}
