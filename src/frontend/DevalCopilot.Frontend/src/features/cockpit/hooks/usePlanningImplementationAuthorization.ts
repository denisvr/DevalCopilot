import { useCallback, useEffect, useRef, useState } from 'react'
import type { PlanningImplementationAuthorizationResponse } from '../../../api/clients'
import { planningImplementationAuthorizationClient } from '../../../api/clients'

export interface UsePlanningImplementationAuthorizationResult {
  /** The server's current authorization facts for this exact (run, escalation), or `null` while unread or after a failed read. */
  authorization: PlanningImplementationAuthorizationResponse | null
  loading: boolean
  /** A fixed message when the facts could not be read; never inferred from a request that merely succeeded. */
  error: string | null
  refresh: () => void
}

/**
 * The committed lifetimes of the current interaction. `key` names the (run, escalation) identity and `requestToken` the
 * request (identity, run sequence, refresh), but both can recur: A, then B, then A again with the same sequence. The ids
 * never recur. `identityId` changes whenever the identity changes, and `readId` with every new request, so a settled
 * record is tied to the very lifetime that asked for it and a return to earlier values awaits a fresh read.
 */
interface ReadLifetime {
  key: string | null
  requestToken: string | null
  identityId: number
  readId: number
}

/** The last settled read, tagged with the lifetimes it answered. */
interface SettledRead {
  identityId: number
  readId: number
  authorization: PlanningImplementationAuthorizationResponse | null
  error: string | null
}

const NO_LIFETIME: ReadLifetime = { key: null, requestToken: null, identityId: 0, readId: 0 }
const UNAVAILABLE_MESSAGE = 'The authorization status could not be read, so no decision is shown. Nothing is assumed from earlier actions.'

const keyOf = (runId: string | null, escalationMessageId: string | null) =>
  runId && escalationMessageId ? JSON.stringify([runId, escalationMessageId]) : null

/**
 * Reads the human implementation authorization facts of one planning escalation. The facts belong to the identity
 * lifetime that read them: a read for another identity is never shown, and returning to an earlier run or escalation
 * starts a new lifetime, so a settled record or error of an earlier interaction never authorizes the new one, not even
 * for one frame. Within one identity the shown facts persist while a newer read is pending (so an open form keeps its
 * state), a stale response of an earlier read is ignored, and a failed read replaces the facts with an honest
 * "unavailable" instead of keeping or inventing a state. `latestEventSequence` re-reads whenever the run advances;
 * `refresh` forces a read after a request. A read is "loading" until the response of exactly the current read has
 * settled, derived at render rather than set in the effect.
 */
export function usePlanningImplementationAuthorization(
  runId: string | null,
  escalationMessageId: string | null,
  latestEventSequence: number | undefined,
): UsePlanningImplementationAuthorizationResult {
  const [lifetime, setLifetime] = useState<ReadLifetime>(NO_LIFETIME)
  const [settled, setSettled] = useState<SettledRead | null>(null)
  const [refreshToken, setRefreshToken] = useState(0)
  const generationRef = useRef(0)
  const key = keyOf(runId, escalationMessageId)
  const requestToken = key === null ? null : JSON.stringify([key, latestEventSequence ?? null, refreshToken])

  // A new identity or request (including a return to earlier values) begins a new lifetime during render, before anything
  // is committed or read, so nothing settled under an earlier lifetime can be matched to it.
  const lifetimeIsCurrent = lifetime.key === key && lifetime.requestToken === requestToken
  if (!lifetimeIsCurrent) {
    setLifetime((previous) => ({
      key,
      requestToken,
      identityId: previous.key === key ? previous.identityId : previous.identityId + 1,
      readId: previous.readId + 1,
    }))
  }

  const refresh = useCallback(() => {
    setRefreshToken((token) => token + 1)
  }, [])

  const { identityId, readId } = lifetime
  useEffect(() => {
    const generation = ++generationRef.current
    if (!runId || !escalationMessageId || !requestToken || lifetime.requestToken !== requestToken) {
      return
    }

    planningImplementationAuthorizationClient()
      .getPlanningImplementationAuthorization(runId, escalationMessageId)
      .then((response) => {
        if (generationRef.current === generation) {
          setSettled({ identityId, readId, authorization: response, error: null })
        }
      })
      .catch(() => {
        if (generationRef.current === generation) {
          setSettled({ identityId, readId, authorization: null, error: UNAVAILABLE_MESSAGE })
        }
      })

    return () => {
      generationRef.current += 1
    }
  }, [runId, escalationMessageId, requestToken, lifetime.requestToken, identityId, readId])

  const belongsToCurrentIdentity = lifetime.key === key && settled !== null && settled.identityId === identityId
  const answeredCurrentRead = lifetimeIsCurrent && settled !== null && settled.readId === readId
  return {
    authorization: belongsToCurrentIdentity ? settled.authorization : null,
    error: belongsToCurrentIdentity ? settled.error : null,
    loading: requestToken !== null && !answeredCurrentRead,
    refresh,
  }
}
