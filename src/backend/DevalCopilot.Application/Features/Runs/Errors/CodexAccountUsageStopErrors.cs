using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Errors;

/// <summary>Stable, non-echoing errors of the run-scoped Codex account-usage stop's set operation. No error carries a stored value, a
/// provider payload, or an exception. The claim-time and dispatch-time refusals live with their gate
/// (<c>CodexAccountUsageStopGate</c>).</summary>
public static class CodexAccountUsageStopErrors
{
    public const string RunNotFoundCode = "runs.not_found";
    public const string RunNotEditableCode = "codex_account_usage_stop.run_not_editable";
    public const string ConcurrentChangeCode = "codex_account_usage_stop.concurrent_change";

    public static Error RunNotFound() => Error.NotFound(RunNotFoundCode, "This run does not exist.");

    public static Error RunNotEditable() =>
        Error.Failure(RunNotEditableCode, "This run's Codex account-usage stop can no longer be changed.");

    public static Error ConcurrentChange() =>
        Error.Conflict(ConcurrentChangeCode, "The run's Codex account-usage stop changed concurrently; retry the request.");
}
