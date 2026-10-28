namespace DevalCopilot.Api.IntegrationTests.Features.Runs.RecordedLocalCommitInspection;

/// <summary>The identities and recorded facts of one seeded run and its operation, for comparing the wire with the database.</summary>
public sealed record LocalCommitInspectionApiScene(
    Guid RunId,
    Guid ProjectId,
    Guid WorkspaceId,
    Guid CheckpointId,
    Guid OperationId,
    string Branch,
    string ProposedCommit);
