using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>
/// The one place the three Claude adapters translate an attempt's immutable model/effort request
/// into CLI arguments, so they cannot drift apart. A null model appends nothing (and, being an
/// invalid pairing, a non-null effort with it fails closed), preserving the exact pre-existing
/// argument list. A non-null model must be a member of the closed <see cref="ClaudeModelAlias"/>
/// set; a non-null effort must be a closed <see cref="ClaudeEffortLevel"/> paired with an explicit
/// <c>sonnet</c> or <c>opus</c> alias (<see cref="ClaudeModelRequest.IsValid"/>). Anything else
/// (malformed or legacy stored data) fails the invocation closed before any process starts rather
/// than reaching the command line. Each value is passed as one discrete argument, never through a
/// shell. The arguments are a request only: the provider may reject or adjust the effort.
/// </summary>
internal static class ClaudeModelRequestArguments
{
    public static bool TryAppend(List<string> arguments, string? requestedModel, string? requestedEffort)
    {
        if (!ClaudeModelRequest.IsValid(requestedModel, requestedEffort))
        {
            return false;
        }

        if (requestedModel is not null)
        {
            arguments.Add("--model");
            arguments.Add(requestedModel);
        }

        if (requestedEffort is not null)
        {
            arguments.Add("--effort");
            arguments.Add(requestedEffort);
        }

        return true;
    }
}
