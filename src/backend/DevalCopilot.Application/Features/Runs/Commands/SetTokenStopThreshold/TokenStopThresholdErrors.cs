using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.SetTokenStopThreshold;

public static class TokenStopThresholdErrors
{
    public const string RunNotFoundCode = "runs.not_found";
    public const string InvalidCode = "token_stop.invalid";
    public const string RunNotEditableCode = "token_stop.run_not_editable";
    public const string ConcurrentChangeCode = "token_stop.concurrent_change";

    public static Error RunNotFound() => Error.NotFound(RunNotFoundCode, "This run does not exist.");

    public static Error RunNotEditable() =>
        Error.Failure(RunNotEditableCode, "This run's token stop thresholds can no longer be changed.");

    public static Error ConcurrentChange() =>
        Error.Conflict(ConcurrentChangeCode, "The run changed concurrently; retry the request.");
}
