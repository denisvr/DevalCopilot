namespace DevalCopilot.Application.Tests.Features.Projects.ProjectVerificationInspection;

/// <summary>Seeds one project with one workspace, checkpoint and recipe and the single execution of the requested shape whose storage a
/// damage case then corrupts.</summary>
public abstract class ProjectVerificationInspectionBaseDamage : ProjectVerificationInspectionTestBase
{
    internal async Task<DamageScene> SeedAsync(ExecutionShape shape)
    {
        var projectId = await Seed.SeedProjectAsync();
        var (workspace, checkpoint) = await Seed.SeedWorkspaceAsync(projectId);
        var command = await Seed.SeedCommandAsync(projectId);
        var execution = await Seed.SeedExecutionAsync(projectId, workspace, checkpoint, command, 4, shape, 3);
        return new DamageScene(projectId, execution);
    }
}
