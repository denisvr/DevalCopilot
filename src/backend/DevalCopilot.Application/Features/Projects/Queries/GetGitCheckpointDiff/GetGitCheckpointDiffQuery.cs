using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Queries.GetGitCheckpointDiff;

/// <summary>Returns the bounded host comparison of attested tracked sources only when a fresh capture still matches the
/// requested immutable checkpoint (ADR-0027). It never returns Git's raw working-path patch, and the source text is never
/// persisted by this query.</summary>
public sealed record GetGitCheckpointDiffQuery(Guid ProjectId, Guid CheckpointId)
    : IQuery<Result<GetGitCheckpointDiffQueryResult>>;
