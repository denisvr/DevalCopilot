using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Commands.CaptureGitWorkspaceCheckpoint;

/// <summary>Captures evidence after Git I/O, not inside an ambient database transaction. A
/// fresh immutable checkpoint is intentionally created for every explicit request.</summary>
public sealed record CaptureGitWorkspaceCheckpointCommand(Guid ProjectId)
    : IManualTransactionCommand<Result<CaptureGitWorkspaceCheckpointCommandResult>>;
