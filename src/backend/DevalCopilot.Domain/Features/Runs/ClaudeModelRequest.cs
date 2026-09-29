namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The one validity rule for an owner's requested Claude model/effort pair, shared by the Run,
/// the Attempt snapshot, and the adapters' fail-closed argument check so they cannot drift. No
/// model means no effort. A <c>haiku</c> request has no effort control under the currently
/// documented model support. An effort is accepted only with an explicitly requested
/// <c>sonnet</c> or <c>opus</c> alias. Validity is a request-syntax rule, never proof that the
/// signed-in account or model will honor the request.
/// </summary>
public static class ClaudeModelRequest
{
    public static bool IsValid(string? model, string? effort)
    {
        if (model is not null && !ClaudeModelAlias.IsSupported(model))
        {
            return false;
        }

        if (effort is null)
        {
            return true;
        }

        return ClaudeEffortLevel.IsSupported(effort)
            && (string.Equals(model, ClaudeModelAlias.Sonnet, StringComparison.Ordinal)
                || string.Equals(model, ClaudeModelAlias.Opus, StringComparison.Ordinal));
    }
}
