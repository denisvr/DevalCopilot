using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Errors;

/// <summary>Stable, non-echoing errors of the run-scoped Codex account-usage warning's set operation (ADR-0026). No error carries a
/// stored value, a provider payload, or an exception.</summary>
public static class CodexAccountUsageWarningErrors
{
    public const string RunNotFoundCode = "runs.not_found";
    public const string RunNotEditableCode = "codex_account_usage_warning.run_not_editable";
    public const string ConcurrentChangeCode = "codex_account_usage_warning.concurrent_change";

    public static Error RunNotFound() => Error.NotFound(RunNotFoundCode, "This run does not exist.");

    public static Error RunNotEditable() =>
        Error.Failure(RunNotEditableCode, "This run's Codex account-usage warning can no longer be changed.");

    public static Error ConcurrentChange() =>
        Error.Conflict(ConcurrentChangeCode, "The run changed concurrently; retry the request.");
}
