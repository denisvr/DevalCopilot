namespace DevalCopilot.Api.Features.Projects.GetProjectRunHistory;

public sealed record ProjectRunHistoryReceiptSourceResponse(Guid RunId, Guid OperationId, string CommitSha, Guid CheckpointId, int CheckpointNumber);
