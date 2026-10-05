import type { GetGitCheckpointDiffResponse } from '../../../api/clients'

export interface CheckpointComparisonOmission {
  path: string
  reason: string
}

/** The host comparison of one inspected checkpoint (ADR-0027): never Git's raw patch. Every tracked changed path is either
 * in `text` or listed in `omissions`; `complete` means no tracked path was omitted. */
export interface CheckpointComparison {
  text: string
  complete: boolean
  trackedPathCount: number
  comparedPathCount: number
  limitation: string
  omissions: CheckpointComparisonOmission[]
}

/** Shown when the host answered with a body that does not state its coverage coherently. */
export const UNCONFIRMED_COMPARISON_MESSAGE = 'The host response could not be confirmed as a valid checkpoint comparison.'

const isCount = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0

/** Admits coverage only from explicit members with coherent accounting, or returns null (a missing member is never evidence of
 * zero work). Accepted only when every member is present, both counts are finite non-negative integers, compared + omitted equals
 * tracked, the completeness flag equals "nothing omitted", each omission names a distinct path and a reason, and the text is
 * empty exactly when no path was compared. The text itself is never parsed. */
export function toCheckpointComparison(response: GetGitCheckpointDiffResponse): CheckpointComparison | null {
  const { comparisonText, isComplete, trackedPathCount, comparedPathCount, omissions, limitation } = response
  if (
    typeof comparisonText !== 'string' ||
    typeof isComplete !== 'boolean' ||
    !isCount(trackedPathCount) ||
    !isCount(comparedPathCount) ||
    !Array.isArray(omissions)
  ) {
    return null
  }

  const admitted: CheckpointComparisonOmission[] = []
  for (const omission of omissions) {
    if (!omission || typeof omission.path !== 'string' || omission.path === '' || typeof omission.reason !== 'string' || omission.reason === '') {
      return null
    }

    admitted.push({ path: omission.path, reason: omission.reason })
  }

  if (
    new Set(admitted.map((omission) => omission.path)).size !== admitted.length ||
    comparedPathCount + admitted.length !== trackedPathCount ||
    isComplete !== (admitted.length === 0) ||
    (comparisonText === '') !== (comparedPathCount === 0)
  ) {
    return null
  }

  return {
    text: comparisonText,
    complete: isComplete,
    trackedPathCount,
    comparedPathCount,
    limitation: typeof limitation === 'string' ? limitation : '',
    omissions: admitted,
  }
}

const REASON_LABELS: Record<string, string> = {
  containment_unproven: 'not proven to be this file inside the owned workspace',
  not_attested: 'no attested content',
  attestation_incoherent: 'attested content was inconsistent',
  comparison_limit: 'comparison text limit reached',
  aggregate_limit: 'source size budget of this inspection reached',
  too_large: 'file too large',
  too_many_lines: 'too many lines',
  binary: 'binary content',
  invalid_utf8: 'not valid UTF-8 text',
  unsupported_status: 'change type not compared',
  unmerged: 'unmerged',
  unsupported_mode: 'file mode not compared',
  symbolic_link: 'symbolic link',
  submodule: 'submodule',
  not_regular_file: 'not a regular file',
  unreadable: 'could not be read',
  baseline_unavailable: 'committed content unavailable',
  baseline_unverified: 'committed content could not be verified',
  unencodable_path: 'path cannot be shown safely',
  no_content_difference: 'no content difference',
  reserved_instruction_file: 'reserved instruction file',
}

/** Plain text for a fixed omission reason; an unknown code is shown as received rather than hidden. */
export function omissionReasonLabel(reason: string): string {
  return REASON_LABELS[reason] ?? reason
}
