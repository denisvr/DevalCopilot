using DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationHistory;
using DevalCopilot.Application.Tests.Features.Projects.ProjectVerificationInspection;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects.ProjectVerificationHistory;

/// <summary>
/// ADR-0069 malformed storage at every conversion seam of a history page: an identifier, a claim time, a number or a name that cannot be
/// admitted makes the whole page Unavailable (never a filtered or partial list and never a conversion fault), the lookahead key must be
/// admitted too, and a keyset page is an independent recorded read: a damaged row outside the read window does not damage the page.
/// </summary>
public sealed class ProjectVerificationHistoryDamageTests : ProjectVerificationInspectionTestBase
{
    private const int Total = 25;

    /// <summary>Number 20 lies inside the newest page (25 down to 6); number 5 is the lookahead row; number 4 lies beyond it.</summary>
    private static Dictionary<string, Func<Guid, FormattableString>> DisplayedRowDefects() => new()
    {
        ["id-lowercase"] = id => $"UPDATE verification_executions SET Id = lower(Id) WHERE Id = {id}",
        ["id-not-a-guid"] = id => $"UPDATE verification_executions SET Id = 'not-a-guid' WHERE Id = {id}",
        ["id-empty-guid"] = id => $"UPDATE verification_executions SET Id = {Guid.Empty} WHERE Id = {id}",
        ["id-braced"] = id => $"UPDATE verification_executions SET Id = '{{' || Id || '}}' WHERE Id = {id}",
        ["id-nul-hidden"] = id => $"UPDATE verification_executions SET Id = substr(Id, 1, 35) || char(0) WHERE Id = {id}",
        ["id-blob"] = id => $"UPDATE verification_executions SET Id = X'0102030405060708' WHERE Id = {id}",
        ["time-t-separator"] = id => $"UPDATE verification_executions SET ClaimedAtUtc = replace(ClaimedAtUtc, ' ', 'T') WHERE Id = {id}",
        ["time-offset"] = id => $"UPDATE verification_executions SET ClaimedAtUtc = substr(ClaimedAtUtc, 1, length(ClaimedAtUtc) - 6) || '+01:00' WHERE Id = {id}",
        ["time-default"] = id => $"UPDATE verification_executions SET ClaimedAtUtc = '0001-01-01 00:00:00+00:00' WHERE Id = {id}",
        ["time-garbage"] = id => $"UPDATE verification_executions SET ClaimedAtUtc = 'yesterday' WHERE Id = {id}",
        ["time-blank"] = id => $"UPDATE verification_executions SET ClaimedAtUtc = '' WHERE Id = {id}",
        ["time-oversized"] = id => $"UPDATE verification_executions SET ClaimedAtUtc = {new string('1', 40)} WHERE Id = {id}",
        ["time-nul-hidden"] = id => $"UPDATE verification_executions SET ClaimedAtUtc = '2026-10-26 09:00:00+00:00' || char(0) WHERE Id = {id}",
        ["time-number"] = id => $"UPDATE verification_executions SET ClaimedAtUtc = 5 WHERE Id = {id}",
        ["number-real"] = id => $"UPDATE verification_executions SET ExecutionNumber = 19.5 WHERE Id = {id}",
        ["number-text"] = id => $"UPDATE verification_executions SET ExecutionNumber = 'twenty' WHERE Id = {id}",
        ["number-numeric-prefix-text"] = id => $"UPDATE verification_executions SET ExecutionNumber = '20abc' WHERE Id = {id}",
        ["number-blob"] = id => $"UPDATE verification_executions SET ExecutionNumber = X'01' WHERE Id = {id}",
        ["number-beyond-int32"] = id => $"UPDATE verification_executions SET ExecutionNumber = 4294967297 WHERE Id = {id}",
        ["number-just-above-int32"] = id => $"UPDATE verification_executions SET ExecutionNumber = 2147483648 WHERE Id = {id}",
        ["name-blank"] = id => $"UPDATE verification_executions SET CommandName = '   ' WHERE Id = {id}",
        ["name-201-units"] = id => $"UPDATE verification_executions SET CommandName = {new string('n', 201)} WHERE Id = {id}",
        ["name-over-600-bytes"] = id => $"UPDATE verification_executions SET CommandName = {new string('n', 601)} WHERE Id = {id}",
        ["name-nul-hidden"] = id => $"UPDATE verification_executions SET CommandName = 'ok' || char(0) || {new string('n', 700)} WHERE Id = {id}",
    };

    public static TheoryData<string> DisplayedRowDefectNames() => new(DisplayedRowDefects().Keys.ToArray());

    private async Task<(Guid ProjectId, IReadOnlyList<SeededExecution> Executions)> SeedProjectAsync()
    {
        var projectId = await Seed.SeedProjectAsync();
        var (workspace, checkpoint) = await Seed.SeedWorkspaceAsync(projectId);
        var command = await Seed.SeedCommandAsync(projectId);
        return (projectId, await Seed.SeedManyAsync(projectId, workspace, checkpoint, command, Total));
    }

    private static void AssertUnavailable(GetProjectVerificationHistoryQueryResult page, Guid projectId)
    {
        Assert.Equal(projectId, page.ProjectId);
        Assert.Equal(ProjectVerificationHistoryStatus.Unavailable, page.Status);
        Assert.Empty(page.Items);
        Assert.False(page.HasMore);
        Assert.Null(page.NextBeforeExecutionNumber);
    }

    [Theory]
    [MemberData(nameof(DisplayedRowDefectNames))]
    public async Task A_defect_in_one_displayed_row_makes_the_whole_page_unavailable_without_omission(string defect)
    {
        var (projectId, executions) = await SeedProjectAsync();
        Assert.Equal(ProjectVerificationHistoryStatus.Recorded, (await HistoryAsync(projectId)).Status);

        await ExecuteAsync(DisplayedRowDefects()[defect](executions.Single(execution => execution.Number == 20).Id));

        AssertUnavailable(await HistoryAsync(projectId), projectId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task A_nonpositive_stored_number_is_refused_on_the_page_that_reads_it(int stored)
    {
        var (projectId, executions) = await SeedProjectAsync();
        await ExecuteAsync($"UPDATE verification_executions SET ExecutionNumber = {stored} WHERE Id = {executions.Single(execution => execution.Number == 2).Id}");

        var newest = await HistoryAsync(projectId);
        var older = await HistoryAsync(projectId, 6);

        Assert.Equal(ProjectVerificationHistoryStatus.Recorded, newest.Status);
        AssertUnavailable(older, projectId);
    }

    public static TheoryData<string> LookaheadDefectNames() => new("id-lowercase", "id-not-a-guid", "id-empty-guid", "number-real");

    [Theory]
    [MemberData(nameof(LookaheadDefectNames))]
    public async Task A_lookahead_key_that_cannot_be_admitted_makes_the_page_unavailable(string defect)
    {
        var (projectId, executions) = await SeedProjectAsync();
        var lookahead = executions.Single(execution => execution.Number == 5).Id;
        FormattableString update = defect switch
        {
            "id-lowercase" => $"UPDATE verification_executions SET Id = lower(Id) WHERE Id = {lookahead}",
            "id-not-a-guid" => $"UPDATE verification_executions SET Id = 'not-a-guid' WHERE Id = {lookahead}",
            "id-empty-guid" => $"UPDATE verification_executions SET Id = {Guid.Empty} WHERE Id = {lookahead}",
            _ => $"UPDATE verification_executions SET ExecutionNumber = 5.5 WHERE Id = {lookahead}",
        };

        await ExecuteAsync(update);

        AssertUnavailable(await HistoryAsync(projectId), projectId);
    }

    [Fact]
    public async Task The_lookahead_only_proves_that_older_executions_exist_so_its_name_and_time_are_not_judged()
    {
        var (projectId, executions) = await SeedProjectAsync();
        var lookahead = executions.Single(execution => execution.Number == 5).Id;
        await ExecuteAsync($"UPDATE verification_executions SET CommandName = '  ', ClaimedAtUtc = 'garbage' WHERE Id = {lookahead}");

        var page = await HistoryAsync(projectId);

        Assert.Equal(ProjectVerificationHistoryStatus.Recorded, page.Status);
        Assert.Equal(20, page.Items.Count);
        Assert.True(page.HasMore);
        Assert.Equal(6, page.NextBeforeExecutionNumber);
    }

    [Fact]
    public async Task A_page_is_an_independent_read_so_a_damaged_row_beyond_its_window_does_not_damage_it()
    {
        var (projectId, executions) = await SeedProjectAsync();
        await ExecuteAsync($"UPDATE verification_executions SET CommandName = '', ClaimedAtUtc = 'garbage' WHERE Id = {executions.Single(execution => execution.Number == 4).Id}");

        var newest = await HistoryAsync(projectId);
        var older = await HistoryAsync(projectId, newest.NextBeforeExecutionNumber);

        Assert.Equal(ProjectVerificationHistoryStatus.Recorded, newest.Status);
        Assert.Equal(20, newest.Items.Count);
        AssertUnavailable(older, projectId);
    }

    [Fact]
    public async Task A_damaged_row_of_another_project_never_affects_this_projects_page()
    {
        var (projectId, _) = await SeedProjectAsync();
        var (otherId, others) = await SeedProjectAsync();
        await ExecuteAsync($"UPDATE verification_executions SET ClaimedAtUtc = 'garbage', Id = 'broken' WHERE Id = {others[^1].Id}");

        Assert.Equal(ProjectVerificationHistoryStatus.Recorded, (await HistoryAsync(projectId)).Status);
        AssertUnavailable(await HistoryAsync(otherId), otherId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MaxValue - 1)]
    public async Task The_valid_extreme_execution_numbers_are_listed_exactly(int number)
    {
        var projectId = await Seed.SeedProjectAsync();
        var (workspace, checkpoint) = await Seed.SeedWorkspaceAsync(projectId);
        var command = await Seed.SeedCommandAsync(projectId);
        var execution = await Seed.SeedExecutionAsync(projectId, workspace, checkpoint, command, number);

        var page = await HistoryAsync(projectId);

        var item = Assert.Single(page.Items);
        Assert.Equal((execution.Id, number), (item.ExecutionId, item.ExecutionNumber));
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task An_integral_real_stored_in_the_integer_column_is_the_integer_it_is()
    {
        var (projectId, executions) = await SeedProjectAsync();
        await ExecuteAsync($"UPDATE verification_executions SET ExecutionNumber = 20.0 WHERE Id = {executions.Single(execution => execution.Number == 20).Id}");

        var page = await HistoryAsync(projectId);

        Assert.Equal(ProjectVerificationHistoryStatus.Recorded, page.Status);
        Assert.Contains(page.Items, item => item.ExecutionNumber == 20);
    }
}
