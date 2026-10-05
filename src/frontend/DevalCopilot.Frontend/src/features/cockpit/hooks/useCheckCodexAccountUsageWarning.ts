import { useCallback, useState } from 'react'
import { getCodexAccountUsageWarningClient } from '../../../api/clients'
import { describeCodexAccountUsageWarningCheck, type CodexAccountUsageWarningCheckView } from '../describeCodexAccountUsageWarning'
import { useRunActionLifetime, type RunActionLifetimeApi } from './useRunActionLifetime'

export type CodexAccountUsageWarningCheckPhase =
  | { phase: 'idle' }
  | { phase: 'pending' }
  | { phase: 'failed' }
  | { phase: 'observed'; view: CodexAccountUsageWarningCheckView }

const IDLE: CodexAccountUsageWarningCheckPhase = { phase: 'idle' }
const PENDING: CodexAccountUsageWarningCheckPhase = { phase: 'pending' }
const FAILED: CodexAccountUsageWarningCheckPhase = { phase: 'failed' }

interface OwnedPhase {
  /** The one mounted lifetime that produced this phase; a different lifetime never sees it. */
  owner: RunActionLifetimeApi
  phase: CodexAccountUsageWarningCheckPhase
}

interface UseCheckCodexAccountUsageWarningResult {
  phase: CodexAccountUsageWarningCheckPhase
  /** Starts one explicit check. A no-op without a saved warning, while a check is in flight, or from an ended owner. */
  check: () => Promise<void>
}

/**
 * The explicit, advisory Codex account-usage warning check. Nothing starts a check except `check()`
 * (never mount, selection, a save or a cockpit refresh), and a check is the one provider-reading
 * request of this feature. The interaction and its last observation are owned by the committed
 * run plus `settingIdentity`, the identity of the authoritative saved warning: the lifetime ends
 * synchronously when either changes (or on unmount), returning to an earlier identity is a new
 * lifetime that starts with no observation, a handler retained from an ended lifetime is inert, a
 * completion that belongs to an ended lifetime is ignored (the server request itself still
 * happened), and a second activation while one check is in flight is ignored. While a check is
 * pending, or after it failed, no earlier classification is shown. A response is a classification
 * only for exactly the saved warning (`expectedPercent`) the caller shows.
 */
export function useCheckCodexAccountUsageWarning(
  runId: string,
  settingIdentity: string,
  expectedPercent: number | null,
): UseCheckCodexAccountUsageWarningResult {
  const ownerKey = JSON.stringify([runId, settingIdentity])
  const lifetime = useRunActionLifetime(ownerKey)
  const [owned, setOwned] = useState<OwnedPhase | null>(null)

  const check = useCallback(async () => {
    if (expectedPercent === null) {
      return
    }
    const ticket = lifetime.begin(ownerKey)
    if (!ticket) {
      return
    }
    setOwned({ owner: lifetime, phase: PENDING })
    try {
      const response = await getCodexAccountUsageWarningClient().getCodexAccountUsageWarning(runId)
      if (ticket.isCurrent()) {
        setOwned({
          owner: lifetime,
          phase: { phase: 'observed', view: describeCodexAccountUsageWarningCheck(response, expectedPercent) },
        })
      }
    } catch {
      if (ticket.isCurrent()) {
        setOwned({ owner: lifetime, phase: FAILED })
      }
    } finally {
      ticket.release()
    }
  }, [lifetime, ownerKey, runId, expectedPercent])

  return { phase: owned?.owner === lifetime ? owned.phase : IDLE, check }
}
