using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>
/// The left-joined historical entities of one admission-history row, before the guarded scalar projection of
/// <see cref="LocalExecutionAdmissionHistoryProjection"/>. It is only the shared input shape of the per-run ADR-0063 statement and the
/// global ADR-0064 statement: it is never materialized, and a member that does not exist in the database is the absence of that row.
/// Initializer members, not a constructor, so the query translator can bind each member to its joined entity.
/// </summary>
internal sealed class LocalExecutionAdmissionHistoryJoin
{
    public required Run Run { get; init; }

    public required LocalExecutionSequence Sequence { get; init; }

    public required LocalExecutionAdmission Admission { get; init; }

    public required LocalExecutionSequenceStep Step { get; init; }

    public required GitWorkspace Workspace { get; init; }

    public required GitCheckpoint Initial { get; init; }

    public required GitCheckpoint Source { get; init; }

    public required CollaborationMessage Report { get; init; }

    public required LocalExecutionSequenceRecipe Recipe { get; init; }
}
