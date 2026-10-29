using System.Text.RegularExpressions;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Queries.GetRunAbandonmentInspection;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace DevalCopilot.Application.Tests.Features.Runs.RunAbandonmentInspection;

/// <summary>Proves how the recorded abandonment read reads (ADR-0071): exactly one bounded, index-driven, read-only statement over the
/// three named tables for every answer, no forbidden column, no write, no external dependency and no whole-table scan, and a database
/// fault or cancellation that propagates to the normal failure boundary instead of becoming Unavailable or absent.</summary>
public sealed class RunAbandonmentInspectionReadProofTests(ITestOutputHelper output) : RunAbandonmentInspectionTestBase
{
    private static readonly string[] ForbiddenNames =
    [
        "Objective", "Stage", "MaximumReviewCorrectionAttempts", "MaximumAgentAttempts", "MaximumAgentInvocationTime",
        "AccumulatedAutonomousSeconds", "RequestedCodexModel", "RequestedCodexEffort", "RequestedClaudeModel", "RequestedClaudeEffort",
        "RequestedClaudeMaxTurns", "CodexAccountUsageStopPercent", "CodexAccountUsageWarningPercent", "CodexTokenStopThreshold",
        "ClaudeTokenStopThreshold", "CodexTokenWarningThreshold", "ClaudeTokenWarningThreshold", "NextExecutionNumber", "ExecutionNumber",
        "CreatedAtUtc", "RootPath", "attempts", "collaboration_messages", "git_workspaces", "git_checkpoints", "local_commit_operations",
        "repository_mutation_leases", "verification_executions", "artifacts", "COUNT(", "SELECT *",
    ];

    public enum Scene
    {
        ClosedFromCreated,
        ClosedFromRunningWithWorkspace,
        Open,
        Damaged,
    }

    public static TheoryData<Scene> Scenes => new(Enum.GetValues<Scene>());

    private async Task<Guid> SeedAsync(Scene scene)
    {
        switch (scene)
        {
            case Scene.ClosedFromCreated:
                return (await AbandonAsync(running: false)).RunId;
            case Scene.ClosedFromRunningWithWorkspace:
                return (await AbandonAsync(running: true, withWorkspace: true)).RunId;
            case Scene.Open:
                return (await AbandonmentScene.CreateAsync(Fixture, running: true)).RunId;
            default:
                var damaged = await AbandonAsync();
                await ExecuteAsync($"UPDATE events SET PayloadJson = '{{' WHERE RunId = '{Id(damaged.RunId)}' AND EventType = 'run.abandoned'");
                return damaged.RunId;
        }
    }

    [Theory]
    [MemberData(nameof(Scenes))]
    public async Task One_bounded_read_only_statement_over_the_three_named_tables_answers_every_scene(Scene scene)
    {
        var runId = await SeedAsync(scene);
        var before = await ChangeFingerprintAsync();
        var log = new RunAbandonmentInspectionStatementLog();

        await InspectAsync(runId, log);

        var recorded = Assert.Single(log.Commands);
        log.AssertReadOnly();
        log.AssertNeverSelects(ForbiddenNames);
        Assert.Contains(recorded.Parameters, parameter => parameter.Name == "@p" && Equals(parameter.Value, 2));
        Assert.Equal(
            ["events", "projects", "runs"],
            Regex.Matches(recorded.Text, "(?:FROM|JOIN) \"(?<table>[a-z_]+)\"").Select(match => match.Groups["table"].Value).Distinct().Order().ToArray());
        Assert.Single(Regex.Matches(recorded.Text, @"\bLIMIT\b"));
        Assert.Equal(before, await ChangeFingerprintAsync());
    }

    [Fact]
    public async Task An_unknown_run_also_costs_one_read_only_statement()
    {
        var log = new RunAbandonmentInspectionStatementLog();

        Assert.True((await TryInspectAsync(Guid.NewGuid(), log)).IsFailure);

        Assert.Single(log.Commands);
        log.AssertReadOnly();
    }

    [Fact]
    public async Task The_statement_is_driven_by_the_run_key_and_the_run_event_index_with_no_scan_or_sort()
    {
        var closed = await AbandonAsync(running: true, withWorkspace: true);
        var log = new RunAbandonmentInspectionStatementLog();
        await InspectAsync(closed.RunId, log);

        var plan = await RunAbandonmentInspectionPlanProbe.ExplainAsync(Fixture.ConnectionString, Assert.Single(log.Commands));
        output.WriteLine("STATEMENT: " + log.Commands[0].Text);
        output.WriteLine("PLAN: " + plan);

        Assert.Matches(@"SEARCH r USING (INDEX|PRIMARY KEY|COVERING INDEX)", plan);
        Assert.Matches(@"SEARCH \w+ USING (COVERING )?INDEX \w*[Ee]vents\w*", plan);
        Assert.DoesNotContain("SCAN", plan);
        Assert.DoesNotContain("TEMP B-TREE", plan);
    }

    [Fact]
    public void The_handler_has_no_dependency_beyond_the_database_context()
    {
        var constructor = Assert.Single(typeof(GetRunAbandonmentInspectionQueryHandler).GetConstructors());

        Assert.Equal([typeof(IDevalCopilotDbContext)], constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
    }

    [Theory]
    [InlineData("events")]
    [InlineData("projects")]
    [InlineData("runs")]
    public async Task A_database_fault_propagates_and_is_never_relabelled_unavailable_or_absent(string table)
    {
        var closed = await AbandonAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => TryInspectAsync(closed.RunId, new FaultOnTable(table)));
    }

    [Fact]
    public async Task A_cancelled_read_propagates_the_cancellation_and_answers_nothing()
    {
        var closed = await AbandonAsync();
        await using var dbContext = Fixture.CreateContext();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GetRunAbandonmentInspectionQueryHandler(dbContext)
            .HandleAsync(new GetRunAbandonmentInspectionQuery(closed.RunId), cancelled.Token));
    }

    private sealed class FaultOnTable(string table) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            CommandEventData eventData,
            InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default) =>
            command.CommandText.Contains($"\"{table}\"", StringComparison.Ordinal)
                ? throw new InvalidOperationException("Injected database fault.")
                : ValueTask.FromResult(result);
    }
}
