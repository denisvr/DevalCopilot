using System.Collections.Immutable;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>The identities the production lineage recorded, captured as an immutable snapshot. The seed's execution membership is a
/// mutable list behind a read-only interface, so capture copies it into an immutable array (preserving order, absence and emptiness)
/// and every clone receives that immutable membership. Nothing mutable of the seed (list, host, observer, clock) is ever shared.</summary>
internal sealed class GitHubCheckContextsSealedIdentities
{
    private GitHubCheckContextsSealedIdentities(LocalCommitLineageIds ids, Guid operationId, string sha, Guid targetId)
    {
        Ids = ids;
        OperationId = operationId;
        Sha = sha;
        TargetId = targetId;
    }

    public LocalCommitLineageIds Ids { get; }

    public Guid OperationId { get; }

    public string Sha { get; }

    public Guid TargetId { get; }

    public static GitHubCheckContextsSealedIdentities Capture(LocalCommitLineageIds ids, Guid operationId, string sha, Guid targetId) =>
        new(Sever(ids), operationId, sha, targetId);

    /// <summary>The identities one clone's fact receives: the same scalars and an immutable copy of the membership.</summary>
    public LocalCommitLineageIds IdsForClone() => Sever(Ids);

    private static LocalCommitLineageIds Sever(LocalCommitLineageIds ids) =>
        ids with { AllExecutionIds = ids.AllExecutionIds?.ToImmutableArray() };
}
