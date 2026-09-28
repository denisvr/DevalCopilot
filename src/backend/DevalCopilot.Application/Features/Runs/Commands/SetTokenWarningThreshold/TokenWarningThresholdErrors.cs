using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.SetTokenWarningThreshold;

public static class TokenWarningThresholdErrors
{
    public const string RunNotFoundCode = "runs.not_found";
    public const string RunNotEditableCode = "token_warning.run_not_editable";
    public const string ConcurrentChangeCode = "token_warning.concurrent_change";

    public static Error RunNotFound() => Error.NotFound(RunNotFoundCode, "This run does not exist.");

    public static Error RunNotEditable() =>
        Error.Failure(RunNotEditableCode, "This run's token warning thresholds can no longer be changed.");

    public static Error ConcurrentChange() =>
        Error.Conflict(ConcurrentChangeCode, "The run changed concurrently; retry the request.");
}
