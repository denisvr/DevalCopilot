import { useState } from 'react'
import type { RunCockpitTokenWarningResponse } from '../../../api/clients'
import { useSetTokenWarningThreshold } from '../hooks/useSetTokenWarningThreshold'
import { useOwnedFlow } from '../hooks/useRunActionLifetime'

type WarningProvider = 'Codex' | 'ClaudeCode'

const PROVIDER_LABEL: Record<WarningProvider, string> = {
  Codex: 'Codex',
  ClaudeCode: 'Claude Code',
}

const nf = new Intl.NumberFormat('en-US')

/** Mirrors the backend's inclusive upper bound (Run.MaxTokenWarningThreshold); the server remains authoritative. */
const MAX_THRESHOLD = 1_000_000_000_000

const SYNC_FAILURE_MESSAGE = 'Saved, but the cockpit could not be refreshed; the displayed warning may be out of date.'

/** Exact meaning of each provider's count, shown so the number is never read as a cross-provider or cost figure. */
const FORMULA_LABEL: Record<WarningProvider, string> = {
  Codex: 'input + output tokens (cached input is already part of input)',
  ClaudeCode: 'input + cache-creation + cache-read + output tokens',
}

interface ProviderWarningProps {
  runId: string
  provider: WarningProvider
  warning: RunCockpitTokenWarningResponse | undefined
  onSaved?: () => Promise<boolean>
}

function gapText(warning: RunCockpitTokenWarningResponse, provider: WarningProvider): string {
  const parts: string[] = []
  if ((warning.pendingAttempts ?? 0) > 0) {
    parts.push(`${warning.pendingAttempts} still running`)
  }
  if ((warning.insufficientEvidenceAttempts ?? 0) > 0) {
    parts.push(
      provider === 'ClaudeCode'
        ? `${warning.insufficientEvidenceAttempts} without complete usage evidence (all four counts are required)`
        : `${warning.insufficientEvidenceAttempts} without usable usage evidence`,
    )
  }
  if ((warning.unattributedAttempts ?? 0) > 0) {
    parts.push(`${warning.unattributedAttempts} dispatched attempt(s) not attributable to a provider`)
  }
  return parts.join(', ')
}

function statusMessage(warning: RunCockpitTokenWarningResponse | undefined, provider: WarningProvider) {
  const label = PROVIDER_LABEL[provider]
  const known = nf.format(warning?.knownTokenCount ?? 0)
  const threshold = warning?.thresholdTokens === undefined ? '' : nf.format(warning.thresholdTokens)
  const counted = warning?.countedAttempts ?? 0
  const gaps = warning ? gapText(warning, provider) : ''

  switch (warning?.state) {
    case 'ThresholdReached':
      return {
        kind: 'alert' as const,
        text:
          `${label} token-activity warning: ${known} reported tokens recorded across ${counted} concluded attempt(s), ` +
          `at or above the ${threshold} threshold.` +
          (gaps ? ` This is a lower bound; evidence gaps: ${gaps}.` : ''),
      }
    case 'BelowThresholdComplete':
      return {
        kind: 'ok' as const,
        text:
          counted > 0 && (warning?.knownTokenCount ?? 0) === 0
            ? `${label}: known zero — 0 reported tokens across ${counted} concluded attempt(s); below the ${threshold} threshold.`
            : `${label}: ${known} reported tokens across ${counted} concluded attempt(s); below the ${threshold} threshold, with complete evidence.`,
      }
    case 'Indeterminate':
      return {
        kind: 'unknown' as const,
        text:
          `${label}: not an all-clear. ${known} reported tokens are known, below the ${threshold} threshold, ` +
          `but evidence is incomplete (${gaps}); the real count may be higher.`,
      }
    case 'NoEvidence':
      return {
        kind: 'neutral' as const,
        text: `${label}: no dispatched attempts recorded yet, so there is no evidence either way (this is not a known zero).`,
      }
    default:
      return {
        kind: 'neutral' as const,
        text: `${label}: no token-activity warning threshold set${counted > 0 || (warning?.knownTokenCount ?? 0) > 0 ? ` (${known} reported tokens recorded so far)` : ''}.`,
      }
  }
}

function ProviderWarning({ runId, provider, warning, onSaved }: ProviderWarningProps) {
  const label = PROVIDER_LABEL[provider]
  const identity = `${runId}|${provider}|${warning?.thresholdTokens ?? ''}`
  const [seenIdentity, setSeenIdentity] = useState(identity)
  const [saved, setSaved] = useState<number | null>(warning?.thresholdTokens ?? null)
  const [input, setInput] = useState(warning?.thresholdTokens === undefined ? '' : String(warning.thresholdTokens))
  const [localError, setLocalError] = useState<string | null>(null)
  const [syncFailed, setSyncFailed] = useState(false)
  const { saving, error, save } = useSetTokenWarningThreshold(runId)
  const beginFlow = useOwnedFlow(runId, identity)

  // A different run, provider, or authoritative threshold re-derives the saved value, input, and
  // messages during render, so nothing owned by the previous identity is shown or kept.
  if (seenIdentity !== identity) {
    setSeenIdentity(identity)
    setSaved(warning?.thresholdTokens ?? null)
    setInput(warning?.thresholdTokens === undefined ? '' : String(warning.thresholdTokens))
    setLocalError(null)
    setSyncFailed(false)
  }
  const status = statusMessage(warning, provider)

  const handleSave = async () => {
    const trimmed = input.trim()
    if (!/^[1-9][0-9]*$/.test(trimmed) || Number(trimmed) > MAX_THRESHOLD) {
      setLocalError(`Enter a whole number of tokens from 1 to ${nf.format(MAX_THRESHOLD)}.`)
      return
    }
    setLocalError(null)
    setSyncFailed(false)
    const owns = beginFlow()
    if ((await save(runId, provider, Number(trimmed))) && owns()) {
      setSaved(Number(trimmed))
      // The threshold endpoint emits no run event, so the authoritative cockpit is re-queried
      // explicitly; the warning shown above is always re-derived by the server from recorded evidence.
      if (onSaved && !(await onSaved()) && owns()) {
        setSyncFailed(true)
      }
    }
  }

  const handleClear = async () => {
    setLocalError(null)
    setSyncFailed(false)
    const owns = beginFlow()
    if ((await save(runId, provider, null)) && owns()) {
      setSaved(null)
      setInput('')
      if (onSaved && !(await onSaved()) && owns()) {
        setSyncFailed(true)
      }
    }
  }

  return (
    <div className={`dc-token-warning dc-token-warning-${status.kind}`} aria-label={`${label} token-activity warning`}>
      <p className="dc-token-warning-status" role={status.kind === 'alert' ? 'alert' : undefined}>
        {status.text}
      </p>
      <div className="dc-token-warning-controls">
        <label>
          {label} warning threshold (tokens)
          <input
            type="text"
            inputMode="numeric"
            value={input}
            onChange={(event) => setInput(event.target.value)}
            disabled={saving}
            aria-label={`${label} warning threshold`}
          />
        </label>
        <button type="button" onClick={() => void handleSave()} disabled={saving}>
          Save {label} threshold
        </button>
        <button type="button" onClick={() => void handleClear()} disabled={saving || saved === null}>
          Clear {label} threshold
        </button>
      </div>
      <p className="dc-token-warning-formula">Counts {FORMULA_LABEL[provider]} from locally recorded, concluded attempts.</p>
      {(localError ?? error) && <p className="dc-token-warning-error">{localError ?? error}</p>}
      {syncFailed && <p className="dc-token-warning-error">{SYNC_FAILURE_MESSAGE}</p>}
    </div>
  )
}

interface TokenWarningPanelProps {
  runId: string
  tokenWarnings: RunCockpitTokenWarningResponse[] | null | undefined
  /** Re-queries the authoritative cockpit after a successful save or clear; resolves false if that failed. */
  onSaved?: () => Promise<boolean>
}

/**
 * Per-provider, run-scoped token-activity warnings. Advisory only: each threshold warns about the
 * provider-reported token counts already recorded for concluded attempts and never limits, blocks,
 * or otherwise changes any claim or invocation. The two providers use different, provider-specific
 * counts and are never combined. A below-threshold result with missing, pending, or unattributed
 * evidence is shown as "not an all-clear", distinct from a complete below-threshold result and from
 * having no evidence at all.
 */
export function TokenWarningPanel({ runId, tokenWarnings, onSaved }: TokenWarningPanelProps) {
  const byProvider = (provider: WarningProvider) => tokenWarnings?.find((entry) => entry.provider === provider)
  const codex = byProvider('Codex')
  const claude = byProvider('ClaudeCode')

  return (
    <section className="dc-token-warnings" aria-label="Token-activity warnings">
      <h3>Token-activity warnings (advisory)</h3>
      <p className="dc-token-warnings-note">
        These warn about locally recorded, provider-reported token activity only. They do not limit or block any attempt and are not an
        account allowance or a cost.
      </p>
      <ProviderWarning
        key={`Codex:${codex?.thresholdTokens ?? ''}`}
        runId={runId}
        provider="Codex"
        warning={codex}
        onSaved={onSaved}
      />
      <ProviderWarning
        key={`ClaudeCode:${claude?.thresholdTokens ?? ''}`}
        runId={runId}
        provider="ClaudeCode"
        warning={claude}
        onSaved={onSaved}
      />
    </section>
  )
}
