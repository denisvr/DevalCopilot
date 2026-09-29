using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// The one read-side rule for whether a persisted Agent attempt's identity is trustworthy enough to
/// disclose its history row or sealed evidence: the attempt is Agent-kind, its status, role,
/// provider, and response contract are defined, the role/provider pair is one the product actually
/// launches, the response contract belongs to that role, and the immutable assignment snapshot is
/// well-formed. A corrupted or legacy row fails closed instead of being presented as evidence for a
/// role or provider it cannot prove. This is provenance validation only — never a claim about
/// provider capability or current workflow authority.
/// </summary>
public static class AgentAttemptIdentity
{
    public static bool IsCoherent(Attempt attempt)
    {
        if (attempt.Kind != AttemptKind.Agent
            || !Enum.IsDefined(attempt.Status)
            || attempt.AgentRole is not { } role
            || attempt.AgentProvider is not { } provider
            || attempt.AgentResponseContract is not { } responseContract
            || !Enum.IsDefined(role)
            || !Enum.IsDefined(provider)
            || !Enum.IsDefined(responseContract)
            || !IsSupportedPair(role, provider)
            || !ContractBelongsToRole(responseContract, role))
        {
            return false;
        }

        return attempt.GetAssignmentSnapshot() is not null;
    }

    private static bool IsSupportedPair(AgentRole role, AgentProvider provider) => (role, provider) switch
    {
        (AgentRole.Planner, AgentProvider.Codex) => true,
        (AgentRole.Resolver, AgentProvider.Codex) => true,
        (AgentRole.CodeReviewer, AgentProvider.Codex) => true,
        (AgentRole.CriticalReviewer, AgentProvider.ClaudeCode) => true,
        (AgentRole.Implementer, AgentProvider.ClaudeCode) => true,
        _ => false,
    };

    private static bool ContractBelongsToRole(AgentResponseContract responseContract, AgentRole role)
    {
        try
        {
            return AgentAttemptContract.For(responseContract).Role == role;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
