using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.FormatRepair;

/// <summary>
/// The final dispatch gate's protection for a CriticalReviewer, Resolver, or CodeReviewer repair
/// attempt: before any provider process could start, the repair's own link, its source, and both
/// input identities must still be one coherent fact. It deliberately does not re-apply the
/// pre-claim "source is the latest Agent attempt" and "source has no repair" tests — the committed
/// repair itself now owns that slot — and it leaves the ordinary duplicate-input classifications to
/// the caller. Every check reads fresh untracked state; a persisted row that cannot be materialized
/// is incoherent, while database and cancellation failures propagate.
/// </summary>
internal static class ReadOnlyFormatRepairLink
{
    public static bool AppliesTo(Attempt attempt) =>
        attempt.AgentRepairSourceAttemptId is not null
        && attempt.AgentResponseContract is { } contract
        && ReadOnlyFormatRepairPolicy.Supports(contract);

    /// <summary>Whether the repair attempt's persisted link is currently coherent.</summary>
    public static async Task<bool> IsCoherentAsync(
        IDevalCopilotDbContext dbContext, Attempt repair, CancellationToken cancellationToken)
    {
        if (repair.AgentRepairSourceAttemptId is not { } sourceAttemptId
            || repair.AgentResponseContract is not { } contract
            || sourceAttemptId == repair.Id
            || !ReadOnlyFormatRepairPolicy.HasExactTuple(repair, contract))
        {
            return false;
        }

        Attempt? source;
        try
        {
            source = await dbContext.Attempts
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == sourceAttemptId && candidate.RunId == repair.RunId, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        if (source is null
            || source.AttemptNumber >= repair.AttemptNumber
            || !ReadOnlyFormatRepairPolicy.IsEligibleSource(source, contract)
            || !AgentAttemptIdentity.IsCoherent(source)
            || source.AgentGitWorkspaceId != repair.AgentGitWorkspaceId
            || source.AgentGitCheckpointId != repair.AgentGitCheckpointId
            || !string.Equals(source.AgentCheckpointFingerprintSha256, repair.AgentCheckpointFingerprintSha256, StringComparison.Ordinal))
        {
            return false;
        }

        if (await dbContext.CollaborationMessages.AsNoTracking().AnyAsync(message => message.AttemptId == source.Id, cancellationToken))
        {
            return false;
        }

        var sourceInputs = await ReadOnlyFormatRepairInputs.ReadAsync(dbContext, source.Id, contract, cancellationToken);
        var repairInputs = await ReadOnlyFormatRepairInputs.ReadAsync(dbContext, repair.Id, contract, cancellationToken);
        return sourceInputs is not null && sourceInputs.Matches(repairInputs);
    }
}
