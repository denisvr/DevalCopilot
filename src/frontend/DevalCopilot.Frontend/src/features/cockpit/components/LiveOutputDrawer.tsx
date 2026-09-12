/**
 * The bounded live-output drawer shell. This increment's simulated adapter has no real
 * stdout/stderr to stream, so the drawer states its actual (empty) condition rather
 * than fabricating log lines.
 */
export function LiveOutputDrawer() {
  return (
    <div className="dc-live-output" aria-label="Live output">
      Live output: not yet collected for the simulated adapter.
    </div>
  )
}
