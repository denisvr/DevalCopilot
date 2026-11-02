import { STARTUP_WARNING_COPY } from '../../../api/startupDiagnostics'

/**
 * ADR-0072: the one app-shell summary of nonfatal startup warnings carried with readiness. It is informational only: a warning
 * creates no evidence, readiness or release, and the affected historical evidence stays unavailable.
 */
export function StartupWarningSummary({ warnings }: { warnings: readonly string[] | undefined }) {
  if (!warnings || warnings.length === 0) {
    return null
  }

  return (
    <section className="dc-startup-warnings" role="status" aria-label="Startup warnings">
      <p>The local host started, but some historical evidence could not be fully reconciled:</p>
      <ul>
        {warnings.map((warning) => (
          <li key={warning}>{STARTUP_WARNING_COPY[warning] ?? 'An unrecognized startup warning was reported.'}</li>
        ))}
      </ul>
      <p>Nothing was released or marked ready because of this.</p>
    </section>
  )
}
