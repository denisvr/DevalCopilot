using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistoryAttempt;

public sealed class GetCollaborationHistoryAttemptQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetCollaborationHistoryAttemptQuery, Result<GetCollaborationHistoryAttemptQueryResult>>
{
    private const int ProviderObserved = 2;
    private const int AgentKind = 3;

    private static readonly AttemptStatus[] StatusesByCode =
        [AttemptStatus.Running, AttemptStatus.Completed, AttemptStatus.Failed, AttemptStatus.Interrupted];

    private static readonly AgentRole[] RolesByCode =
        [AgentRole.Planner, AgentRole.CriticalReviewer, AgentRole.Resolver, AgentRole.Implementer, AgentRole.CodeReviewer];

    private static readonly AgentProvider[] ProvidersByCode = [AgentProvider.Codex, AgentProvider.ClaudeCode];

    private static readonly AgentResponseContract[] ContractsByCode =
    [
        AgentResponseContract.Proposal,
        AgentResponseContract.CriticalReview,
        AgentResponseContract.ChallengeResolution,
        AgentResponseContract.ImplementationReport,
        AgentResponseContract.ImplementationReview,
        AgentResponseContract.ReviewCorrection,
        AgentResponseContract.VerificationDiagnosis,
    ];

    private static readonly AgentPermissionProfile[] ProfilesByCode =
        [AgentPermissionProfile.Unknown, AgentPermissionProfile.WorkspaceEditOnly, AgentPermissionProfile.ReadOnly];

    public async Task<Result<GetCollaborationHistoryAttemptQueryResult>> HandleAsync(
        GetCollaborationHistoryAttemptQuery query, CancellationToken cancellationToken)
    {
        if (!await dbContext.Runs.AsNoTracking().AnyAsync(run => run.Id == query.RunId, cancellationToken))
        {
            return Result<GetCollaborationHistoryAttemptQueryResult>.Failure(GetCollaborationHistoryAttemptErrors.NotFound());
        }

        // The source is read by its exact identity within the run: metadata and the attempt reference only, never its text.
        var source = await dbContext.CollaborationMessages.AsNoTracking()
            .Where(message => message.RunId == query.RunId && message.Id == query.MessageId)
            .ProjectSource()
            .FirstOrDefaultAsync(cancellationToken);
        if (source is null)
        {
            return Result<GetCollaborationHistoryAttemptQueryResult>.Failure(GetCollaborationHistoryAttemptErrors.NotFound());
        }

        if (source.Id == Guid.Empty || source.Sequence <= 0)
        {
            return Result<GetCollaborationHistoryAttemptQueryResult>.Failure(GetCollaborationHistoryAttemptErrors.Unavailable());
        }

        var (status, header) = await ResolveAsync(query.RunId, source, cancellationToken);
        return Result<GetCollaborationHistoryAttemptQueryResult>.Success(
            new GetCollaborationHistoryAttemptQueryResult(query.RunId, source.Id, source.Sequence, status, header));
    }

    private async Task<(CollaborationAttemptStatus Status, CollaborationAttemptHeader? Header)> ResolveAsync(
        Guid runId, CollaborationAttemptSource source, CancellationToken cancellationToken)
    {
        var unavailable = (CollaborationAttemptStatus.Unavailable, (CollaborationAttemptHeader?)null);
        if (!source.VersionKnown || !source.TypeKnown || source.ProvenanceCode == 0)
        {
            return unavailable;
        }

        // A recognized message that was not provider-observed attributes no Agent attempt under this contract (a simulated message
        // can still have a simulated attempt): no attempt row is resolved from its reference.
        if (source.ProvenanceCode != ProviderObserved)
        {
            return (CollaborationAttemptStatus.NoAgentAttempt, null);
        }

        if (source.ActorKindCode != AgentKind
            || source.ActorRoleCode is < 1 or > 5
            || source.ActorProviderCode is < 1 or > 2
            || source.AttemptId is not { } attemptId
            || attemptId == Guid.Empty)
        {
            return unavailable;
        }

        // At most one attempt, by the message's own stored identity, within this run. Nothing else is ever substituted.
        var attempt = await dbContext.Attempts.AsNoTracking()
            .Where(candidate => candidate.RunId == runId && candidate.Id == attemptId)
            .ProjectAttempt()
            .FirstOrDefaultAsync(cancellationToken);
        if (attempt is null || attempt.Id == Guid.Empty || attempt.AttemptNumber <= 0 || !attempt.IsAgent)
        {
            return unavailable;
        }

        var actorRole = RolesByCode[source.ActorRoleCode - 1];
        var actorProvider = ProvidersByCode[source.ActorProviderCode - 1];
        AttemptStatus? status = attempt.StatusCode is >= 1 and <= 4 ? StatusesByCode[attempt.StatusCode - 1] : null;
        AgentRole? role = attempt.RoleCode is >= 1 and <= 5 ? RolesByCode[attempt.RoleCode - 1] : null;
        AgentProvider? provider = attempt.ProviderCode is >= 1 and <= 2 ? ProvidersByCode[attempt.ProviderCode - 1] : null;
        AgentResponseContract? contract = attempt.ContractCode is >= 1 and <= 7 ? ContractsByCode[attempt.ContractCode - 1] : null;
        if (role != actorRole || provider != actorProvider || status is null || contract is null)
        {
            return unavailable;
        }

        // A rejected oversized storage is never read as a legitimate null: it is invalid by itself.
        if (attempt.RequestedModelOversized
            || attempt.ObservedModelOversized
            || attempt.RequestedEffortOversized
            || attempt.ObservedEffortOversized
            || attempt.AdapterContractVersionOversized)
        {
            return unavailable;
        }

        // A stored profile that is not a defined member is passed as such, so the unchanged policy rejects it.
        AgentPermissionProfile? profile = attempt.ProfileCode switch
        {
            0 => null,
            >= 1 and <= 3 => ProfilesByCode[attempt.ProfileCode - 1],
            _ => (AgentPermissionProfile)(-1),
        };
        var maxTurns = attempt.StoredMaxTurnsOversized
            ? ClaudeMutationTurnLimitReading.Malformed
            : ClaudeMutationTurnLimit.Read(attempt.StoredMaxTurns);
        var assignment = AgentAssignmentSnapshotPolicy.Read(
            AttemptKind.Agent,
            provider,
            attempt.RequestedModel,
            attempt.ObservedModel,
            attempt.RequestedEffort,
            attempt.ObservedEffort,
            profile,
            attempt.AdapterContractVersion,
            maxTurns);
        if (!AgentAttemptIdentity.IsCoherent(AttemptKind.Agent, status, role, provider, contract, assignment))
        {
            return unavailable;
        }

        return (CollaborationAttemptStatus.Available, new CollaborationAttemptHeader(
            attempt.Id,
            attempt.AttemptNumber,
            actorRole,
            actorProvider,
            contract.Value,
            status.Value,
            attempt.ClaimedAtUtc,
            attempt.DispatchedAtUtc,
            attempt.CompletedAtUtc));
    }
}
