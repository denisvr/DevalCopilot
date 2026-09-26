import { describeProviderTokenUsage, type RunCockpitProviderTokenUsageEntryView } from '../describeTokenUsage'

interface ProviderTokenUsageSummariesProps {
  entries: RunCockpitProviderTokenUsageEntryView[] | null | undefined
}

/**
 * Renders the cockpit's provider-separated token-usage projection: one line per provider bucket
 * (Codex, Claude Code, and any unattributed dispatched attempts), each following exactly the same
 * completeness and total-eligibility rules as the run-wide summary rendered beside it. This is
 * additive evidence only — it never replaces `RunTokenUsageSummary`, and an entry this cockpit build
 * does not recognize is still shown honestly rather than hidden.
 */
export function ProviderTokenUsageSummaries({ entries }: ProviderTokenUsageSummariesProps) {
  if (!entries || entries.length === 0) {
    return null
  }

  return (
    <ul className="dc-provider-token-usage" aria-label="Provider token usage">
      {entries.map((entry, index) => (
        <li
          key={entry.attribution ?? index}
          data-attribution={entry.attribution ?? 'Unknown'}
          data-completeness={entry.summary?.completeness ?? 'Unknown'}
        >
          {describeProviderTokenUsage(entry)}
        </li>
      ))}
    </ul>
  )
}
