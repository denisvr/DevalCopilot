using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptInputs;

public static class GetAgentAttemptInputsErrors
{
    public const string NotFoundCode = "agent_attempt_inputs.not_found";

    /// <summary>One fixed answer for an unknown run and for a missing, foreign or non-Agent attempt, so none is distinguishable.</summary>
    public static Error NotFound() =>
        Error.NotFound(NotFoundCode, "The requested Agent attempt was not found.");
}
