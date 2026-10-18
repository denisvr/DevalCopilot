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

    private static readonly AgentRole[] RolesByCode =
        [AgentRole.Planner, AgentRole.CriticalReviewer, AgentRole.Resolver, AgentRole.Implementer, AgentRole.CodeReviewer];

    private static readonly AgentProvider[] ProvidersByCode = [AgentProvider.Codex, AgentProvider.ClaudeCode];

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
            .ProjectScalarIdentity()
            .FirstOrDefaultAsync(cancellationToken);

        // The shared evaluation applies the unchanged assignment policy and identity rule; the attempt must also be exactly the actor.
        var identity = attempt is null ? null : ScalarAgentAttemptIdentity.Evaluate(attempt);
        if (identity is null
            || identity.Role != RolesByCode[source.ActorRoleCode - 1]
            || identity.Provider != ProvidersByCode[source.ActorProviderCode - 1])
        {
            return unavailable;
        }

        return (CollaborationAttemptStatus.Available, new CollaborationAttemptHeader(
            attempt!.Id,
            attempt.AttemptNumber,
            identity.Role,
            identity.Provider,
            identity.ResponseContract,
            identity.Status,
            attempt.ClaimedAtUtc,
            attempt.DispatchedAtUtc,
            attempt.CompletedAtUtc));
    }
}
