using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.SetClaudeModelPreference;

public static class ClaudeModelPreferenceErrors
{
    public const string RunNotFoundCode = "runs.not_found";
    public const string RunNotEditableCode = "claude_model.run_not_editable";
    public const string ConcurrentChangeCode = "claude_model.concurrent_change";

    public static Error RunNotFound() => Error.NotFound(RunNotFoundCode, "This run does not exist.");

    public static Error RunNotEditable() =>
        Error.Failure(RunNotEditableCode, "This run's Claude model request can no longer be changed.");

    public static Error ConcurrentChange() =>
        Error.Conflict(ConcurrentChangeCode, "The run's Claude model request changed concurrently; retry the request.");
}
