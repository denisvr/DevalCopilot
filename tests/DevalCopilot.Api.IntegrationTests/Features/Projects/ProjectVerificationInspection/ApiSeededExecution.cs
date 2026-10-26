namespace DevalCopilot.Api.IntegrationTests.Features.Projects.ProjectVerificationInspection;

public sealed record ApiSeededExecution(Guid Id, int Number, Guid CommandId, Guid WorkspaceId, Guid CheckpointId);
