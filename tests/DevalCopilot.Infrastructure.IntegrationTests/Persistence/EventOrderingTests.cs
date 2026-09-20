using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

public sealed class EventOrderingTests(SqliteFileFixture fixture) : IClassFixture<SqliteFileFixture>
{
    [Fact]
    public async Task Sequence_is_database_assigned_and_monotonically_increasing_in_insertion_order()
    {
        var now = DateTimeOffset.UtcNow;
        Guid runId;

        await using (var context = fixture.CreateContext())
        {
            await context.Database.MigrateAsync();

            var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\event-ordering-test", DateTimeOffset.UtcNow);
            context.Projects.Add(project);
            await context.SaveChangesAsync();

            var run = Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Prove ordering", now);
            context.Runs.Add(run);
            await context.SaveChangesAsync();
            runId = run.Id;

            // Insert out of any natural Guid or timestamp order; only insertion order should
            // determine the assigned sequence.
            foreach (var eventType in new[] { RunEventType.RunStarted, RunEventType.CodexProposal, RunEventType.ClaudeChallenge })
            {
                context.Events.Add(RunEvent.Record(Guid.NewGuid(), runId, null, eventType, ParticipantIdentity.ForOrchestrator(), "{}", now));
                await context.SaveChangesAsync();
            }
        }

        await using var readContext = fixture.CreateContext();
        var orderedEvents = await readContext.Events
            .Where(runEvent => runEvent.RunId == runId)
            .OrderBy(runEvent => runEvent.Sequence)
            .ToListAsync();

        Assert.Equal(3, orderedEvents.Count);
        Assert.Equal(RunEventType.RunStarted, orderedEvents[0].EventType);
        Assert.Equal(RunEventType.CodexProposal, orderedEvents[1].EventType);
        Assert.Equal(RunEventType.ClaudeChallenge, orderedEvents[2].EventType);
        Assert.True(orderedEvents[0].Sequence < orderedEvents[1].Sequence);
        Assert.True(orderedEvents[1].Sequence < orderedEvents[2].Sequence);
    }
}
