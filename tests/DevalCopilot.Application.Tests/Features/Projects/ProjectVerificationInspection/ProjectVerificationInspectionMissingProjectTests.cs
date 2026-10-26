using System.Text;
using DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;
using DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationInspection;
using DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationOutputWindow;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Features.Processes;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects.ProjectVerificationInspection;

/// <summary>
/// PVI-R1 (ADR-0069 review 1): an unknown project is the fixed non-echoing not-found on all three operations even when its executions,
/// workspace, checkpoint and sealed output rows were retained (the project row is deleted with foreign keys off, damage setup only). The
/// metadata and the window read no stream row and no file, nothing is written, and the valid-project, foreign-execution and
/// damaged-member controls keep their answers.
/// </summary>
public sealed class ProjectVerificationInspectionMissingProjectTests : ProjectVerificationInspectionBaseDamage
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-missing-project-{Guid.NewGuid():N}");

    public override async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        await base.InitializeAsync();
    }

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private async Task<(DamageScene Scene, ProjectVerificationRecordingStore Store)> SeedWithSealedOutputAsync()
    {
        var scene = await SeedAsync(ExecutionShape.FailedExited);
        var relative = Path.Combine("verification", scene.Execution.Id.ToString("N"), "stdout.sealed");
        var bytes = Encoding.UTF8.GetBytes("retained sealed output");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_root, relative))!);
        await File.WriteAllBytesAsync(Path.Combine(_root, relative), bytes);
        var hash = "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        await Seed.AddOutputAsync(scene.Execution.Id, VerificationOutputPurpose.StandardOutput, relative, hash, bytes.Length);
        return (scene, new ProjectVerificationRecordingStore(new FilesystemArtifactStore(_root)));
    }

    private async Task<Devalente.Shared.Results.Result<GetProjectVerificationOutputWindowQueryResult>> WindowAsync(
        Guid projectId, Guid executionId, ProjectVerificationRecordingStore store, ProjectVerificationStatementLog? log = null)
    {
        await using var context = Fixture.CreateContext(log is null ? [] : [log]);
        var result = await new GetProjectVerificationOutputWindowQueryHandler(context, store).HandleAsync(
            new GetProjectVerificationOutputWindowQuery(projectId, executionId, VerificationOutputPurpose.StandardOutput, 0, 64),
            CancellationToken.None);
        Assert.Empty(context.ChangeTracker.Entries());
        return result;
    }

    private static void AssertFixedNotFound(Devalente.Shared.Results.Error error, params Guid[] identities)
    {
        Assert.Equal(ProjectVerificationInspectionErrors.NotFoundCode, error.Code);
        Assert.Equal(ProjectVerificationInspectionErrors.NotFound().Description, error.Description);
        foreach (var identity in identities)
        {
            Assert.DoesNotContain(identity.ToString(), error.Description, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("history")]
    [InlineData("detail")]
    [InlineData("window")]
    public async Task An_absent_project_with_retained_children_is_the_fixed_not_found_and_reads_no_stream_row_or_file(string operation)
    {
        var (scene, store) = await SeedWithSealedOutputAsync();
        await ExecuteAsync($"DELETE FROM projects WHERE Id = {scene.ProjectId}");
        var before = await ChangeFingerprintAsync();
        var log = new ProjectVerificationStatementLog();

        var error = operation switch
        {
            "history" => (await TryHistoryAsync(scene.ProjectId, null, log)).Errors.Single(),
            "detail" => (await TryInspectAsync(scene.ProjectId, scene.Execution.Id, log)).Errors.Single(),
            _ => (await WindowAsync(scene.ProjectId, scene.Execution.Id, store, log)).Errors.Single(),
        };

        AssertFixedNotFound(error, scene.ProjectId, scene.Execution.Id);
        Assert.Empty(store.Calls);
        log.AssertReadOnly();
        log.AssertNeverSelects("verification_output_artifacts", "RelativeStoragePath", "ContentHash", "git_checkpoints");
        Assert.Equal(before, await ChangeFingerprintAsync());
    }

    [Fact]
    public async Task The_same_scene_with_the_project_present_is_recorded_and_serves_the_sealed_window()
    {
        var (scene, store) = await SeedWithSealedOutputAsync();

        var detail = await InspectAsync(scene.ProjectId, scene.Execution.Id);
        var window = await WindowAsync(scene.ProjectId, scene.Execution.Id, store);

        Assert.Equal(ProjectVerificationInspectionStatus.Recorded, detail.Status);
        Assert.True(window.IsSuccess);
        Assert.Equal(ProjectVerificationOutputWindowStatus.Ok, window.Value.Status);
        Assert.Equal("retained sealed output", window.Value.Text);
        Assert.Single(store.Calls);
    }

    [Fact]
    public async Task A_foreign_execution_of_a_present_project_stays_the_fixed_not_found()
    {
        var (scene, store) = await SeedWithSealedOutputAsync();
        var otherProject = await Seed.SeedProjectAsync();

        var detail = await TryInspectAsync(otherProject, scene.Execution.Id);
        var window = await WindowAsync(otherProject, scene.Execution.Id, store);

        AssertFixedNotFound(detail.Errors.Single(), otherProject, scene.Execution.Id);
        AssertFixedNotFound(window.Errors.Single(), otherProject, scene.Execution.Id);
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task A_damaged_member_of_a_present_project_stays_unavailable_before_any_stream_or_file_read()
    {
        var (scene, store) = await SeedWithSealedOutputAsync();
        await ExecuteAsync($"UPDATE verification_executions SET Status = 'Weird' WHERE Id = {scene.Execution.Id}");
        var log = new ProjectVerificationStatementLog();

        var detail = await InspectAsync(scene.ProjectId, scene.Execution.Id);
        var window = await WindowAsync(scene.ProjectId, scene.Execution.Id, store, log);

        Assert.Equal(ProjectVerificationInspectionStatus.Unavailable, detail.Status);
        Assert.Null(detail.Execution);
        Assert.True(window.IsSuccess);
        Assert.Equal(ProjectVerificationOutputWindowStatus.Unavailable, window.Value.Status);
        Assert.Empty(store.Calls);
        log.AssertNeverSelects("verification_output_artifacts");
    }

    [Fact]
    public async Task Deleting_only_the_project_row_after_a_successful_read_stops_the_next_window_and_metadata_read()
    {
        var (scene, store) = await SeedWithSealedOutputAsync();
        Assert.Equal(ProjectVerificationOutputWindowStatus.Ok, (await WindowAsync(scene.ProjectId, scene.Execution.Id, store)).Value.Status);

        await ExecuteAsync($"DELETE FROM projects WHERE Id = {scene.ProjectId}");

        AssertFixedNotFound((await WindowAsync(scene.ProjectId, scene.Execution.Id, store)).Errors.Single(), scene.ProjectId);
        AssertFixedNotFound((await TryInspectAsync(scene.ProjectId, scene.Execution.Id)).Errors.Single(), scene.ProjectId);
        Assert.Single(store.Calls);
    }
}
