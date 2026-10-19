import type { AgentSupervisionReading, AgentSupervisionStatusName } from './agentSupervision'
import type { AgentSupervisionReadFailure } from './hooks/useAgentSupervision'

// The fixed English wording of the current Agent supervision (ADR-0056). It describes what the host's own observer last recorded and
// never says that an Agent is working, running or making progress, never claims a live process, and offers no retry, resume, cancel or
// takeover: an observation is not permission to act, and attention does not show that earlier authorized work stopped.

/** The one sentence every non-empty reading carries. */
export const AGENT_SUPERVISION_DISCLAIMER =
  'Reported by the host’s own observer. It is not provider progress, a live process or permission to act.'

export const AGENT_SUPERVISION_NO_ACTION_NOTE =
  'This observation offers no retry, resume, cancel or takeover. Earlier authorized work may still finish later.'

export const AGENT_SUPERVISION_LOADING = 'Reading the host’s observation of the current Agent attempt…'

export const AGENT_SUPERVISION_READ_AGAIN = 'Read the host observation again'

/** How long without a newer answer before a displayed reading is called stale: three refresh intervals. */
export const AGENT_SUPERVISION_STALE_READING_SECONDS = 15

export const AGENT_SUPERVISION_STATUS_LABEL: Readonly<Record<AgentSupervisionStatusName, string>> = {
  NoAgentAttempt: 'No Agent attempt recorded',
  NotObserved: 'Not observed',
  Observed: 'Observed by the host',
  NeedsAttention: 'Needs attention',
  Ended: 'Attempt ended',
  Unavailable: 'Observation unavailable',
}

export const AGENT_SUPERVISION_FAILURE_TEXT: Readonly<Record<AgentSupervisionReadFailure, string>> = {
  read: 'The host observation could not be read. Nothing is shown as current.',
  timeout: 'The host did not answer within ten seconds. Nothing is shown as current.',
  'not-found': 'The host no longer knows this run. Nothing is shown as current.',
  invalid: 'The host answer was not coherent, so none of it is shown. Nothing is shown as current.',
}

export const AGENT_SUPERVISION_STALE_NOTE =
  'No newer answer from the host has arrived recently. This reading may be out of date; the browser never renews a heartbeat.'

export const AGENT_SUPERVISION_NOT_OBSERVED_NOTE =
  'No host observation is recorded for this attempt: it was never observed or it predates observations. This is not a heartbeat.'

/** The compact header wording: the observation, separately from the recorded lifecycle. `null` when there is nothing to say. */
export function describeSupervisionBadge(
  reading: AgentSupervisionReading | null,
  options: { stale: boolean; loading: boolean; failed: boolean },
): string | null {
  if (options.loading && reading === null) {
    return null
  }

  if (options.failed || reading === null) {
    return 'Host observation: unavailable'
  }

  if (options.stale && reading.status !== 'Ended' && reading.status !== 'NoAgentAttempt') {
    return 'Host observation: stale reading'
  }

  switch (reading.status) {
    case 'NoAgentAttempt':
      return null
    case 'NotObserved':
      return 'Host observation: none recorded'
    case 'Observed':
      return 'Host observation: reporting'
    case 'NeedsAttention':
      return 'Host observation: needs attention'
    case 'Ended':
      return 'Host observation: attempt ended'
    default:
      return 'Host observation: unavailable'
  }
}

/** The fixed phrase of each first-attention reason, for the timeline event (the same meaning as the region's text). */
export const AGENT_SUPERVISION_ATTENTION_PHRASE: Readonly<Record<string, string>> = {
  stale_heartbeat: 'the host observer had not reported for at least thirty seconds',
  phase_deadline_elapsed: 'the observed phase passed its absolute deadline',
  observer_ended_without_terminal_result: 'the host observer ended without a recorded terminal result',
  host_interrupted: 'the host that observed it is no longer running',
}

export const AGENT_SUPERVISION_CLOSURE_PHRASE: Readonly<Record<string, string>> = {
  attempt_terminal: 'the attempt reached its recorded terminal result',
  observer_ended: 'the observer ended without a recorded terminal result',
  host_interrupted: 'the host that observed it is no longer running',
}

/** The fixed phrase of each observation phase, for the phase-entry timeline event. Entering a phase never shows that a process started. */
export const AGENT_SUPERVISION_PHASE_PHRASE: Readonly<Record<string, string>> = {
  Preparing: 'preparing',
  AwaitingAdapterResult: 'awaiting the adapter result',
  RecordingResult: 'recording the result',
}

const NOT_PROGRESS = 'It is not provider progress, a live process or permission to act.'

const phraseOf = (phrases: Readonly<Record<string, string>>, value: unknown): string | null =>
  typeof value === 'string' && Object.hasOwn(phrases, value) ? phrases[value] : null

/**
 * The timeline wording of the Agent supervision events (registration, phase entry, first attention and closure), from their fixed event
 * type and a reason or phase that is a member of its closed vocabulary only: no other payload text is ever shown. Null for any other
 * event type.
 */
export function summarizeAgentSupervisionEvent(eventType: string, payload: { reason?: unknown; phase?: unknown }): string | null {
  const { reason, phase } = payload
  if (eventType === 'agent_supervision.registered') {
    return `The host started observing an Agent attempt. ${NOT_PROGRESS}`
  }

  if (eventType === 'agent_supervision.phase_entered') {
    const phrase = phraseOf(AGENT_SUPERVISION_PHASE_PHRASE, phase)
    return phrase === null
      ? `The host observation of an Agent attempt entered a later phase. ${NOT_PROGRESS}`
      : `The host observation of an Agent attempt entered a later phase: ${phrase}. ${NOT_PROGRESS}`
  }

  if (eventType === 'agent_supervision.attention') {
    const phrase = phraseOf(AGENT_SUPERVISION_ATTENTION_PHRASE, reason)
    return phrase === null
      ? 'The host observation of an Agent attempt recorded its first attention. It does not show whether earlier work stopped.'
      : `The host observation of an Agent attempt recorded its first attention: ${phrase}. It does not show whether earlier work stopped.`
  }

  if (eventType === 'agent_supervision.closed') {
    const phrase = phraseOf(AGENT_SUPERVISION_CLOSURE_PHRASE, reason)
    return phrase === null
      ? 'The host closed its observation of an Agent attempt. The attempt itself is unchanged.'
      : `The host closed its observation of an Agent attempt: ${phrase}. The attempt itself is unchanged.`
  }

  return null
}
