import { useCallback, useLayoutEffect, useMemo, useRef } from 'react'

/** One accepted submission of one control. It is current only while its lifetime is. */
export interface RunActionTicket {
  /** False once the owning lifetime ended. An obsolete ticket must not write state, refresh,
   * clear a draft, or report success; its server request is unaffected. */
  isCurrent: () => boolean
  release: () => void
}

export interface RunActionLifetimeApi {
  /**
   * Claims `control` for a submission bound to `runId`. Returns null, without touching any other
   * request, when this lifetime is not active, `runId` is not this lifetime's run, or the same
   * control already has a submission in flight.
   */
  begin: (runId: string, control?: string) => RunActionTicket | null
  /** Captures THIS lifetime, for a continuation to re-check after each asynchronous boundary. */
  capture: () => () => boolean
  /** Whether this lifetime is the active, committed one right now. */
  isActive: () => boolean
}

export const DEFAULT_CONTROL = 'default'

/**
 * One mounted interaction lifetime of one run. Every render that binds a run gets its own
 * instance, so a handler retained from an earlier render keeps pointing at the lifetime that
 * committed it: once that lifetime ended it stays rejected, even after the control returns to
 * the same run (which is a new lifetime). The instance is inert until an effect activates it, so
 * an abandoned render never owns anything.
 */
class RunLifetime implements RunActionLifetimeApi {
  private active = false
  private readonly inFlight = new Set<string>()

  private readonly runId: string

  constructor(runId: string) {
    this.runId = runId
  }

  activate() {
    this.active = true
  }

  end() {
    this.active = false
  }

  isActive = () => this.active

  capture = () => () => this.active

  begin = (requestRunId: string, control: string = DEFAULT_CONTROL): RunActionTicket | null => {
    if (!this.active || this.runId !== requestRunId || this.inFlight.has(control)) {
      return null
    }
    this.inFlight.add(control)
    return {
      isCurrent: () => this.active,
      release: () => {
        this.inFlight.delete(control)
      },
    }
  }
}

/**
 * Owns the interaction lifetime of `runId` for one hook or component. The lifetime is activated
 * and ended in a layout effect (never during render), so it ends synchronously in the commit that
 * replaces it, before any later continuation can observe the new render, so StrictMode's extra mount/cleanup pair and
 * abandoned renders cannot leak or corrupt it. The returned API belongs to that lifetime only: it
 * changes identity with the run, so each committed render's handlers stay bound to their own.
 */
export function useRunActionLifetime(runId: string): RunActionLifetimeApi {
  const lifetime = useMemo(() => new RunLifetime(runId), [runId])

  useLayoutEffect(() => {
    lifetime.activate()
    return () => {
      lifetime.end()
    }
  }, [lifetime])

  return lifetime
}

/**
 * Operation ownership for a component's multi-step flow (save, refresh, then local updates).
 * `identity` is the committed identity that owns the component's local state (the run plus the
 * authoritative value or target the state is derived from): a change of either ends the flows
 * started under the old one, and returning to an earlier identity is a new one. `begin()` starts a
 * flow and returns its ownership check, which stays true only while that identity is current AND
 * no newer flow of the same control began, so an older flow's late continuation can never clear a
 * newer draft or write a stale warning. A handler bound to an ended identity cannot start a flow
 * and never supersedes the current one.
 */
export function useOwnedFlow(runId: string, identity: string): () => () => boolean {
  const lifetime = useRunActionLifetime(JSON.stringify([runId, identity]))
  const latest = useRef(0)

  return useCallback(() => {
    if (!lifetime.isActive()) {
      return () => false
    }
    const mine = ++latest.current
    return () => lifetime.isActive() && latest.current === mine
  }, [lifetime])
}
