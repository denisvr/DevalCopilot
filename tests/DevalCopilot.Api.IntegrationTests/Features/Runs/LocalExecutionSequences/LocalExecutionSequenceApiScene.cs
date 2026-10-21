namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>The identities of one seeded arming-ready run (ADR-0060): a Running ManualAgent run with its Ready workspace, lease,
/// latest checkpoint, an explicitly Accepted Planner root plan and an ordered set of enabled verification recipes.</summary>
internal sealed record LocalExecutionSequenceApiScene(
    Guid RunId,
    Guid ProjectId,
    Guid WorkspaceId,
    Guid CheckpointId,
    Guid PlanId,
    IReadOnlyList<Guid> RecipeIds,
    string WorkspacePath,
    string RepositoryPath);
