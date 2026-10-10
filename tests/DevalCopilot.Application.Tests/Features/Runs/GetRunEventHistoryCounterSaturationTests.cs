using DevalCopilot.Application.Features.Runs.Queries.GetRunEventHistory;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// ADR-0034: a stored row at the signed 64-bit maximum saturates SQLite's own sequence counter, so no later insert can succeed in
/// that database. This is the only class that does it, and each test owns a private database, so no other control shares a
/// saturated counter. The page must still read the maximum exactly and disclose it as an exact decimal string.
/// </summary>
public sealed class GetRunEventHistoryCounterSaturationTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task A_header_stored_at_the_int64_maximum_is_read_exactly_and_is_an_exclusive_cursor_ceiling()
    {
        Guid runId;
        Guid olderId;
        await using (var dbContext = _fixture.CreateContext())
        {
            var project = Project.Register(Guid.NewGuid(), "Saturated", $@"C:\repos\{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
            dbContext.Projects.Add(project);
            var run = Run.RecordClassifiedIntent(
                Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Objective", DateTimeOffset.UtcNow);
            dbContext.Runs.Add(run);
            var older = RunEvent.Record(
                Guid.NewGuid(), run.Id, null, RunEventType.RunStarted, ParticipantIdentity.ForOrchestrator(), "{}", DateTimeOffset.UtcNow);
            dbContext.Events.Add(older);
            await dbContext.SaveChangesAsync();
            (runId, olderId) = (run.Id, older.Id);
        }

        var maximumId = Guid.NewGuid();
        await using (var dbContext = _fixture.CreateContext())
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO events (Sequence, Id, RunId, AttemptId, EventType, ActorKind, ActorAgentRole, ActorAgentProvider, PayloadJson, OccurredAtUtc)
                   VALUES ({long.MaxValue}, {maximumId}, {runId}, NULL, {RunEventType.RunCompleted}, 'Orchestrator', NULL, NULL, '{{}}', '2026-10-09 10:00:00+00:00')");
        }

        await using var reader = _fixture.CreateContext();
        var handler = new GetRunEventHistoryQueryHandler(reader);
        var all = await handler.HandleAsync(new GetRunEventHistoryQuery(runId, null, null), CancellationToken.None);
        var belowMaximum = await handler.HandleAsync(
            new GetRunEventHistoryQuery(runId, "9223372036854775807", null), CancellationToken.None);
        var onePage = await handler.HandleAsync(new GetRunEventHistoryQuery(runId, null, 1), CancellationToken.None);

        Assert.Equal([long.MaxValue, all.Value.Entries[1].Sequence], all.Value.Entries.Select(entry => entry.Sequence));
        Assert.Equal(maximumId, all.Value.Entries[0].Id);
        Assert.Equal(RunEventType.RunCompleted, all.Value.Entries[0].EventType);
        Assert.Equal([olderId], belowMaximum.Value.Entries.Select(entry => entry.Id));
        Assert.Equal([long.MaxValue], onePage.Value.Entries.Select(entry => entry.Sequence));
        Assert.True(onePage.Value.HasMore);
        Assert.Equal(long.MaxValue, onePage.Value.NextBeforeSequence);
        Assert.Equal("9223372036854775807", RunEventHistoryCursor.Format(onePage.Value.NextBeforeSequence!.Value));
    }
}
