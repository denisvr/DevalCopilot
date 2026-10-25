using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionOwnershipReleases;

/// <summary>
/// The left-joined historical entities of one release-candidate row (ADR-0065), before the guarded scalar projection of
/// <see cref="LocalExecutionReleaseProjection"/>. It is only the input shape of the one statement and is never materialized: a member that
/// does not exist in the database is the absence of that row. Initializer members, not a constructor, so the query translator binds each
/// member to its joined entity.
/// </summary>
internal sealed class LocalExecutionReleaseJoin
{
    public required Run Run { get; init; }

    public required LocalExecutionSequence Sequence { get; init; }

    public required LocalExecutionSequenceStep Step { get; init; }

    public required GitWorkspace Workspace { get; init; }

    public required GitCheckpoint Source { get; init; }

    public required LocalExecutionSequenceRecipe Recipe { get; init; }

    public required LocalExecutionDispatchWitness Witness { get; init; }

    public required LocalExecutionTerminationObservation Observation { get; init; }

    public required LocalExecutionOwnershipRelease Release { get; init; }

    /// <summary>The release that names this step's witness; it must be the very same row as <see cref="Release"/>, or the chain is
    /// refused (a receipt that names one but not the other is never merged into a coherent answer).</summary>
    public required LocalExecutionOwnershipRelease ReleaseByWitness { get; init; }

    public required RunEvent Event { get; init; }

    public required Attempt Attempt { get; init; }

    public required VerificationExecution Execution { get; init; }

    public required LocalExecutionAdmission Admission { get; init; }
}
