const OWNER = /^[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?$/
const REPOSITORY = /^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$/

/**
 * The conservative ASCII subset of GitHub names the host supports (ADR-0039, ADR-0040), mirrored from the host's one policy: an
 * owner is 1 to 39 letters, digits and hyphens that begins and ends alphanumeric, and a repository is 1 to 100 letters, digits,
 * dots, underscores and hyphens that begins alphanumeric. The match is exact: nothing is trimmed, decoded or normalized.
 */
export const isGitHubOwner = (value: unknown): value is string => typeof value === 'string' && OWNER.test(value)

export const isGitHubRepository = (value: unknown): value is string => typeof value === 'string' && REPOSITORY.test(value)
