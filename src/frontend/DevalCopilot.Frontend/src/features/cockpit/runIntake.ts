import { ApiException } from '../../api/generated/api-client'

/** The server's objective boundary; the client guard matches it and never trims what is sent. */
export const MAX_OBJECTIVE_LENGTH = 2000

/** The server's accepted range for the owner's immutable run-wide ceilings (ADR-0028), and the defaults used when untouched. */
export const MIN_AGENT_CLAIMS = 1
export const MAX_AGENT_CLAIMS = 16
export const MIN_INVOCATION_MINUTES = 1
export const MAX_INVOCATION_MINUTES = 120

/** One project's whole submission snapshot: the objective and both budget drafts, edited and versioned together. */
export interface RunIntakeDraft {
  objective: string
  maximumAgentAttempts: string
  maximumAgentInvocationMinutes: string
}

export const INITIAL_RUN_INTAKE_DRAFT: RunIntakeDraft = {
  objective: '',
  maximumAgentAttempts: String(MAX_AGENT_CLAIMS),
  maximumAgentInvocationMinutes: String(MAX_INVOCATION_MINUTES),
}

const WHOLE_NUMBER = /^[0-9]{1,9}$/

function parseWhole(draft: string, minimum: number, maximum: number): number | null {
  if (!WHOLE_NUMBER.test(draft)) {
    return null
  }
  const value = Number(draft)
  return value >= minimum && value <= maximum ? value : null
}

/** The chosen claim ceiling as a number, or null when the draft is not a whole number from 1 through 16. Never rounds or clamps. */
export function parseAgentClaimsDraft(draft: string): number | null {
  return parseWhole(draft, MIN_AGENT_CLAIMS, MAX_AGENT_CLAIMS)
}

/** The chosen reserved minutes as a number, or null when the draft is not a whole number from 1 through 120. */
export function parseInvocationMinutesDraft(draft: string): number | null {
  return parseWhole(draft, MIN_INVOCATION_MINUTES, MAX_INVOCATION_MINUTES)
}

export function validateAgentClaimsDraft(draft: string): string | null {
  if (draft.trim().length === 0) {
    return 'Enter a whole number of claims.'
  }
  return parseAgentClaimsDraft(draft) === null
    ? `The claim ceiling must be a whole number from ${MIN_AGENT_CLAIMS} through ${MAX_AGENT_CLAIMS}.`
    : null
}

export function validateInvocationMinutesDraft(draft: string): string | null {
  if (draft.trim().length === 0) {
    return 'Enter a whole number of minutes.'
  }
  return parseInvocationMinutesDraft(draft) === null
    ? `The reserved time must be a whole number of minutes from ${MIN_INVOCATION_MINUTES} through ${MAX_INVOCATION_MINUTES}.`
    : null
}

/** Null when the objective and both budget drafts are acceptable; otherwise the first fixed, safe message. */
export function validateRunIntakeDraft(draft: RunIntakeDraft): string | null {
  return (
    validateRunObjective(draft.objective) ??
    validateAgentClaimsDraft(draft.maximumAgentAttempts) ??
    validateInvocationMinutesDraft(draft.maximumAgentInvocationMinutes)
  )
}

export const RUN_INTAKE_BLOCKED_REASON ='A new objective can be recorded only after every run of this project has finished.'

/** Null when the objective is acceptable; otherwise a fixed, safe message. */
export function validateRunObjective(objective: string): string | null {
  if (objective.trim().length === 0) {
    return 'Enter an objective to record a run.'
  }
  if (objective.length > MAX_OBJECTIVE_LENGTH) {
    return `The objective must be at most ${MAX_OBJECTIVE_LENGTH} characters.`
  }
  return null
}

function failureCode(caught: unknown): string | null {
  if (!ApiException.isApiException(caught)) {
    return null
  }
  try {
    const parsed = JSON.parse((caught as ApiException).response) as { errors?: { code?: unknown }[] }
    const code = parsed.errors?.[0]?.code
    return typeof code === 'string' ? code : null
  } catch {
    return null
  }
}

export function isRunIntakeBlocked(caught: unknown): boolean {
  return failureCode(caught) === 'runs.intent_blocked'
}

/** Maps a failure to fixed, safe copy; raw server or exception text is never displayed. */
export function describeRunIntakeFailure(caught: unknown, fallback: string): string {
  const code = failureCode(caught)
  if (code === 'runs.intent_blocked') {
    return 'This project already has an unfinished run, so no new run was created. The project list is being refreshed.'
  }
  if (code === 'runs.intent_conflict') {
    return 'Another request for this project was being processed at the same time. No run was created; try again.'
  }
  if (code === 'projects.not_found') {
    return 'This project could not be found.'
  }
  if (ApiException.isApiException(caught) && (caught as ApiException).status === 400) {
    return `The run was not accepted. The objective must contain text and be at most ${MAX_OBJECTIVE_LENGTH} characters, the Agent claims must be a whole number from ${MIN_AGENT_CLAIMS} through ${MAX_AGENT_CLAIMS}, and the reserved minutes a whole number from ${MIN_INVOCATION_MINUTES} through ${MAX_INVOCATION_MINUTES}.`
  }
  return fallback
}
