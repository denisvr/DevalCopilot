using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.RecordedLocalCommit;

/// <summary>
/// The joined historical entities of one run's recorded local-commit read, before the guarded scalar projection of
/// <see cref="RecordedLocalCommitProjection"/>. It is never materialized: the registered run and project are inner joined, and the
/// operation, its named workspace and its named checkpoint are left joins, so a member that does not exist in the database is the absence
/// of that row and the present-operation distinction survives a missing workspace or checkpoint. Initializer members, not a constructor,
/// so the query translator can bind each member to its joined entity.
/// </summary>
internal sealed class RecordedLocalCommitJoin
{
    public required Run Run { get; init; }

    public required Project Project { get; init; }

    public required LocalCommitOperation Operation { get; init; }

    public required GitWorkspace Workspace { get; init; }

    public required GitCheckpoint Checkpoint { get; init; }
}
