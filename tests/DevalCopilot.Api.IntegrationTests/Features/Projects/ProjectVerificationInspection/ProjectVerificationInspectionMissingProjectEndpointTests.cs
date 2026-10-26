using System.Net;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;
using DevalCopilot.Domain.Features.Projects;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Projects.ProjectVerificationInspection;

/// <summary>
/// PVI-R1 (ADR-0069 review 1) at the wire: a project whose row is gone (deleted with foreign keys off, damage setup only) while its
/// executions, checkpoint and real sealed output remain is the one fixed non-echoing 404 on all three protected routes, with no sealed
/// file read and no write; the same scene with the project present, a foreign execution and a damaged member of a present project keep
/// their answers.
/// </summary>
public sealed class ProjectVerificationInspectionMissingProjectEndpointTests(ApiWebApplicationFactory factory)
    : ProjectVerificationInspectionApiTestBase(factory), IClassFixture<ApiWebApplicationFactory>
{
    private async Task<(Guid ProjectId, Guid ExecutionId)> SeedWithSealedOutputAsync()
    {
        var (projectId, executions) = await SeedAsync(true);
        await RecordAsync(executions[0].Id, VerificationOutputPurpose.StandardOutput, "orphaned output"u8.ToArray());
        return (projectId, executions[0].Id);
    }

    private static string RouteOf(string operation, Guid projectId, Guid executionId) => operation switch
    {
        "history" => History(projectId),
        "detail" => Detail(projectId, executionId),
        _ => Window(projectId, executionId, "stdout"),
    };

    [Theory]
    [InlineData("history")]
    [InlineData("detail")]
    [InlineData("window")]
    public async Task An_unknown_project_with_retained_rows_and_sealed_output_is_the_fixed_non_echoing_404_that_reads_no_file(string operation)
    {
        var (projectId, executionId) = await SeedWithSealedOutputAsync();
        await ExecuteAsync($"DELETE FROM projects WHERE Id = {projectId}");
        var files = Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
        var before = await SnapshotAsync();

        var response = await GetAsync(RouteOf(operation, projectId, executionId));

        Assert.Equal(HttpStatusCode.NotFound, response.Status);
        Assert.Equal("urn:devalente:problem:not-found", response.Body.GetProperty("type").GetString());
        Assert.Equal(
            ProjectVerificationInspectionErrors.NotFoundCode,
            Assert.Single(response.Body.GetProperty("errors").EnumerateArray()).GetProperty("code").GetString());
        var body = Normalize(response.Text);
        Assert.DoesNotContain(projectId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(executionId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("orphaned output", response.Text, StringComparison.Ordinal);
        Assert.Equal(0, StoreCalls);
        Assert.Equal(before, await SnapshotAsync());
        Assert.Equal(files, Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task All_three_routes_of_an_unknown_project_share_one_body()
    {
        var (projectId, executionId) = await SeedWithSealedOutputAsync();
        await ExecuteAsync($"DELETE FROM projects WHERE Id = {projectId}");

        var bodies = new List<string>();
        foreach (var operation in new[] { "history", "detail", "window" })
        {
            bodies.Add(Normalize((await GetAsync(RouteOf(operation, projectId, executionId))).Text));
        }

        Assert.Single(bodies.Distinct());
    }

    [Fact]
    public async Task The_same_scene_with_the_project_present_is_recorded_and_serves_the_real_sealed_output()
    {
        var (projectId, executionId) = await SeedWithSealedOutputAsync();

        var detail = await GetAsync(Detail(projectId, executionId));
        var window = await GetAsync(Window(projectId, executionId, "stdout"));

        Assert.Equal("Recorded", detail.Body.GetProperty("status").GetString());
        Assert.Equal("Ok", window.Body.GetProperty("status").GetString());
        Assert.Equal("orphaned output", window.Body.GetProperty("text").GetString());
        Assert.Equal(1, StoreCalls);
    }

    [Fact]
    public async Task A_foreign_execution_and_a_damaged_member_of_a_present_project_keep_their_answers()
    {
        var (projectId, executionId) = await SeedWithSealedOutputAsync();
        var (otherProject, _) = await SeedAsync(false);
        await ExecuteAsync($"UPDATE verification_executions SET Status = 'Weird' WHERE Id = {executionId}");

        var foreign = await GetAsync(Detail(otherProject, executionId));
        var damaged = await GetAsync(Detail(projectId, executionId));
        var damagedWindow = await GetAsync(Window(projectId, executionId, "stdout"));

        Assert.Equal(HttpStatusCode.NotFound, foreign.Status);
        Assert.Equal("Unavailable", damaged.Body.GetProperty("status").GetString());
        Assert.Equal("Unavailable", damagedWindow.Body.GetProperty("status").GetString());
        Assert.Equal(0, StoreCalls);
    }
}
