namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>The identities of one production-written plan lineage: the Planner root, the plan an arming selects (the root itself when
/// it was Accepted, or the final Human-authorized revision of a depth-two lineage), the owned workspace with its first checkpoint, and
/// the project's enabled verification recipes in command-number order.</summary>
internal sealed record LocalExecutionSequenceLineage(
    Guid RunId,
    Guid ProjectId,
    Guid WorkspaceId,
    Guid CheckpointId,
    Guid RootId,
    Guid PlanId,
    IReadOnlyList<Guid> RecipeIds);
