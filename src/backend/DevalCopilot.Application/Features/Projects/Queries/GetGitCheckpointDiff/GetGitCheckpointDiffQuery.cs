using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Queries.GetGitCheckpointDiff;

/// <summary>Returns complete diff text only when a fresh capture still matches the requested
/// immutable checkpoint. The source text is never persisted by this query.</summary>
public sealed record GetGitCheckpointDiffQuery(Guid ProjectId, Guid CheckpointId)
    : IQuery<Result<GetGitCheckpointDiffQueryResult>>;

public sealed record GetGitCheckpointDiffQueryResult(string FingerprintSha256, string CompleteDiff);
