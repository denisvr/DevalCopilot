namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>What a Codex claim handler carries from its early stop check to the durable claim: the setting exactly as the claim's
/// tracked Run loaded it (its stored text, which the commit seam re-compares; null when disabled) and, only when the stop is enabled,
/// the facts of the observation that permitted the claim. A disabled stop makes no observation at all.</summary>
public sealed record CodexAccountUsageClaimGuard(int? ThresholdPercent, string? StoredText, CodexAccountUsageGuardFacts? Facts);
