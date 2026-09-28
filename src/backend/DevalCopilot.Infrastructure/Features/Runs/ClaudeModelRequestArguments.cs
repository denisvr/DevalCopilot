using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>
/// The one place the three Claude adapters translate an attempt's immutable model-alias request
/// into CLI arguments, so they cannot drift apart. A null request appends nothing, preserving the
/// exact pre-existing argument list. A non-null request must be a member of the closed
/// <see cref="ClaudeModelAlias"/> set; anything else (malformed or legacy stored data) fails the
/// invocation closed before any process starts rather than reaching the command line. The alias is
/// passed as one discrete argument, never through a shell.
/// </summary>
internal static class ClaudeModelRequestArguments
{
    public static bool TryAppend(List<string> arguments, string? requestedModel)
    {
        if (requestedModel is null)
        {
            return true;
        }

        if (!ClaudeModelAlias.IsSupported(requestedModel))
        {
            return false;
        }

        arguments.Add("--model");
        arguments.Add(requestedModel);
        return true;
    }
}
