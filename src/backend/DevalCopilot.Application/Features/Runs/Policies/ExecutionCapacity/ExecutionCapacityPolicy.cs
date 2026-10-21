using System.Linq.Expressions;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;

/// <summary>
/// The one fresh, untracked reading of how many potentially mutating execution owners are recorded across one authoritative database
/// (ADR-0058), against the latest persisted limit revision (ADR-0059). Ownership is derived only from rows that already exist: an
/// unresolved Agent attempt whose coherent contract and role are WorkspaceMutating in Domain's <see cref="AgentAttemptContract"/>, an
/// unresolved verification execution (a configured command may mutate), and ADR-0029's local-commit reservation tied to its one open
/// operation. It reads no Git, provider, process table or AgentSupervision, adds no slot, lease or counter (the only table it reads
/// beside existing rows is the immutable limit configuration), and writes nothing.
///
/// <para>
/// Every predicate is a database-side exclusion of the recognized terminal set, so a stored status this build does not recognize is never
/// classified as finished. A recognized ReadOnly Agent attempt, a simulated or standalone Process attempt, an approval wait, an idle
/// prepared workspace and an observation row consume nothing, and effects come from Domain's table, never from a provider name. An
/// unresolved Agent attempt whose contract-role pair is missing, unrecognized or incoherent, an unknown attempt kind, an owner whose run,
/// project or workspace association cannot be attested, a reservation without its open operation (or an open operation without its
/// reservation), and two owners recorded on one workspace make the reading Unavailable instead of a convenient count. A run that ended, a
/// lease that changed or a workspace that is no longer Ready never drops an unresolved owner, and nothing here proves a process stopped.
/// </para>
///
/// <para>
/// A caller that is itself the owner under judgment (a dispatch or execution gate) names its exact kind and identifier. That owner must be
/// one of the counted coherent owners of its own kind, otherwise the reading is Unavailable; it is never excluded by the identifier of
/// another table, its run, its contract or its workspace. The reading still includes it, so the gate subtracts exactly one.
/// </para>
/// </summary>
internal static class ExecutionCapacityPolicy
{
    private static readonly Expression<Func<Attempt, bool>> MutatingPair = PairsOf(AgentEffectKind.WorkspaceMutating);
    private static readonly Expression<Func<Attempt, bool>> ReadOnlyPair = PairsOf(AgentEffectKind.ReadOnly);

    /// <summary>Whether the stored contract and role are exactly a coherent pair Domain classifies WorkspaceMutating.</summary>
    public static bool IsMutating(AgentResponseContract? contract, AgentRole? role) => Classify(contract, role) == AgentEffectKind.WorkspaceMutating;

    /// <summary>Whether the stored contract and role are exactly a coherent pair Domain classifies ReadOnly.</summary>
    public static bool IsReadOnly(AgentResponseContract? contract, AgentRole? role) => Classify(contract, role) == AgentEffectKind.ReadOnly;

    private static AgentEffectKind? Classify(AgentResponseContract? contract, AgentRole? role)
    {
        if (contract is not { } known || role is not { } knownRole || !Enum.IsDefined(known))
        {
            return null;
        }

        var row = AgentAttemptContract.For(known);
        return row.Role == knownRole ? row.Effect : null;
    }

    /// <summary>The reading across the whole database, optionally validating the caller's own owner (included in the count). The persisted
    /// limit is read first, fresh and untracked: a missing or malformed latest revision is Unavailable with no limit, and an ownership
    /// reading that cannot be trusted keeps the validated limit but states no count.</summary>
    public static async Task<ExecutionCapacityReading> ReadAsync(
        IDevalCopilotDbContext dbContext, ExecutionOwner? own, CancellationToken cancellationToken)
    {
        ExecutionCapacityConfiguration? configuration;
        try
        {
            configuration = await ReadConfigurationAsync(dbContext, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            configuration = null;
        }

        if (configuration is not { } current)
        {
            return new ExecutionCapacityReading(ExecutionCapacityStatus.Unavailable, null, null, null);
        }

        try
        {
            return await ReadCoreAsync(dbContext, own, current, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            return Unavailable(current);
        }
    }

    /// <summary>The fixed reason code each status carries on the wire (null: none).</summary>
    public static string? ReasonCodeOf(ExecutionCapacityStatus status) => status switch
    {
        ExecutionCapacityStatus.Available => null,
        ExecutionCapacityStatus.AtCapacity => "execution_capacity_limit_reached",
        _ => "execution_capacity_unavailable",
    };

    /// <summary>The latest configuration revision, read fresh and untracked, or null when none exists or the latest one is malformed. A
    /// malformed latest row is never skipped in favour of an older one. The revision is the table's integer row key, so only its range is
    /// judged; the limit is compared database-side with the two supported integers, so a real, text or blob value (which equals neither)
    /// is never coerced into a setting and is never materialized.</summary>
    internal static async Task<ExecutionCapacityConfiguration?> ReadConfigurationAsync(
        IDevalCopilotDbContext dbContext, CancellationToken cancellationToken)
    {
        var latest = await dbContext.ExecutionCapacityConfigurations.AsNoTracking()
            .OrderByDescending(configuration => configuration.Revision)
            .Select(configuration => new
            {
                Valid = configuration.Revision >= 1 && (configuration.Limit == 1 || configuration.Limit == 2),
                Revision = configuration.Revision >= 1 ? configuration.Revision : 0L,
                Limit = configuration.Limit == 1 ? 1 : configuration.Limit == 2 ? 2 : 0,
            })
            .FirstOrDefaultAsync(cancellationToken);
        return latest is { Valid: true } ? new ExecutionCapacityConfiguration(latest.Revision, latest.Limit) : null;
    }

    private static async Task<ExecutionCapacityReading> ReadCoreAsync(
        IDevalCopilotDbContext dbContext, ExecutionOwner? own, ExecutionCapacityConfiguration configuration, CancellationToken cancellationToken)
    {
        // ADR-0060: a linked operation whose row is missing, foreign or incoherent is never a zero count.
        if (await LocalExecutionOwnership.HasIncoherentLinkAsync(dbContext, null, cancellationToken))
        {
            return Unavailable(configuration);
        }

        var unresolvedAttempts = dbContext.Attempts.AsNoTracking().Where(LocalExecutionOwnership.UnresolvedAttempt(dbContext));
        if (await unresolvedAttempts.AnyAsync(
                attempt => attempt.Kind != AttemptKind.Agent && attempt.Kind != AttemptKind.Process && attempt.Kind != AttemptKind.Simulated,
                cancellationToken))
        {
            return Unavailable(configuration);
        }

        var unresolvedAgents = unresolvedAttempts.Where(attempt => attempt.Kind == AttemptKind.Agent);
        var mutating = unresolvedAgents.Where(MutatingPair);
        var mutatingCount = await mutating.CountAsync(cancellationToken);
        var readOnlyCount = await unresolvedAgents.Where(ReadOnlyPair).CountAsync(cancellationToken);
        // The two pair sets are disjoint (one row per response contract), so anything left over is an unrecognized or incoherent pair.
        if (await unresolvedAgents.CountAsync(cancellationToken) != mutatingCount + readOnlyCount)
        {
            return Unavailable(configuration);
        }

        var agentOwners = mutating.Where(attempt => attempt.AgentGitWorkspaceId != null
            && dbContext.Runs.Any(run => run.Id == attempt.RunId
                && dbContext.GitWorkspaces.Any(workspace => workspace.Id == attempt.AgentGitWorkspaceId && workspace.ProjectId == run.ProjectId)));
        var agentOwnerCount = await agentOwners.CountAsync(cancellationToken);
        if (agentOwnerCount != mutatingCount)
        {
            return Unavailable(configuration);
        }

        var unresolvedExecutions = dbContext.VerificationExecutions.AsNoTracking().Where(LocalExecutionOwnership.UnresolvedExecution(dbContext));
        var verificationOwners = unresolvedExecutions.Where(execution => dbContext.GitWorkspaces.Any(
            workspace => workspace.Id == execution.GitWorkspaceId && workspace.ProjectId == execution.ProjectId));
        var verificationOwnerCount = await verificationOwners.CountAsync(cancellationToken);
        if (verificationOwnerCount != await unresolvedExecutions.CountAsync(cancellationToken))
        {
            return Unavailable(configuration);
        }

        // ADR-0029's reservation exactly as CommitReservation reads it: a Committing workspace, or a NeedsAttention one while its own
        // open operation exists. Both the operation and the workspace must belong to the operation's project and run.
        var openOperations = dbContext.LocalCommitOperations.AsNoTracking().Where(operation =>
            operation.Status != LocalCommitStatus.Completed
            && operation.Status != LocalCommitStatus.Failed
            && operation.Status != LocalCommitStatus.Interrupted);
        var commitOwners = openOperations.Where(operation =>
            dbContext.Runs.Any(run => run.Id == operation.RunId && run.ProjectId == operation.ProjectId)
            && dbContext.GitWorkspaces.Any(workspace => workspace.Id == operation.GitWorkspaceId
                && workspace.ProjectId == operation.ProjectId
                && (workspace.Status == WorkspaceStatus.Committing || workspace.Status == WorkspaceStatus.NeedsAttention)));
        var commitOwnerCount = await commitOwners.CountAsync(cancellationToken);
        if (commitOwnerCount != await openOperations.CountAsync(cancellationToken)
            || await dbContext.GitWorkspaces.AsNoTracking().AnyAsync(
                workspace => workspace.Status == WorkspaceStatus.Committing
                    && !openOperations.Any(operation => operation.GitWorkspaceId == workspace.Id),
                cancellationToken))
        {
            return Unavailable(configuration);
        }

        // Two owners recorded on one workspace are untrustworthy for this reading, never one coalesced place.
        if (await agentOwners.GroupBy(attempt => attempt.AgentGitWorkspaceId).AnyAsync(group => group.Count() > 1, cancellationToken)
            || await verificationOwners.GroupBy(execution => execution.GitWorkspaceId).AnyAsync(group => group.Count() > 1, cancellationToken)
            || await commitOwners.GroupBy(operation => operation.GitWorkspaceId).AnyAsync(group => group.Count() > 1, cancellationToken)
            || await agentOwners.AnyAsync(
                attempt => verificationOwners.Any(execution => execution.GitWorkspaceId == attempt.AgentGitWorkspaceId), cancellationToken)
            || await agentOwners.AnyAsync(
                attempt => commitOwners.Any(operation => operation.GitWorkspaceId == attempt.AgentGitWorkspaceId), cancellationToken)
            || await verificationOwners.AnyAsync(
                execution => commitOwners.Any(operation => operation.GitWorkspaceId == execution.GitWorkspaceId), cancellationToken))
        {
            return Unavailable(configuration);
        }

        if (own is { } self && !await IsCountedAsync(self, agentOwners, verificationOwners, commitOwners, cancellationToken))
        {
            return Unavailable(configuration);
        }

        var total = (long)agentOwnerCount + verificationOwnerCount + commitOwnerCount;
        var owners = (int)Math.Min(total, int.MaxValue);
        return new ExecutionCapacityReading(
            total >= configuration.Limit ? ExecutionCapacityStatus.AtCapacity : ExecutionCapacityStatus.Available,
            owners,
            configuration.Limit,
            configuration.Revision);
    }

    private static Task<bool> IsCountedAsync(
        ExecutionOwner own,
        IQueryable<Attempt> agentOwners,
        IQueryable<VerificationExecution> verificationOwners,
        IQueryable<LocalCommitOperation> commitOwners,
        CancellationToken cancellationToken) => own.Kind switch
        {
            ExecutionOwnerKind.AgentAttempt => agentOwners.AnyAsync(attempt => attempt.Id == own.Id, cancellationToken),
            ExecutionOwnerKind.Verification => verificationOwners.AnyAsync(execution => execution.Id == own.Id, cancellationToken),
            ExecutionOwnerKind.LocalCommit => commitOwners.AnyAsync(operation => operation.Id == own.Id, cancellationToken),
            _ => Task.FromResult(false),
        };

    private static ExecutionCapacityReading Unavailable(ExecutionCapacityConfiguration configuration) =>
        new(ExecutionCapacityStatus.Unavailable, null, configuration.Limit, configuration.Revision);

    /// <summary>Exactly the coherent (response contract, role) pairs Domain's table gives the effect, as one database-side predicate.</summary>
    private static Expression<Func<Attempt, bool>> PairsOf(AgentEffectKind effect)
    {
        var attempt = Expression.Parameter(typeof(Attempt), "attempt");
        Expression body = Expression.Constant(false);
        foreach (var response in Enum.GetValues<AgentResponseContract>())
        {
            var row = AgentAttemptContract.For(response);
            if (row.Effect != effect)
            {
                continue;
            }

            var pair = Expression.AndAlso(
                Expression.Equal(
                    Expression.Property(attempt, nameof(Attempt.AgentResponseContract)),
                    Expression.Constant(response, typeof(AgentResponseContract?))),
                Expression.Equal(
                    Expression.Property(attempt, nameof(Attempt.AgentRole)),
                    Expression.Constant(row.Role, typeof(AgentRole?))));
            body = Expression.OrElse(body, pair);
        }

        return Expression.Lambda<Func<Attempt, bool>>(body, attempt);
    }
}
