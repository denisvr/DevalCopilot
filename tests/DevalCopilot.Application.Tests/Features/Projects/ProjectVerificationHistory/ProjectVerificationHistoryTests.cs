using DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;
using DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationHistory;
using DevalCopilot.Application.Tests.Features.Projects.ProjectVerificationInspection;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects.ProjectVerificationHistory;

/// <summary>
/// ADR-0069 history pages over real migrated file-backed SQLite: fixed 20-member keyset pages, newest first, a 21-row lookahead that
/// proves older executions exist, exact cursors, another project's executions never listed, snapshot command names that survive later
/// recipe edits and work, and an older manual execution (no run, Agent or local sequence) reachable beyond the live latest 20.
/// </summary>
public sealed class ProjectVerificationHistoryTests : ProjectVerificationInspectionTestBase
{
    private async Task<(Guid ProjectId, IReadOnlyList<SeededExecution> Executions)> SeedProjectWithAsync(int count)
    {
        var projectId = await Seed.SeedProjectAsync();
        var (workspace, checkpoint) = await Seed.SeedWorkspaceAsync(projectId);
        var command = await Seed.SeedCommandAsync(projectId);
        return (projectId, await Seed.SeedManyAsync(projectId, workspace, checkpoint, command, count));
    }

    [Fact]
    public async Task An_unknown_project_is_the_one_fixed_non_echoing_not_found()
    {
        var unknown = Guid.NewGuid();

        var result = await TryHistoryAsync(unknown);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ProjectVerificationInspectionErrors.NotFoundCode, error.Code);
        Assert.DoesNotContain(unknown.ToString(), error.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_known_project_without_executions_is_a_recorded_empty_page_with_no_cursor()
    {
        var projectId = await Seed.SeedProjectAsync();

        var page = await HistoryAsync(projectId);

        Assert.Equal(projectId, page.ProjectId);
        Assert.Equal(ProjectVerificationHistoryStatus.Recorded, page.Status);
        Assert.Empty(page.Items);
        Assert.False(page.HasMore);
        Assert.Null(page.NextBeforeExecutionNumber);
    }

    [Theory]
    [InlineData(1, 1, false)]
    [InlineData(19, 19, false)]
    [InlineData(20, 20, false)]
    [InlineData(21, 20, true)]
    [InlineData(40, 20, true)]
    [InlineData(41, 20, true)]
    public async Task The_newest_page_holds_at_most_twenty_and_the_lookahead_decides_has_more(int total, int displayed, bool hasMore)
    {
        var (projectId, executions) = await SeedProjectWithAsync(total);

        var page = await HistoryAsync(projectId);

        Assert.Equal(ProjectVerificationHistoryStatus.Recorded, page.Status);
        Assert.Equal(displayed, page.Items.Count);
        Assert.Equal(hasMore, page.HasMore);
        Assert.Equal(
            Enumerable.Range(total - displayed + 1, displayed).Reverse().ToArray(),
            page.Items.Select(item => item.ExecutionNumber).ToArray());
        Assert.Equal(hasMore ? total - displayed + 1 : null, page.NextBeforeExecutionNumber);
        var byNumber = executions.ToDictionary(execution => execution.Number);
        Assert.All(page.Items, item => Assert.Equal(byNumber[item.ExecutionNumber].Id, item.ExecutionId));
    }

    [Fact]
    public async Task Cursor_pages_walk_the_whole_history_newest_to_oldest_without_gap_overlap_or_accumulation()
    {
        var (projectId, executions) = await SeedProjectWithAsync(45);

        var first = await HistoryAsync(projectId);
        var second = await HistoryAsync(projectId, first.NextBeforeExecutionNumber);
        var third = await HistoryAsync(projectId, second.NextBeforeExecutionNumber);

        Assert.Equal((20, true, 26), (first.Items.Count, first.HasMore, first.NextBeforeExecutionNumber ?? 0));
        Assert.Equal((20, true, 6), (second.Items.Count, second.HasMore, second.NextBeforeExecutionNumber ?? 0));
        Assert.Equal((5, false, null), (third.Items.Count, third.HasMore, third.NextBeforeExecutionNumber));
        var numbers = first.Items.Concat(second.Items).Concat(third.Items).Select(item => item.ExecutionNumber).ToArray();
        Assert.Equal(Enumerable.Range(1, 45).Reverse().ToArray(), numbers);
        Assert.Equal(
            executions.OrderByDescending(execution => execution.Number).Select(execution => execution.Id).ToArray(),
            first.Items.Concat(second.Items).Concat(third.Items).Select(item => item.ExecutionId).ToArray());
    }

    [Theory]
    [InlineData(1, 0, false)]
    [InlineData(2, 1, false)]
    [InlineData(21, 20, false)]
    [InlineData(22, 20, true)]
    [InlineData(int.MaxValue, 20, true)]
    public async Task A_cursor_reads_only_strictly_older_executions_at_every_boundary(int before, int displayed, bool hasMore)
    {
        var (projectId, _) = await SeedProjectWithAsync(40);

        var page = await HistoryAsync(projectId, before);

        Assert.Equal(ProjectVerificationHistoryStatus.Recorded, page.Status);
        Assert.Equal(displayed, page.Items.Count);
        Assert.Equal(hasMore, page.HasMore);
        Assert.All(page.Items, item => Assert.True(item.ExecutionNumber < before));
        if (displayed > 0)
        {
            var expectedFirst = Math.Min(before - 1, 40);
            Assert.Equal(expectedFirst, page.Items[0].ExecutionNumber);
            Assert.Equal(hasMore ? page.Items[^1].ExecutionNumber : null, page.NextBeforeExecutionNumber);
        }
    }

    [Fact]
    public async Task An_older_manual_execution_with_no_run_agent_or_sequence_is_reachable_beyond_the_latest_twenty_beside_newer_work()
    {
        var projectId = await Seed.SeedProjectAsync();
        var (workspace, checkpoint) = await Seed.SeedWorkspaceAsync(projectId);
        var command = await Seed.SeedCommandAsync(projectId, 1, "Manual build");
        var oldest = await Seed.SeedExecutionAsync(projectId, workspace, checkpoint, command, 1, ExecutionShape.FailedExited);
        for (var number = 2; number <= 30; number++)
        {
            await Seed.SeedExecutionAsync(projectId, workspace, checkpoint, command, number);
        }

        // Newer work of every other kind: a run, a newer workspace, a newer checkpoint and a newer execution on it.
        await using (var dbContext = Fixture.CreateContext())
        {
            dbContext.Runs.Add(Run.RecordClassifiedIntent(
                Guid.NewGuid(), projectId, 1, RunExecutionMode.ManualAgent, "Newer run", ProjectVerificationInspectionSeed.Now.AddHours(1)));
            await dbContext.SaveChangesAsync();
        }

        var (newerWorkspace, newerCheckpoint) = await Seed.SeedWorkspaceAsync(projectId, 1, new string('c', 64));
        await Seed.SeedExecutionAsync(projectId, newerWorkspace, newerCheckpoint, command, 31);

        var newest = await HistoryAsync(projectId);
        var pageTwo = await HistoryAsync(projectId, newest.NextBeforeExecutionNumber);

        Assert.DoesNotContain(newest.Items, item => item.ExecutionId == oldest.Id);
        Assert.Equal(11, pageTwo.Items.Count);
        Assert.False(pageTwo.HasMore);
        var item = pageTwo.Items[^1];
        Assert.Equal((oldest.Id, 1, "Manual build"), (item.ExecutionId, item.ExecutionNumber, item.CommandName));
        Assert.Equal(ProjectVerificationInspectionSeed.Now.AddSeconds(1), item.ClaimedAtUtc);
        await using var verify = Fixture.CreateContext();
        Assert.Empty(verify.AttemptVerificationEvidence);
        Assert.Empty(verify.LocalExecutionSequenceSteps);
    }

    [Fact]
    public async Task Another_projects_executions_are_never_listed_and_each_project_has_its_own_numbers()
    {
        var (projectId, _) = await SeedProjectWithAsync(3);
        var (otherId, otherExecutions) = await SeedProjectWithAsync(5);

        var page = await HistoryAsync(projectId);
        var other = await HistoryAsync(otherId);

        Assert.Equal([3, 2, 1], page.Items.Select(item => item.ExecutionNumber));
        Assert.Equal([5, 4, 3, 2, 1], other.Items.Select(item => item.ExecutionNumber));
        Assert.Empty(page.Items.Select(item => item.ExecutionId).Intersect(otherExecutions.Select(execution => execution.Id)));
    }

    [Fact]
    public async Task The_listed_name_is_the_execution_snapshot_whatever_later_happens_to_the_recipe()
    {
        var projectId = await Seed.SeedProjectAsync();
        var (workspace, checkpoint) = await Seed.SeedWorkspaceAsync(projectId);
        var command = await Seed.SeedCommandAsync(projectId, 1, "Original name");
        await Seed.SeedExecutionAsync(projectId, workspace, checkpoint, command, 1);

        await ExecuteAsync($"UPDATE verification_commands SET Name = 'Renamed later', IsEnabled = 0 WHERE Id = {command.Id}");
        var disabled = await HistoryAsync(projectId);
        await ExecuteAsync($"UPDATE verification_commands SET Name = 'Third name', ExecutablePath = 'C:\\other.exe' WHERE Id = {command.Id}");
        var edited = await HistoryAsync(projectId);

        Assert.Equal("Original name", Assert.Single(disabled.Items).CommandName);
        Assert.Equal("Original name", Assert.Single(edited.Items).CommandName);
    }

    [Fact]
    public async Task A_reopened_context_and_a_new_page_read_return_the_same_recorded_page()
    {
        var (projectId, _) = await SeedProjectWithAsync(23);

        var first = await HistoryAsync(projectId);
        Microsoft.Data.Sqlite.SqliteConnection.ClearPool(new Microsoft.Data.Sqlite.SqliteConnection(Fixture.ConnectionString));
        var reopened = await HistoryAsync(projectId);

        Assert.Equal(first.Items, reopened.Items);
        Assert.Equal((first.HasMore, first.NextBeforeExecutionNumber), (reopened.HasMore, reopened.NextBeforeExecutionNumber));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public async Task A_nonpositive_cursor_is_refused_never_clamped(int before)
    {
        var projectId = await Seed.SeedProjectAsync();

        var result = await TryHistoryAsync(projectId, before);

        Assert.True(result.IsFailure);
        Assert.Equal(ProjectVerificationInspectionErrors.InvalidBeforeExecutionNumberCode, Assert.Single(result.Errors).Code);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(1, true)]
    [InlineData(int.MaxValue, true)]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    public async Task The_validator_admits_only_a_positive_int32_or_no_cursor(int? before, bool valid)
    {
        var validator = new GetProjectVerificationHistoryQueryValidator();

        var result = await validator.ValidateAsync(new GetProjectVerificationHistoryQuery(Guid.NewGuid(), before));

        Assert.Equal(valid, result.IsValid);
        if (!valid)
        {
            var failure = Assert.Single(result.Errors);
            Assert.Equal(ProjectVerificationInspectionErrors.InvalidBeforeExecutionNumberCode, failure.ErrorCode);
            Assert.DoesNotContain(before!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), failure.ErrorMessage, StringComparison.Ordinal);
        }
    }
}
