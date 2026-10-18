using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>A real, coherent recorded-verification lineage written through the production domain factories into the fixture's
/// file-backed SQLite database: project, run, workspace, checkpoint, recipes, terminal executions and the attempt that claimed them.</summary>
internal sealed record SeededVerificationScene(
    Guid ProjectId,
    Guid RunId,
    GitWorkspace Workspace,
    GitCheckpoint Checkpoint,
    Attempt Attempt,
    IReadOnlyList<SeededVerificationMember> Members);
