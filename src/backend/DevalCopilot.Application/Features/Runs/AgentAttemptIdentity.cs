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
    public static bool IsCoherent(Attempt attempt) => IsCoherent(
        attempt.Kind, attempt.Status, attempt.AgentRole, attempt.AgentProvider, attempt.AgentResponseContract, attempt.GetAssignmentSnapshot());

    /// <summary>The same rule over already-read scalar facts, for a reader that judges a bounded projection without materializing an
    /// <see cref="Attempt"/>. A stored value that is not a defined member is passed as <see langword="null"/>, which fails exactly as an
    /// undefined member does. <paramref name="assignment"/> is the result of <see cref="AgentAssignmentSnapshotPolicy.Read"/> over the
    /// same stored facts (null when they are not a well-formed snapshot).</summary>
    public static bool IsCoherent(
        AttemptKind kind,
        AttemptStatus? status,
        AgentRole? role,
        AgentProvider? provider,
        AgentResponseContract? responseContract,
        AgentAssignmentSnapshot? assignment)
    {
        if (kind != AttemptKind.Agent
            || status is not { } recordedStatus
            || !Enum.IsDefined(recordedStatus)
            || role is not { } agentRole
            || provider is not { } agentProvider
            || responseContract is not { } contract
            || !Enum.IsDefined(agentRole)
            || !Enum.IsDefined(agentProvider)
            || !Enum.IsDefined(contract)
            || !IsSupportedPair(agentRole, agentProvider)
            || !ContractBelongsToRole(contract, agentRole))
        {
            return false;
        }

        return assignment is not null;
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
