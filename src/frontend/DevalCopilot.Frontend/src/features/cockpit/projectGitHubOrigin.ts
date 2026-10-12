import type { ProjectGitHubOriginResponse } from '../../api/clients'
import { isGitHubOwner, isGitHubRepository } from './gitHubRepositoryNames'

/** The closed states of the explicit repository-local GitHub origin observation (ADR-0039). */
export type ProjectGitHubOriginStatus = 'Unavailable' | 'NotConfigured' | 'Available'

export interface ProjectGitHubOriginTarget {
  owner: string
  repository: string
}

/** A validated answer: the fixed source and host are implied, a time exists for the two observed statuses and a target for Available alone. */
export interface ProjectGitHubOrigin {
  projectId: string
  status: ProjectGitHubOriginStatus
  observedAtUtc: Date | null
  target: ProjectGitHubOriginTarget | null
}

export const ORIGIN_SOURCE = 'RepositoryLocalConfig'
export const ORIGIN_HOST = 'github.com'

const isAbsent = (value: unknown) => value === null || value === undefined

const isValidDate = (value: unknown): value is Date => value instanceof Date && !Number.isNaN(value.getTime())

function toTarget(value: unknown): ProjectGitHubOriginTarget | null {
  if (!value || typeof value !== 'object') {
    return null
  }

  const { host, owner, repository } = value as Record<string, unknown>
  if (host !== ORIGIN_HOST || typeof owner !== 'string' || typeof repository !== 'string') {
    return null
  }

  return isGitHubOwner(owner) && isGitHubRepository(repository) ? { owner, repository } : null
}

/**
 * Accepts the whole answer or none of it, for exactly the observed project: the fixed source, a status of the closed set, a valid
 * observation instant for NotConfigured and Available and none for Unavailable, and a target of the fixed host with an owner and a
 * repository of the supported ASCII shape for Available and none otherwise. Anything else is null (an incoherent answer), never a
 * guessed state.
 */
export function toProjectGitHubOrigin(
  response: ProjectGitHubOriginResponse | null | undefined,
  expectedProjectId: string,
): ProjectGitHubOrigin | null {
  if (!response || typeof response !== 'object') {
    return null
  }

  const { projectId, source, status, observedAtUtc, target } = response
  if (typeof projectId !== 'string' || projectId.toLowerCase() !== expectedProjectId.toLowerCase() || source !== ORIGIN_SOURCE) {
    return null
  }

  switch (status) {
    case 'Unavailable':
      return isAbsent(observedAtUtc) && isAbsent(target) ? { projectId: expectedProjectId, status, observedAtUtc: null, target: null } : null
    case 'NotConfigured':
      return isValidDate(observedAtUtc) && isAbsent(target) ? { projectId: expectedProjectId, status, observedAtUtc, target: null } : null
    case 'Available': {
      const accepted = toTarget(target)
      return isValidDate(observedAtUtc) && accepted !== null ? { projectId: expectedProjectId, status, observedAtUtc, target: accepted } : null
    }
    default:
      return null
  }
}

/** Fixed plain-text statements; never provider or Git text. */
export const ORIGIN_STATEMENTS: Record<Exclude<ProjectGitHubOriginStatus, 'Available'>, string> = {
  Unavailable:
    "Unavailable: the repository-local origin could not be observed right now, so nothing is shown. Possible causes include a Git tool or project physical identity that is not currently verified, a repository layout that is not the project's own main repository, and a missing, duplicate or unsupported origin value; which one applies is not reported, and nothing was changed.",
  NotConfigured: "No origin URL is configured in this repository's own local Git configuration.",
}
