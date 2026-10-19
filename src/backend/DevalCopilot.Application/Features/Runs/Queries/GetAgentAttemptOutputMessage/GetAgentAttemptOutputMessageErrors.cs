using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptOutputMessage;

public static class GetAgentAttemptOutputMessageErrors
{
    public const string NotFoundCode = "agent_attempt_output_message.not_found";

    /// <summary>One fixed answer for an unknown run, a missing, foreign or non-Agent attempt and a message that is not a member of its
    /// proved output set, so none is distinguishable and no rejected identity is repeated.</summary>
    public static Error NotFound() =>
        Error.NotFound(NotFoundCode, "The requested Agent attempt output message was not found.");
}
