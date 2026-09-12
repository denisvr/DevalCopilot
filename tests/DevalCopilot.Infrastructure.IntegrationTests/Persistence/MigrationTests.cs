using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

public sealed class MigrationTests(SqliteFileFixture fixture) : IClassFixture<SqliteFileFixture>
{
    [Fact]
    public async Task Migrate_creates_a_schema_that_accepts_a_project_run_attempt_and_event()
    {
        var now = DateTimeOffset.UtcNow;

        await using (var context = fixture.CreateContext())
        {
            await context.Database.MigrateAsync();

            var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\migration-test");
            context.Projects.Add(project);
            await context.SaveChangesAsync();

            var run = Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Prove the schema", now);
            context.Runs.Add(run);
            await context.SaveChangesAsync();

            run.Claim(now);
            var attempt = Attempt.Claim(Guid.NewGuid(), run.Id, attemptNumber: 1, now);
            context.Attempts.Add(attempt);
            await context.SaveChangesAsync();

            var runEvent = RunEvent.Record(Guid.NewGuid(), run.Id, attempt.Id, RunEventType.RunStarted, ParticipantKind.Orchestrator, "{}", now);
            context.Events.Add(runEvent);
            await context.SaveChangesAsync();

            Assert.True(runEvent.Sequence > 0);
        }
    }

    [Fact]
    public async Task Migrate_applied_twice_is_idempotent()
    {
        await using var first = fixture.CreateContext();
        await first.Database.MigrateAsync();

        await using var second = fixture.CreateContext();
        await second.Database.MigrateAsync();

        var appliedMigrations = await second.Database.GetAppliedMigrationsAsync();
        Assert.Contains("InitialCreate", appliedMigrations.Select(id => id.Split('_').Last()));
    }
}
