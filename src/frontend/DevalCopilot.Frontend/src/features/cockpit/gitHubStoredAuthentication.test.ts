import { describe, expect, it } from 'vitest'
import type { GitHubStoredAuthenticationResponse } from '../../api/clients'
import { toGitHubStoredAuthentication } from './gitHubStoredAuthentication'

const AT = new Date('2026-10-12T09:00:00.000Z')

const response = (overrides: Record<string, unknown> = {}) =>
  ({ host: 'github.com', context: 'StoredDefaultConfig', status: 'Authenticated', observedAtUtc: AT, ...overrides }) as unknown as GitHubStoredAuthenticationResponse

describe('toGitHubStoredAuthentication', () => {
  it.each(['NoActiveAccount', 'Authenticated', 'IssueReported'])('accepts %s only with a valid observation time', (status) => {
    expect(toGitHubStoredAuthentication(response({ status }))).toEqual({ status, observedAtUtc: AT })
    expect(toGitHubStoredAuthentication(response({ status, observedAtUtc: undefined }))).toBeNull()
    expect(toGitHubStoredAuthentication(response({ status, observedAtUtc: null }))).toBeNull()
    expect(toGitHubStoredAuthentication(response({ status, observedAtUtc: new Date('nope') }))).toBeNull()
    expect(toGitHubStoredAuthentication(response({ status, observedAtUtc: '2026-10-12T09:00:00Z' }))).toBeNull()
  })

  it.each(['CliUnavailable', 'Unknown'])('accepts %s only without a time', (status) => {
    expect(toGitHubStoredAuthentication(response({ status, observedAtUtc: undefined }))).toEqual({ status, observedAtUtc: null })
    expect(toGitHubStoredAuthentication(response({ status, observedAtUtc: null }))).toEqual({ status, observedAtUtc: null })
    expect(toGitHubStoredAuthentication(response({ status, observedAtUtc: AT }))).toBeNull()
  })

  it('refuses a foreign host or context, an unknown or missing status, and a non-answer', () => {
    expect(toGitHubStoredAuthentication(response({ host: 'example.com' }))).toBeNull()
    expect(toGitHubStoredAuthentication(response({ host: 'GitHub.com' }))).toBeNull()
    expect(toGitHubStoredAuthentication(response({ host: undefined }))).toBeNull()
    expect(toGitHubStoredAuthentication(response({ context: 'AmbientEnvironment' }))).toBeNull()
    expect(toGitHubStoredAuthentication(response({ context: undefined }))).toBeNull()
    expect(toGitHubStoredAuthentication(response({ status: 'authenticated' }))).toBeNull()
    expect(toGitHubStoredAuthentication(response({ status: 'Ready' }))).toBeNull()
    expect(toGitHubStoredAuthentication(response({ status: undefined }))).toBeNull()
    expect(toGitHubStoredAuthentication(response({ status: 3 }))).toBeNull()
    expect(toGitHubStoredAuthentication(null)).toBeNull()
    expect(toGitHubStoredAuthentication(undefined)).toBeNull()
  })
})
