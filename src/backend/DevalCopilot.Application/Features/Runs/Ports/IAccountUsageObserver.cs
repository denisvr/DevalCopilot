namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// Observes, strictly, the account-usage snapshot a provider reports for one already-vetted local launch target, for the run-scoped
/// account-usage stop (ADR-0025). One bounded, read-only observation; it grants no resume, dispatch or mutating capability, reads no
/// credential, and never throws for an unavailable answer: every failure (an unsupported method, an error reply, a malformed, partial,
/// duplicated, oversized or truncated answer, a timeout, a process failure) is <see cref="AccountUsageObservation.Unavailable"/>; only
/// the caller's own cancellation is rethrown. Provider protocol details belong to the adapter in Infrastructure, never here.
/// Distinct from the display-only allowance port, which may drop a malformed window and show the rest: this port never keeps a subset.
/// Each call is its own observation; there is no cache, and no result is valid for another check or another launch tuple.
/// </summary>
public interface IAccountUsageObserver
{
    Task<AccountUsageObservation> ObserveAsync(string executablePath, string? scriptPath, CancellationToken cancellationToken);
}
