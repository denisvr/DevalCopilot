using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Tests.Features.Projects.ProjectVerificationInspection;

internal sealed record SeededExecution(
    Guid Id, Guid ProjectId, int Number, VerificationCommand Command, GitWorkspace Workspace, GitCheckpoint Checkpoint, ExecutionShape Shape);
