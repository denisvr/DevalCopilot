using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetPlanningImplementationAuthorization;

/// <summary>
/// Reads one planning escalation's authorization facts from a single untracked snapshot. It performs no Git work and
/// writes nothing; <c>Available</c> therefore means "recorded, coherent, unconsumed, and bound to the checkpoint that is
/// current in the database", and the claim path still re-checks the live fingerprint and every other gate. An unknown or
/// foreign escalation is not found; unreadable or incoherent evidence is <c>Invalid</c> without echoing any of it.
/// </summary>
public sealed class GetPlanningImplementationAuthorizationQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetPlanningImplementationAuthorizationQuery, Result<PlanningImplementationAuthorizationQueryResult>>
{
    public async Task<Result<PlanningImplementationAuthorizationQueryResult>> HandleAsync(
        GetPlanningImplementationAuthorizationQuery query, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.AsNoTracking()
            .Where(candidate => candidate.Id == query.RunId)
            .Select(candidate => new { candidate.Id, candidate.ProjectId })
            .SingleOrDefaultAsync(cancellationToken);
        if (run is null)
        {
            return Result<PlanningImplementationAuthorizationQueryResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        var snapshot = await PlanningLineage.TryLoadSnapshotAsync(dbContext, run.Id, cancellationToken);
        if (snapshot is null)
        {
            return Success(query, PlanningImplementationAuthorizationState.Invalid);
        }

        if (!snapshot.MessagesById.TryGetValue(query.EscalationMessageId, out var escalationMessage)
            || escalationMessage.RunId != run.Id
            || escalationMessage.Type != CollaborationMessageType.Escalation)
        {
            return Result<PlanningImplementationAuthorizationQueryResult>.Failure(
                PlanningImplementationAuthorizationErrors.SourceNotFound());
        }

        var workspace = await dbContext.GitWorkspaces.AsNoTracking()
            .Where(candidate => candidate.ProjectId == run.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);
        var checkpoint = workspace is null
            ? null
            : await dbContext.GitCheckpoints.AsNoTracking()
                .Where(candidate => candidate.WorkspaceId == workspace.Id)
                .OrderByDescending(candidate => candidate.CheckpointNumber)
                .FirstOrDefaultAsync(cancellationToken);

        var grant = snapshot.PlanningAuthorizations.SingleOrDefault(candidate => candidate.EscalationMessageId == escalationMessage.Id);
        if (grant is not null)
        {
            return ForRecordedGrant(query, snapshot, grant, workspace?.Id, checkpoint);
        }

        if (workspace is null || checkpoint is null)
        {
            return Success(query, PlanningImplementationAuthorizationState.Stale);
        }

        var source = PlanningImplementationAuthorizationEvidence.EvaluateSource(
            snapshot, run.Id, workspace.Id, checkpoint.Id, checkpoint.FingerprintSha256, escalationMessage.Id);
        if (source.Final is not { } final)
        {
            return Success(
                query,
                source.Failure == PlanningImplementationAuthorizationEvidence.SourceFailure.Stale
                    ? PlanningImplementationAuthorizationState.Stale
                    : PlanningImplementationAuthorizationState.Invalid);
        }

        return Success(
            query,
            PlanningImplementationAuthorizationEvidence.IsSuperseded(snapshot, final)
                ? PlanningImplementationAuthorizationState.Stale
                : PlanningImplementationAuthorizationState.Absent,
            final.Proposal.Id,
            final.Decisions.Select(decision => decision.Id).ToArray());
    }

    private static Result<PlanningImplementationAuthorizationQueryResult> ForRecordedGrant(
        GetPlanningImplementationAuthorizationQuery query,
        ImplementerExecutionReportEligibility.Snapshot snapshot,
        PlanningImplementationAuthorization grant,
        Guid? currentWorkspaceId,
        GitCheckpoint? currentCheckpoint)
    {
        var recorded = PlanningImplementationAuthorizationEvidence.ValidateRecorded(snapshot, grant);
        if (recorded is null)
        {
            return Success(query, PlanningImplementationAuthorizationState.Invalid);
        }

        var state = !grant.IsAvailable
            ? PlanningImplementationAuthorizationState.Consumed
            : currentWorkspaceId is null
                || currentCheckpoint is null
                || !PlanningImplementationAuthorizationEvidence.IsBoundTo(
                    grant, currentWorkspaceId.Value, currentCheckpoint.Id, currentCheckpoint.FingerprintSha256)
                || PlanningImplementationAuthorizationEvidence.IsSuperseded(snapshot, recorded.Final)
                    ? PlanningImplementationAuthorizationState.Stale
                    : PlanningImplementationAuthorizationState.Available;

        return Result<PlanningImplementationAuthorizationQueryResult>.Success(new PlanningImplementationAuthorizationQueryResult(
            query.RunId,
            query.EscalationMessageId,
            state,
            grant.FinalProposalMessageId,
            recorded.Final.Decisions.Select(decision => decision.Id).ToArray(),
            grant.Id,
            grant.HumanInstructionMessageId,
            recorded.Rationale,
            grant.CreatedAtUtc,
            grant.ConsumedByAttemptId,
            grant.ConsumedAtUtc));
    }

    private static Result<PlanningImplementationAuthorizationQueryResult> Success(
        GetPlanningImplementationAuthorizationQuery query,
        PlanningImplementationAuthorizationState state,
        Guid? finalProposalMessageId = null,
        IReadOnlyList<Guid>? orderedDecisionMessageIds = null) =>
        Result<PlanningImplementationAuthorizationQueryResult>.Success(new PlanningImplementationAuthorizationQueryResult(
            query.RunId, query.EscalationMessageId, state, finalProposalMessageId, orderedDecisionMessageIds ?? [],
            null, null, null, null, null, null));
}
