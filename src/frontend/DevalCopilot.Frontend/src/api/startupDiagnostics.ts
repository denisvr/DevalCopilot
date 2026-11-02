/**
 * ADR-0072: the closed startup diagnosis the desktop shell may hand the WebView, validated again here against the same closed lists.
 * Only stage, reason and action names exist: never a path, message, exception or raw stderr text.
 */
export const STARTUP_FAILURE_STAGES: readonly string[] = [
  'StorageSchema',
  'LocalExecutionSequences',
  'ProcessAttemptOutput',
  'ProcessAttempts',
  'CapabilityCatalog',
  'CapabilityProbes',
  'LocalCommitOperations',
  'Workspaces',
  'VerificationOutput',
  'VerificationExecutions',
  'AgentAttemptOutput',
  'AgentAttempts',
  'ImplementationAttempts',
  'PlanningSequences',
  'AgentSupervisions',
]

export const STARTUP_FAILURE_REASONS: readonly string[] = ['StorageUnavailable', 'StateIncoherent', 'Unclassified']

export const STARTUP_WARNINGS: readonly string[] = [
  'OptionalOutputDeferred',
  'RedundantCleanupDeferred',
  'SupervisionObservationsUnclosed',
]

export interface StartupFailureView {
  readonly stage: string
  readonly reason: string
  readonly action: 'RestoreStorageAccess' | 'RequestReviewedRepair'
  /** The copyable safe diagnostic, identical to the one the host writes to stderr. */
  readonly code: string
}

const isRecord = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value)

/**
 * The shell's closed `startup-failed` error, or null for anything else (an ordinary exit, a timeout, a malformed or unknown value). An
 * action that does not follow from the reason is malformed, never trusted.
 */
export function toStartupFailure(error: unknown): StartupFailureView | null {
  if (!isRecord(error) || error.kind !== 'startup-failed' || Object.keys(error).length !== 4) {
    return null
  }

  const { stage, reason, action } = error
  if (typeof stage !== 'string' || typeof reason !== 'string' || typeof action !== 'string') {
    return null
  }

  const expectedAction = reason === 'StorageUnavailable' ? 'RestoreStorageAccess' : 'RequestReviewedRepair'
  if (!STARTUP_FAILURE_STAGES.includes(stage) || !STARTUP_FAILURE_REASONS.includes(reason) || action !== expectedAction) {
    return null
  }

  return { stage, reason, action: expectedAction, code: `DCSF1-${stage}-${reason}-${expectedAction}` }
}

/** The closed startup warnings of a ready session, or null when the value is malformed. */
export function toStartupWarnings(value: unknown): readonly string[] | null {
  if (value === undefined || value === null) {
    return []
  }

  if (!Array.isArray(value) || value.some((item) => typeof item !== 'string' || !STARTUP_WARNINGS.includes(item))) {
    return null
  }

  return new Set(value).size === value.length ? (value as string[]) : null
}

export const STARTUP_ACTION_GUIDANCE: Readonly<Record<StartupFailureView['action'], string>> = {
  RestoreStorageAccess:
    'The local data could not be opened or written. Restore ordinary access to the DevalCopilot application data folder and free disk space, then reopen the application once. If the same diagnostic appears again, the stored data needs a reviewed repair.',
  RequestReviewedRepair:
    'The stored state could not be reconciled safely, so nothing was started. Keep the application data and every project worktree exactly as they are, copy the diagnostic below and request a reviewed repair.',
}

export const STARTUP_FAILURE_LIMITS =
  'Do not edit or delete the database, reset or remove worktrees, force-release ownership, or restart repeatedly hoping it clears. DevalCopilot does not repair this itself.'

export const STARTUP_WARNING_COPY: Readonly<Record<string, string>> = {
  OptionalOutputDeferred: 'Some historical output of an interrupted attempt could not be imported; its partial file was kept and it is not evidence.',
  RedundantCleanupDeferred: 'A partial output file that is already recorded could not be removed; it was left in place.',
  SupervisionObservationsUnclosed: 'Some supervision observations from a previous launch could not be closed; they remain as recorded.',
}
