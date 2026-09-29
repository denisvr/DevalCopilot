import { useState } from 'react'
import type { RunCockpitTokenStopResponse } from '../../../api/clients'
import { useSetTokenStopThreshold } from '../hooks/useSetTokenStopThreshold'

type StopProvider = 'Codex' | 'ClaudeCode'

const PROVIDER_LABEL: Record<StopProvider, string> = {
  Codex: 'Codex',
  ClaudeCode: 'Claude Code',
}

const nf = new Intl.NumberFormat('en-US')

/** Mirrors the backend's inclusive upper bound (Run.MaxTokenStopThreshold); the server remains authoritative. */
const MAX_THRESHOLD = 1_000_000_000_000

const SYNC_FAILURE_MESSAGE = 'Saved, but the cockpit could not be refreshed; the displayed stop state may be out of date.'

/** Exact meaning of each provider's count, shown so the number is never read as a cross-provider or cost figure. */
const FORMULA_LABEL: Record<StopProvider, string> = {
  Codex: 'input + output tokens (cached input is already part of input)',
  ClaudeCode: 'input + cache-creation + cache-read + output tokens',
}

interface ProviderStopProps {
  runId: string
  provider: StopProvider
  stop: RunCockpitTokenStopResponse | undefined
  onSaved?: () => Promise<boolean>
}

function gapText(stop: RunCockpitTokenStopResponse, provider: StopProvider): string {
  const parts: string[] = []
  if ((stop.pendingAttempts ?? 0) > 0) {
    parts.push(`${stop.pendingAttempts} still running`)
  }
  if ((stop.insufficientEvidenceAttempts ?? 0) > 0) {
    parts.push(
      provider === 'ClaudeCode'
        ? `${stop.insufficientEvidenceAttempts} without complete usage evidence (all four counts are required)`
        : `${stop.insufficientEvidenceAttempts} without usable usage evidence`,
    )
  }
  if ((stop.unattributedAttempts ?? 0) > 0) {
    parts.push(`${stop.unattributedAttempts} dispatched attempt(s) not attributable to a provider`)
  }
  if (stop.countOverflowed) {
    parts.push('the total is not representable')
  }
  return parts.join(', ')
}

function statusMessage(stop: RunCockpitTokenStopResponse | undefined, provider: StopProvider) {
  const label = PROVIDER_LABEL[provider]
  const known = stop?.countOverflowed ? 'an unrepresentable number of' : nf.format(stop?.knownTokenCount ?? 0)
  const threshold = stop?.thresholdTokens === undefined ? '' : nf.format(stop.thresholdTokens)
  const counted = stop?.countedAttempts ?? 0
  const gaps = stop ? gapText(stop, provider) : ''

  switch (stop?.state) {
    case 'ThresholdReached':
      return {
        kind: 'blocked' as const,
        text:
          `${label} token stop reached: ${known} reported tokens recorded across ${counted} concluded attempt(s), ` +
          `at or above the ${threshold} threshold. New ${label} attempts are refused until the threshold is raised or cleared; ` +
          'an attempt already claimed is unaffected.' +
          (gaps ? ` This is a lower bound; evidence gaps: ${gaps}.` : ''),
      }
    case 'EvidenceIndeterminate':
      return {
        kind: 'blocked' as const,
        text:
          `${label} token stop cannot be cleared: staying below the ${threshold} threshold cannot be proved because ` +
          `evidence is incomplete (${gaps}). New ${label} attempts are refused. ` +
          `${known} reported tokens are known so far; the real count may be higher.`,
      }
    case 'BelowThresholdComplete':
      return {
        kind: 'permits' as const,
        text:
          `${label}: ${known} reported tokens across ${counted} concluded attempt(s), below the ${threshold} threshold, ` +
          'with complete evidence. This stop does not refuse a new attempt; it does not show that the provider is available.',
      }
    case 'NoDispatchedHistory':
      return {
        kind: 'permits' as const,
        text:
          `${label}: stop set at ${threshold}. No dispatched attempts are recorded, so this stop does not refuse the first attempt ` +
          '(this is not a measured zero, and it does not show that the provider is available).',
      }
    default:
      return {
        kind: 'neutral' as const,
        text: `${label}: no token stop threshold set; a new ${label} attempt is not limited by a token stop.`,
      }
  }
}

function ProviderStop({ runId, provider, stop, onSaved }: ProviderStopProps) {
  const label = PROVIDER_LABEL[provider]
  const [saved, setSaved] = useState<number | null>(stop?.thresholdTokens ?? null)
  const [input, setInput] = useState(stop?.thresholdTokens === undefined ? '' : String(stop.thresholdTokens))
  const [localError, setLocalError] = useState<string | null>(null)
  const [syncFailed, setSyncFailed] = useState(false)
  const { saving, error, save } = useSetTokenStopThreshold()
  const status = statusMessage(stop, provider)

  const handleSave = async () => {
    const trimmed = input.trim()
    if (!/^[1-9][0-9]*$/.test(trimmed) || Number(trimmed) > MAX_THRESHOLD) {
      setLocalError(`Enter a whole number of tokens from 1 to ${nf.format(MAX_THRESHOLD)}.`)
      return
    }
    setLocalError(null)
    setSyncFailed(false)
    if (await save(runId, provider, Number(trimmed))) {
      setSaved(Number(trimmed))
      // The threshold endpoint emits no run event, so the authoritative cockpit is re-queried
      // explicitly; the stop shown above is always re-derived by the server from recorded evidence.
      if (onSaved && !(await onSaved())) {
        setSyncFailed(true)
      }
    }
  }

  const handleClear = async () => {
    setLocalError(null)
    setSyncFailed(false)
    if (await save(runId, provider, null)) {
      setSaved(null)
      setInput('')
      if (onSaved && !(await onSaved())) {
        setSyncFailed(true)
      }
    }
  }

  return (
    <div className={`dc-token-stop dc-token-stop-${status.kind}`} aria-label={`${label} token stop`}>
      <p className="dc-token-stop-status" role={status.kind === 'blocked' ? 'alert' : undefined}>
        {status.text}
      </p>
      <div className="dc-token-stop-controls">
        <label>
          {label} stop threshold (tokens)
          <input
            type="text"
            inputMode="numeric"
            value={input}
            onChange={(event) => setInput(event.target.value)}
            disabled={saving}
            aria-label={`${label} stop threshold`}
          />
        </label>
        <button type="button" onClick={() => void handleSave()} disabled={saving}>
          Save {label} stop
        </button>
        <button type="button" onClick={() => void handleClear()} disabled={saving || saved === null}>
          Clear {label} stop
        </button>
      </div>
      <p className="dc-token-stop-formula">Counts {FORMULA_LABEL[provider]} from locally recorded, concluded attempts.</p>
      {(localError ?? error) && <p className="dc-token-stop-error">{localError ?? error}</p>}
      {syncFailed && <p className="dc-token-stop-error">{SYNC_FAILURE_MESSAGE}</p>}
    </div>
  )
}

interface TokenStopPanelProps {
  runId: string
  tokenStops: RunCockpitTokenStopResponse[] | null | undefined
  /** Re-queries the authoritative cockpit after a successful save or clear; resolves false if that failed. */
  onSaved?: () => Promise<boolean>
}

/**
 * Per-provider, run-scoped token-activity stop. Unlike the advisory warning above it, a configured
 * stop refuses the next Agent attempt for that provider once the locally recorded, provider-reported
 * token activity reaches it (or when staying below it cannot be proved from the recorded evidence).
 * It is checked when an attempt is claimed, is not a provider account allowance, does not cap or
 * cancel an attempt already claimed, and reserves nothing. A below-threshold result never claims
 * that a provider invocation is possible. The two providers use different, provider-specific counts
 * and are never combined.
 */
export function TokenStopPanel({ runId, tokenStops, onSaved }: TokenStopPanelProps) {
  const byProvider = (provider: StopProvider) => tokenStops?.find((entry) => entry.provider === provider)
  const codex = byProvider('Codex')
  const claude = byProvider('ClaudeCode')

  return (
    <section className="dc-token-stops" aria-label="Token-activity stops">
      <h3>Token-activity stops (enforced at claim)</h3>
      <p className="dc-token-stops-note">
        A stop refuses the next attempt for that provider when locally recorded, provider-reported token activity has reached it. It is
        checked only when an attempt is claimed: it is not an account allowance, does not cap or cancel a claimed attempt, and does not
        show that a provider is available.
      </p>
      <ProviderStop
        key={`Codex:${codex?.thresholdTokens ?? ''}`}
        runId={runId}
        provider="Codex"
        stop={codex}
        onSaved={onSaved}
      />
      <ProviderStop
        key={`ClaudeCode:${claude?.thresholdTokens ?? ''}`}
        runId={runId}
        provider="ClaudeCode"
        stop={claude}
        onSaved={onSaved}
      />
    </section>
  )
}
