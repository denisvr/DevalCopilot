import { ApiException } from '../../../api/generated/api-client'
import type { CheckpointApprovalEvidenceResponse } from '../../../api/clients'

/** The host never offers (or accepts) more than this many members in one complete-set approval. */
export const MAXIMUM_APPROVAL_MEMBERS = 32

export interface ApprovalBundleMember {
  commandId: string
  commandNumber: number
  recipeLabel: string
  executionId: string
  executionNumber: number
}

/** The full source identity the panel observed: every fact the host's bundle must repeat exactly. */
export interface ApprovalBundleSource {
  projectId: string
  workspaceId: string
  checkpointId: string
  checkpointNumber: number
  fingerprintSha256: string
}

/** The complete verification set exactly as the host offered it, already proven consistent with the source it is for. `key` names
 * the source and every member identity, so two bundles are the same decision only when their keys are equal. */
export interface ApprovalBundle extends ApprovalBundleSource {
  members: readonly ApprovalBundleMember[]
  key: string
}

const isText = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
const isPositiveInteger = (value: unknown): value is number => typeof value === 'number' && Number.isInteger(value) && value > 0

/** The identity of the source a bundle is read and owned for, or null when any admitted fact is missing or malformed: the panel
 * never repairs or guesses one, so no bundle is read, offered or approved without the complete identity. */
export function toApprovalSource(
  projectId: string,
  evidence: { gitWorkspaceId?: string | null; checkpointId?: string | null; checkpointNumber?: number | null; fingerprintSha256?: string | null } | null | undefined,
): ApprovalBundleSource | null {
  if (
    !isText(projectId)
    || !evidence
    || !isText(evidence.gitWorkspaceId)
    || !isText(evidence.checkpointId)
    || !isPositiveInteger(evidence.checkpointNumber)
    || !isText(evidence.fingerprintSha256)
  ) {
    return null
  }

  return {
    projectId,
    workspaceId: evidence.gitWorkspaceId,
    checkpointId: evidence.checkpointId,
    checkpointNumber: evidence.checkpointNumber,
    fingerprintSha256: evidence.fingerprintSha256,
  }
}

/** The lifetime key of a source: every admitted identity fact, so a change of any one is a different source. */
export const approvalSourceKey = (source: ApprovalBundleSource): string =>
  JSON.stringify([source.projectId, source.workspaceId, source.checkpointId, source.checkpointNumber, source.fingerprintSha256])

/** Accepts a host response only when it is a coherent, bounded, strictly ordered bundle for exactly `source`: 1 to 32 members,
 * unique command and execution identities, ascending command numbers and the same project, workspace, checkpoint, checkpoint number
 * and fingerprint the caller is reading for. Anything else (a malformed body, a missing or inconsistent identity, a partial or
 * oversized set) is null, never repaired. */
export function toApprovalBundle(
  response: CheckpointApprovalEvidenceResponse | null | undefined,
  source: ApprovalBundleSource,
): ApprovalBundle | null {
  if (!response || typeof response !== 'object') {
    return null
  }

  if (
    response.projectId !== source.projectId
    || response.workspaceId !== source.workspaceId
    || response.checkpointId !== source.checkpointId
    || response.checkpointNumber !== source.checkpointNumber
    || response.fingerprintSha256 !== source.fingerprintSha256
    || !Array.isArray(response.members)
    || response.members.length < 1
    || response.members.length > MAXIMUM_APPROVAL_MEMBERS
  ) {
    return null
  }

  const members: ApprovalBundleMember[] = []
  for (const item of response.members) {
    if (
      !item
      || !isText(item.verificationCommandId)
      || !isText(item.verificationExecutionId)
      || !isText(item.recipeLabel)
      || !isPositiveInteger(item.commandNumber)
      || !isPositiveInteger(item.executionNumber)
    ) {
      return null
    }

    members.push({
      commandId: item.verificationCommandId,
      commandNumber: item.commandNumber,
      recipeLabel: item.recipeLabel,
      executionId: item.verificationExecutionId,
      executionNumber: item.executionNumber,
    })
  }

  const ordered = members.every((member, index) => index === 0 || member.commandNumber > members[index - 1].commandNumber)
  if (
    !ordered
    || new Set(members.map(member => member.commandId)).size !== members.length
    || new Set(members.map(member => member.executionId)).size !== members.length
  ) {
    return null
  }

  return {
    ...source,
    members,
    key: JSON.stringify([approvalSourceKey(source), members.map(member => [member.commandId, member.executionId])]),
  }
}

export type ApprovalBundleRefusal = 'none' | 'tooMany' | 'incomplete' | 'unavailable'

function errorCodeOf(caught: unknown): string | null {
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

/** Classifies a refused or failed bundle read by the host's fixed codes; everything else is a plain unavailable read. */
export function classifyBundleRefusal(caught: unknown): ApprovalBundleRefusal {
  switch (errorCodeOf(caught)) {
    case 'approval_evidence.no_enabled_recipes':
      return 'none'
    case 'approval_evidence.too_many_recipes':
      return 'tooMany'
    case 'approval_evidence.verification_incomplete':
      return 'incomplete'
    default:
      return 'unavailable'
  }
}

/** The fixed message for a refused complete-set approval; the host's own text is never displayed. */
export function describeApprovalFailure(caught: unknown): string {
  switch (errorCodeOf(caught)) {
    case 'reviews.approval_requires_complete_verification_set':
    case 'reviews.approval_requires_passed_verification':
      return 'The enabled checks changed since they were read. Use Refresh evidence, then approve the current set.'
    case 'reviews.checkpoint_not_current':
    case 'reviews.workspace_not_ready':
      return 'The source is no longer current or is busy. Use Refresh evidence before approving.'
    default:
      return 'The complete-set approval could not be recorded.'
  }
}
