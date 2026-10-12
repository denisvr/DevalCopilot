import { describe, expect, it } from 'vitest'
import type { ProjectGitHubOriginResponse } from '../../api/clients'
import { toProjectGitHubOrigin } from './projectGitHubOrigin'

const PROJECT = '11111111-2222-3333-4444-555555555555'
const AT = new Date('2026-10-12T09:00:00.000Z')

type Wire = Record<string, unknown>

const wire = (overrides: Wire = {}): ProjectGitHubOriginResponse =>
  ({
    projectId: PROJECT,
    source: 'RepositoryLocalConfig',
    status: 'Available',
    observedAtUtc: AT,
    target: { host: 'github.com', owner: 'acme', repository: 'widgets' },
    ...overrides,
  }) as unknown as ProjectGitHubOriginResponse

describe('toProjectGitHubOrigin', () => {
  it('accepts a whole Available answer with the fixed host, the observation time and a validated target', () => {
    expect(toProjectGitHubOrigin(wire(), PROJECT)).toEqual({
      projectId: PROJECT,
      status: 'Available',
      observedAtUtc: AT,
      target: { owner: 'acme', repository: 'widgets' },
    })
  })

  it('accepts NotConfigured with a time and no target', () => {
    expect(toProjectGitHubOrigin(wire({ status: 'NotConfigured', target: undefined }), PROJECT)).toEqual({
      projectId: PROJECT,
      status: 'NotConfigured',
      observedAtUtc: AT,
      target: null,
    })
  })

  it('accepts Unavailable with neither time nor target, and treats a wire null like an absent value', () => {
    const expected = { projectId: PROJECT, status: 'Unavailable', observedAtUtc: null, target: null }

    expect(toProjectGitHubOrigin(wire({ status: 'Unavailable', observedAtUtc: undefined, target: undefined }), PROJECT)).toEqual(expected)
    expect(toProjectGitHubOrigin(wire({ status: 'Unavailable', observedAtUtc: null, target: null }), PROJECT)).toEqual(expected)
  })

  it('compares the project identity without case sensitivity but never accepts another project', () => {
    expect(toProjectGitHubOrigin(wire({ projectId: PROJECT.toUpperCase() }), PROJECT)).not.toBeNull()
    expect(toProjectGitHubOrigin(wire({ projectId: '99999999-2222-3333-4444-555555555555' }), PROJECT)).toBeNull()
    expect(toProjectGitHubOrigin(wire({ projectId: undefined }), PROJECT)).toBeNull()
    expect(toProjectGitHubOrigin(wire({ projectId: 7 }), PROJECT)).toBeNull()
  })

  it.each([
    ['a different source', { source: 'Remote' }],
    ['a missing source', { source: undefined }],
    ['an unknown status', { status: 'Verified' }],
    ['a lowercase status', { status: 'available' }],
    ['a missing status', { status: undefined }],
    ['a numeric status', { status: 2 }],
    ['Available without a time', { observedAtUtc: undefined }],
    ['Available with an invalid time', { observedAtUtc: new Date('nope') }],
    ['Available with a string time', { observedAtUtc: '2026-10-12T09:00:00Z' }],
    ['Available without a target', { target: undefined }],
    ['Available with a null target', { target: null }],
    ['NotConfigured with a target', { status: 'NotConfigured' }],
    ['NotConfigured without a time', { status: 'NotConfigured', observedAtUtc: undefined, target: undefined }],
    ['Unavailable with a time', { status: 'Unavailable', target: undefined }],
    ['Unavailable with a target', { status: 'Unavailable', observedAtUtc: undefined }],
    ['another host', { target: { host: 'gitlab.com', owner: 'acme', repository: 'widgets' } }],
    ['a differently cased host', { target: { host: 'GitHub.com', owner: 'acme', repository: 'widgets' } }],
    ['a missing host', { target: { owner: 'acme', repository: 'widgets' } }],
    ['a missing owner', { target: { host: 'github.com', repository: 'widgets' } }],
    ['a missing repository', { target: { host: 'github.com', owner: 'acme' } }],
    ['an empty owner', { target: { host: 'github.com', owner: '', repository: 'widgets' } }],
    ['an empty repository', { target: { host: 'github.com', owner: 'acme', repository: '' } }],
    ['an owner with a leading hyphen', { target: { host: 'github.com', owner: '-acme', repository: 'widgets' } }],
    ['an owner with a trailing hyphen', { target: { host: 'github.com', owner: 'acme-', repository: 'widgets' } }],
    ['an owner with an underscore', { target: { host: 'github.com', owner: 'ac_me', repository: 'widgets' } }],
    ['an owner of 40 characters', { target: { host: 'github.com', owner: 'a'.repeat(40), repository: 'widgets' } }],
    ['a repository of 101 characters', { target: { host: 'github.com', owner: 'acme', repository: 'r'.repeat(101) } }],
    ['a repository with a leading dot', { target: { host: 'github.com', owner: 'acme', repository: '.widgets' } }],
    ['a repository with a slash', { target: { host: 'github.com', owner: 'acme', repository: 'wid/gets' } }],
    ['a repository with whitespace', { target: { host: 'github.com', owner: 'acme', repository: 'wid gets' } }],
    ['a non-ASCII owner', { target: { host: 'github.com', owner: 'acm\u00e9', repository: 'widgets' } }],
    ['a numeric owner', { target: { host: 'github.com', owner: 7, repository: 'widgets' } }],
    ['a target that is not an object', { target: 'github.com/acme/widgets' }],
  ])('refuses the whole answer for %s', (_label, overrides) => {
    expect(toProjectGitHubOrigin(wire(overrides), PROJECT)).toBeNull()
  })

  it('refuses a missing or non-object response', () => {
    expect(toProjectGitHubOrigin(null, PROJECT)).toBeNull()
    expect(toProjectGitHubOrigin(undefined, PROJECT)).toBeNull()
    expect(toProjectGitHubOrigin('Available' as unknown as ProjectGitHubOriginResponse, PROJECT)).toBeNull()
  })

  it('accepts the boundary lengths', () => {
    const boundary = wire({ target: { host: 'github.com', owner: 'a'.repeat(39), repository: `r${'x'.repeat(99)}` } })

    expect(toProjectGitHubOrigin(boundary, PROJECT)).not.toBeNull()
  })
})
