import type { ConnectionState } from '../types'

interface ConnectionBannerProps {
  state: ConnectionState
  // A transient synchronization failure (see useRunCockpit's `syncError`). When present it
  // always takes priority over the generic per-state message: it is strictly more specific
  // (the transport is up but the last catch-up failed) and never replaces or clears the
  // cockpit already on screen — only this banner changes.
  syncError?: string | null
}

const MESSAGES: Partial<Record<ConnectionState, string>> = {
  reconnecting: 'Reconnecting — catching up by event sequence before showing further updates.',
  disconnected: 'Disconnected. Showing the last durable state that was applied.',
}

/** Renders nothing while live and synchronized; a compact stale-state banner otherwise. */
export function ConnectionBanner({ state, syncError }: ConnectionBannerProps) {
  const message = syncError
    ? `Not synchronized: ${syncError}. Showing the last durable state that was applied.`
    : MESSAGES[state]
  if (!message) {
    return null
  }

  return (
    <div className="dc-connection-banner" role="status">
      {message}
    </div>
  )
}
