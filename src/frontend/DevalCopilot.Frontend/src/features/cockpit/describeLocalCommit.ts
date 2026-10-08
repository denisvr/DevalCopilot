import { ApiException } from '../../api/generated/api-client'

/** Fixed client-side copy for the explicit local commit (ADR-0029). Server text, Git output and the commit message are never echoed. */

export const LOCAL_COMMIT_MAX_MESSAGE_BYTES = 2048

export const LOCAL_COMMIT_UNKNOWN_OUTCOME_MESSAGE = 'The request outcome was unknown; the recorded status is shown.'
export const LOCAL_COMMIT_GENERIC_FAILURE = 'The local commit could not be requested.'

const REFUSAL_COPY: Readonly<Record<string, string>> = {
  run_not_eligible: 'Only a running manual Agent run can deliver a local commit.',
  workspace_not_ready: 'A ready, owned isolated workspace is required for a local commit.',
  lease_not_active: 'An active workspace lease is required for a local commit.',
  checkpoint_not_current: 'The approved checkpoint is no longer the workspace’s current checkpoint. Capture and approve a new checkpoint.',
  implementation_not_current: 'The implementation behind this checkpoint is missing or has been superseded.',
  agent_approval_missing: 'The Agent code review has not approved this checkpoint.',
  agent_approval_not_latest: 'A newer implementation or review exists after the approving Agent code review.',
  human_approval_missing: 'No human approval of this checkpoint was found.',
  human_decision_not_approved:
    'Every human decision for this checkpoint must be Approved. Mixed decisions cannot be resolved here; a new checkpoint is needed.',
  verification_not_current: 'Every enabled verification recipe needs a current passed execution for this checkpoint.',
  membership_mismatch: 'The verification membership of the executions, the Agent review and the human approval does not match.',
  active_work: 'An Agent attempt or verification execution is still active in this workspace.',
  parent_mismatch: 'The workspace head is not the expected recorded tip.',
  operation_conflict: 'This run already has a different local-commit operation or request. Reload the run to see the recorded operation.',
  operation_exists: 'This run already has a recorded local-commit operation.',
  authority_changed: 'The approval authority changed while the commit was being prepared. Review the current approvals and request it again.',
  'refused.conversion_refused':
    'A Git attribute or setting would convert the approved bytes (for example line-ending normalization), so the commit was refused rather than converted. Normalize the files in a new checkpoint.',
  'refused.unsafe_source': 'A changed file is not a safe regular file inside the owned workspace, so the commit was refused.',
  'refused.too_many_paths': 'The change touches more paths than a local commit allows.',
  'refused.source_too_large': 'A changed file is larger than a local commit allows.',
  'refused.total_too_large': 'The change is larger in total than a local commit allows.',
  'refused.host_unsupported': 'This host cannot prove the source bytes for a local commit.',
  'refused.identity_unavailable': 'A valid local Git author identity is not configured for this repository.',
  'refused.checkpoint_not_current': 'The workspace no longer matches the approved checkpoint.',
  'refused.source_changed': 'The workspace no longer matches the approved checkpoint.',
}

const REFUSED_GENERIC = 'The approved change cannot be committed by the host as configured.'
const UNKNOWN_REFUSAL = 'A local commit is not available for this run right now.'

const normalizeCode = (code: string) => (code.startsWith('local_commit.') ? code.slice('local_commit.'.length) : code)

/** Fixed explanation for a refusal code (status refusal or request refusal); unknown codes never echo server text. */
export function describeLocalCommitRefusal(code: string | null | undefined): string {
  if (!code) {
    return UNKNOWN_REFUSAL
  }
  const normalized = normalizeCode(code)
  return REFUSAL_COPY[normalized] ?? (normalized.startsWith('refused.') ? REFUSED_GENERIC : UNKNOWN_REFUSAL)
}

export type LocalCommitFailure = { kind: 'unknown' } | { kind: 'refused'; message: string }

function problemCode(caught: ApiException): string | null {
  try {
    const parsed = JSON.parse(caught.response) as { errors?: { code?: unknown }[] }
    const code = parsed.errors?.[0]?.code
    return typeof code === 'string' ? code : null
  } catch {
    return null
  }
}

/**
 * A failure with no HTTP response (network, timeout) or a 5xx is an unknown outcome: the server may have admitted the request.
 * Any other HTTP response is a definite refusal described by fixed copy chosen from the status and the stable problem code.
 */
export function classifyLocalCommitFailure(caught: unknown): LocalCommitFailure {
  if (!ApiException.isApiException(caught)) {
    return { kind: 'unknown' }
  }
  const exception = caught as ApiException
  if (exception.status >= 500 || exception.status < 100) {
    return { kind: 'unknown' }
  }
  const code = problemCode(exception)
  if (code && code.startsWith('local_commit.')) {
    return { kind: 'refused', message: describeLocalCommitRefusal(code) }
  }
  switch (exception.status) {
    case 400:
      return { kind: 'refused', message: 'The request or commit message was not accepted.' }
    case 404:
      return { kind: 'refused', message: 'This run could not be found.' }
    case 409:
      return { kind: 'refused', message: 'The run changed or does not allow a local commit now. Reload the run and retry.' }
    default:
      return { kind: 'refused', message: LOCAL_COMMIT_GENERIC_FAILURE }
  }
}

export type LocalCommitMessageProblem = 'empty' | 'carriage_return' | 'control_character' | 'too_long'

/** Client-side mirror of the host's message rules: trimmed, LF only, a non-empty subject, no control characters, at most 2048 UTF-8 bytes. */
export function validateLocalCommitMessage(draft: string): { valid: true; message: string } | { valid: false; problem: LocalCommitMessageProblem } {
  const message = draft.trim()
  if (message.length === 0 || message.split('\n', 1)[0].trim().length === 0) {
    return { valid: false, problem: 'empty' }
  }
  if (message.includes('\r')) {
    return { valid: false, problem: 'carriage_return' }
  }
  for (let index = 0; index < message.length; index += 1) {
    const code = message.charCodeAt(index)
    if ((code < 0x20 && code !== 0x0a) || code === 0x7f || (code >= 0x80 && code < 0xa0)) {
      return { valid: false, problem: 'control_character' }
    }
  }
  if (new TextEncoder().encode(message).length > LOCAL_COMMIT_MAX_MESSAGE_BYTES) {
    return { valid: false, problem: 'too_long' }
  }
  return { valid: true, message }
}

export const LOCAL_COMMIT_MESSAGE_PROBLEM_COPY: Readonly<Record<LocalCommitMessageProblem, string>> = {
  empty: 'Enter a commit message with a non-empty first line.',
  carriage_return: 'The commit message must use line feeds only; remove carriage returns.',
  control_character: 'The commit message must not contain control characters.',
  too_long: 'The commit message must be at most 2048 bytes.',
}

export const LOCAL_COMMIT_STATUS_COPY: Readonly<Record<string, string>> = {
  Prepared: 'Prepared. The request was admitted and the host has not yet created the commit.',
  Executing: 'Executing. The host is creating the local commit.',
  Completed: 'Completed. Local commit only — not pushed.',
  Failed: 'Failed. The host did not create the commit and the branch did not move.',
  Interrupted: 'Interrupted. The host stopped before recording a completed commit; nothing was retried.',
  NeedsAttention:
    'Needs attention. The host could not prove whether the commit happened, so the outcome is ambiguous and nothing was retried. Inspect the repository yourself.',
}

const OUTCOME_REASON_COPY: Readonly<Record<string, string>> = {
  already_terminal: 'The operation had already reached a final state.',
  git_unprovable: 'Git state could not be proven.',
  ownership_unprovable: 'Ownership of the workspace could not be proven.',
  head_not_bound: 'The workspace head was not bound to the recorded parent.',
  prepared_state_unexpected: 'The prepared state was not as recorded.',
  unpromoted_state_unknown: 'Whether the commit was promoted could not be determined.',
  branch_tip_unrecognized: 'The branch tip was not recognized.',
  unknown_index_lock: 'An unrecognized Git index lock was present.',
  index_state_unrecognized: 'The Git index state was not recognized.',
}

/** Fixed text for a recorded outcome reason code; unrecognized codes are not echoed. */
export function describeLocalCommitOutcomeReason(code: string | null | undefined): string | null {
  if (!code) {
    return null
  }
  const normalized = normalizeCode(code)
  return OUTCOME_REASON_COPY[normalized] ?? REFUSAL_COPY[normalized] ?? 'No further description is available for the recorded reason.'
}

/** The approval identity a local commit is bound to; all three ids come from the settled status, never from the timeline. */
export interface LocalCommitIdentity {
  checkpointId: string
  codeReviewAttemptId: string
  humanCheckpointReviewId: string
}

export const localCommitIdentityKey = (runId: string, identity: LocalCommitIdentity) =>
  [runId, identity.checkpointId, identity.codeReviewAttemptId, identity.humanCheckpointReviewId].join('|')
