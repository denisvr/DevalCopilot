using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Policies.FormatRepair;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Only <see cref="InvalidOperationException"/> raised while materializing the source row means "unreadable source
/// evidence". A database failure or a cancellation is an operational failure and must propagate as such — never be
/// reported as an ineligible source — at the evaluator and at the request.
/// </summary>
public sealed class ReadOnlyFormatRepairSourceFailureTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task A_cancelled_evaluation_propagates_cancellation_instead_of_reporting_an_invalid_source()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _) = scene.AddCriticalReviewSource();
        await scene.SaveAsync();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadOnlyFormatRepairSource.EvaluateAsync(
            scene.Db, scene.Run.Id, source.Id, AgentResponseContract.CriticalReview, context: null, cancelled.Token));
    }

    [Fact]
    public async Task A_database_failure_while_evaluating_propagates_instead_of_reporting_an_invalid_source()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _) = scene.AddCriticalReviewSource();
        await scene.SaveAsync();
        await using (var context = _fixture.CreateContext())
        {
            // The semantic-message check reads this table after the source itself was read and judged eligible.
            await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
            await context.Database.ExecuteSqlRawAsync("DROP TABLE collaboration_messages");
        }

        scene.Detach();
        await Assert.ThrowsAnyAsync<System.Data.Common.DbException>(() => ReadOnlyFormatRepairSource.EvaluateAsync(
            scene.Db, scene.Run.Id, source.Id, AgentResponseContract.CriticalReview, context: null, CancellationToken.None));
    }

    [Fact]
    public async Task An_unreadable_source_row_is_reported_as_ineligible_without_a_crash_or_an_echo()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _) = scene.AddCriticalReviewSource();
        await scene.SaveAsync();
        await scene.CorruptAsync(source.Id, "AgentOutcome = 'NoSuchOutcome'");

        var evaluation = await ReadOnlyFormatRepairSource.EvaluateAsync(
            scene.Db, scene.Run.Id, source.Id, AgentResponseContract.CriticalReview, context: null, CancellationToken.None);

        Assert.Null(evaluation.Source);
        Assert.Equal("agent_attempts.repair_source_ineligible", evaluation.Error!.Code);
        Assert.DoesNotContain("NoSuchOutcome", evaluation.Error.Description, StringComparison.Ordinal);
    }
}
