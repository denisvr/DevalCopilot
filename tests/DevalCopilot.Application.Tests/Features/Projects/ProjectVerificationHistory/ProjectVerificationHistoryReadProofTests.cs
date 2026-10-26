using DevalCopilot.Application.Tests.Features.Projects.ProjectVerificationInspection;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects.ProjectVerificationHistory;

/// <summary>ADR-0069: a history page is a bounded indexed read of scalar columns that writes nothing, observes nothing outside the
/// database and never selects an artifact, path, argument, executable, status or source fact.</summary>
public sealed class ProjectVerificationHistoryReadProofTests : ProjectVerificationInspectionTestBase
{
    private async Task<Guid> SeedProjectWithAsync(int count)
    {
        var projectId = await Seed.SeedProjectAsync();
        var (workspace, checkpoint) = await Seed.SeedWorkspaceAsync(projectId);
        var command = await Seed.SeedCommandAsync(projectId);
        await Seed.SeedManyAsync(projectId, workspace, checkpoint, command, count);
        return projectId;
    }

    [Theory]
    [InlineData(null)]
    [InlineData(30)]
    public async Task A_page_is_two_select_statements_that_read_at_most_twenty_one_rows_and_select_only_the_listed_columns(int? before)
    {
        var projectId = await SeedProjectWithAsync(45);
        var log = new ProjectVerificationStatementLog();
        var fingerprint = await ChangeFingerprintAsync();

        await HistoryAsync(projectId, before, log);

        log.AssertReadOnly();
        log.AssertNeverSelects(
            "WorkspacePath", "ExecutablePath", "\"Arguments\"", "TimeoutSeconds", "\"Status\"", "\"Outcome\"", "\"ExitCode\"",
            "CheckpointFingerprintSha256", "CompletionFingerprintSha256", "DispatchedAtUtc", "CompletedAtUtc", "VerificationCommandId",
            "GitWorkspaceId", "GitCheckpointId", "verification_output_artifacts", "verification_commands", "attempts", "runs", "git_checkpoints");
        Assert.Equal(2, log.Commands.Count);
        var page = Assert.Single(log.Commands, command => command.Text.Contains("FROM \"verification_executions\"", StringComparison.Ordinal));
        Assert.Contains("ORDER BY", page.Text, StringComparison.Ordinal);
        Assert.Contains("DESC", page.Text, StringComparison.Ordinal);
        Assert.Contains("LIMIT", page.Text, StringComparison.Ordinal);
        Assert.Contains(page.Parameters, parameter => parameter.Value is 21 or 21L);
        Assert.Equal(fingerprint, await ChangeFingerprintAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(30)]
    public async Task The_page_statement_is_driven_by_the_project_and_execution_number_index_without_a_sort(int? before)
    {
        var projectId = await SeedProjectWithAsync(45);
        var log = new ProjectVerificationStatementLog();
        await HistoryAsync(projectId, before, log);
        var page = Assert.Single(log.Commands, command => command.Text.Contains("FROM \"verification_executions\"", StringComparison.Ordinal));

        var plan = await ProjectVerificationPlanProbe.ExplainAsync(Fixture.ConnectionString, page);

        Assert.Contains("IX_verification_executions_ProjectId_ExecutionNumber", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("TEMP B-TREE", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("SCAN verification_executions", plan, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Page_cost_does_not_grow_with_the_history_a_larger_project_runs_the_same_two_statements()
    {
        var small = await SeedProjectWithAsync(3);
        var large = await SeedProjectWithAsync(120);
        var smallLog = new ProjectVerificationStatementLog();
        var largeLog = new ProjectVerificationStatementLog();

        await HistoryAsync(small, null, smallLog);
        await HistoryAsync(large, null, largeLog);

        Assert.Equal(2, smallLog.Commands.Count);
        Assert.Equal(2, largeLog.Commands.Count);
    }
}
