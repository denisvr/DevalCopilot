import type { GitHubStoredAuthenticationResponse } from '../../api/clients'

/** The closed states of the default stored GitHub CLI authentication of github.com (ADR-0038). */
export type GitHubStoredAuthenticationStatus = 'CliUnavailable' | 'Unknown' | 'NoActiveAccount' | 'Authenticated' | 'IssueReported'

/** A validated answer: the fixed host and context are implied, and a time exists only for an observed status. */
export interface GitHubStoredAuthentication {
  status: GitHubStoredAuthenticationStatus
  observedAtUtc: Date | null
}

const STATUSES: readonly string[] = ['CliUnavailable', 'Unknown', 'NoActiveAccount', 'Authenticated', 'IssueReported']
const OBSERVED: readonly string[] = ['NoActiveAccount', 'Authenticated', 'IssueReported']

export const GITHUB_HOST = 'github.com'
export const GITHUB_CONTEXT = 'StoredDefaultConfig'

/**
 * Accepts the whole answer or none of it: exactly the fixed host and context, a status of the closed set, and an observation
 * time that is a valid instant for NoActiveAccount, Authenticated and IssueReported and absent for CliUnavailable and Unknown.
 * Anything else is null (an incoherent answer), never a guessed state.
 */
export function toGitHubStoredAuthentication(response: GitHubStoredAuthenticationResponse | null | undefined): GitHubStoredAuthentication | null {
  if (!response || typeof response !== 'object') {
    return null
  }

  const { host, context, status, observedAtUtc } = response
  if (host !== GITHUB_HOST || context !== GITHUB_CONTEXT || typeof status !== 'string' || !STATUSES.includes(status)) {
    return null
  }

  const absent = observedAtUtc === null || observedAtUtc === undefined
  if (OBSERVED.includes(status)) {
    return observedAtUtc instanceof Date && !Number.isNaN(observedAtUtc.getTime())
      ? { status: status as GitHubStoredAuthenticationStatus, observedAtUtc }
      : null
  }

  return absent ? { status: status as GitHubStoredAuthenticationStatus, observedAtUtc: null } : null
}

/** Fixed plain-text statements; never provider text. */
export const STATUS_STATEMENTS: Record<GitHubStoredAuthenticationStatus, string> = {
  CliUnavailable: 'The GitHub CLI is not currently available as a vetted direct executable, so nothing was checked.',
  Unknown: 'Unknown: the stored authentication could not be determined.',
  NoActiveAccount: 'No active stored account was reported for github.com.',
  Authenticated: 'An active stored account is authenticated for github.com.',
  IssueReported: 'The GitHub CLI reports an authentication issue for the active stored account of github.com.',
}
