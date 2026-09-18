using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;

/// <summary>
/// The authoritative last gate before the provider is ever invoked.
/// <c>GetEligibleAgentAttemptsQuery</c> is only a snapshot; workspace status, the mutation lease,
/// and the workspace's current checkpoint can all change during the pre-dispatch Git evidence
/// capture that runs between that snapshot and this call. This handler re-checks every one of
/// those facts in the same short transaction that records the dispatch marker, so nothing between
/// the snapshot and this commit can be dispatched on stale eligibility.
/// </summary>
public sealed class MarkAgentAttemptDispatchedCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<MarkAgentAttemptDispatchedCommand, Result<DateTimeOffset>>
{
    public const string WorkspaceNoLongerEligibleCode = "agent_attempts.workspace_no_longer_eligible";

    public async Task<Result<DateTimeOffset>> HandleAsync(
        MarkAgentAttemptDispatchedCommand command, CancellationToken cancellationToken)
    {
        var attempt = await dbContext.Attempts.SingleOrDefaultAsync(candidate => candidate.Id == command.AttemptId, cancellationToken);

        if (attempt is null || attempt.RunId != command.RunId)
        {
            return Result<DateTimeOffset>.Failure(
                Error.NotFound("attempts.not_found", "The requested attempt was not found for this run."));
        }

        if (attempt.Kind != AttemptKind.Agent)
        {
            return Result<DateTimeOffset>.Failure(
                Error.Conflict("attempts.not_agent", "The attempt is not an Agent attempt."));
        }

        if (attempt.Status != AttemptStatus.Running)
        {
            return Result<DateTimeOffset>.Failure(
                Error.Conflict("attempts.not_active", $"The attempt is {attempt.Status} and cannot be dispatched."));
        }

        if (attempt.AgentDispatchedAtUtc.HasValue)
        {
            return Result<DateTimeOffset>.Failure(
                Error.Conflict("attempts.already_dispatched", "The attempt was already dispatched."));
        }

        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == attempt.RunId, cancellationToken);
        if (run is null || run.Lifecycle != RunLifecycle.Running)
        {
            return WorkspaceNoLongerEligible();
        }

        var workspace = await dbContext.GitWorkspaces
            .SingleOrDefaultAsync(candidate => candidate.Id == attempt.AgentGitWorkspaceId, cancellationToken);
        if (workspace is null || workspace.Status != WorkspaceStatus.Ready)
        {
            return WorkspaceNoLongerEligible();
        }

        var leaseIsActive = await dbContext.RepositoryMutationLeases.AnyAsync(
            lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken);
        if (!leaseIsActive)
        {
            return WorkspaceNoLongerEligible();
        }

        var currentCheckpointId = await dbContext.GitCheckpoints
            .Where(checkpoint => checkpoint.WorkspaceId == workspace.Id)
            .OrderByDescending(checkpoint => checkpoint.CheckpointNumber)
            .Select(checkpoint => (Guid?)checkpoint.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (currentCheckpointId != attempt.AgentGitCheckpointId)
        {
            return WorkspaceNoLongerEligible();
        }

        var nowUtc = timeProvider.GetUtcNow();
        attempt.MarkAgentDispatched(nowUtc);

        return Result<DateTimeOffset>.Success(nowUtc);
    }

    private static Result<DateTimeOffset> WorkspaceNoLongerEligible() =>
        Result<DateTimeOffset>.Failure(
            Error.Conflict(
                WorkspaceNoLongerEligibleCode,
                "The workspace, its mutation lease, or its current checkpoint no longer permit dispatch."));
}
