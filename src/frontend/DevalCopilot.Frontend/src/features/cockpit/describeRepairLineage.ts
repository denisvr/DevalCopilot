/**
 * Provenance wording for an attempt that was requested as a format repair. It states only that the
 * attempt was a repair request and, when proved, for which earlier attempt — never that the repair
 * fixed, corrected, or preserved the source. Returns `null` when the link is unknown.
 */
export function describeRepairLineage(
  repairSourceAttemptId: string | null | undefined,
  repairSourceAttemptNumber: number | null | undefined,
): string | null {
  if (repairSourceAttemptNumber) {
    return `Repair request for attempt #${repairSourceAttemptNumber}`
  }

  return repairSourceAttemptId ? 'Repair request for an earlier attempt' : null
}
