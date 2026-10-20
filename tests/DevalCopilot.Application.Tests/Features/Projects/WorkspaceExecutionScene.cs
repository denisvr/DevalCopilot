using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Tests.Features.Projects;

/// <summary>One project with a Ready, leased workspace, a current checkpoint, an enabled recipe and one claimed Running run: the
/// arrangement the workspace execution exclusion (ADR-0057) tests start from.</summary>
internal sealed record WorkspaceExecutionScene(
    Project Project, GitWorkspace Workspace, GitCheckpoint Checkpoint, VerificationCommand Recipe, Run Run, RepositoryMutationLease Lease);
