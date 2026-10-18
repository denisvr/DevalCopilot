namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>One seeded recorded-verification lineage written through the production domain factories.</summary>
public sealed record SeededVerificationLineage(
    Guid ProjectId, Guid RunId, Guid AttemptId, Guid WorkspaceId, Guid CheckpointId, IReadOnlyList<(Guid CommandId, Guid ExecutionId)> Members);
