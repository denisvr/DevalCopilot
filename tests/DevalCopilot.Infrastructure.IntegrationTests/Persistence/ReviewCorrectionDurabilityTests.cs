using System.Text.Json;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

/// <summary>Exercises the durable budget/escalation/authorization constraints against file-backed SQLite.</summary>
public sealed class ReviewCorrectionDurabilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Escalations_and_authorizations_enforce_their_durable_uniqueness_boundaries()
    {
        var seed = await SeedAsync();
        try
        {
            await using (var context = CreateContext(seed.DatabasePath))
            {
                var duplicateMessage = EscalationMessage(seed.RunId, Guid.NewGuid());
                context.CollaborationMessages.Add(duplicateMessage);
                context.ReviewCorrectionEscalations.Add(
                    ReviewCorrectionEscalation.Record(Guid.NewGuid(), seed.RunId, seed.ReviewAttemptId, duplicateMessage.Id, Now.AddMinutes(1)));
                await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            }

            await using (var context = CreateContext(seed.DatabasePath))
            {
                var duplicateInstruction = HumanInstructionMessage(seed.RunId, seed.EscalationMessageId, Guid.NewGuid());
                context.CollaborationMessages.Add(duplicateInstruction);
                context.ReviewCorrectionAuthorizations.Add(
                    ReviewCorrectionAuthorization.Create(Guid.NewGuid(), seed.RunId, seed.EscalationId, duplicateInstruction.Id, Now.AddMinutes(1)));
                await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            }

            var firstAttemptId = Guid.NewGuid();
            await using (var consumeContext = CreateContext(seed.DatabasePath))
            {
                var authorization = await consumeContext.ReviewCorrectionAuthorizations.SingleAsync();
                authorization.Consume(firstAttemptId, Now.AddMinutes(1));
                await consumeContext.SaveChangesAsync();
            }

            await using (var context = CreateContext(seed.DatabasePath))
            {
                var secondInstruction = HumanInstructionMessage(seed.RunId, seed.EscalationMessageId, Guid.NewGuid());
                var secondAuthorization = ReviewCorrectionAuthorization.Create(
                    Guid.NewGuid(), seed.RunId, seed.EscalationId, secondInstruction.Id, Now.AddMinutes(2));
                secondAuthorization.Consume(firstAttemptId, Now.AddMinutes(3));
                context.CollaborationMessages.Add(secondInstruction);
                context.ReviewCorrectionAuthorizations.Add(secondAuthorization);
                await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            }
        }
        finally
        {
            DeleteDatabase(seed.DatabasePath);
        }
    }

    [Fact]
    public async Task Two_independent_authorization_contexts_converge_to_one_durable_instruction_event_and_authorization()
    {
        var seed = await SeedAsync(includeAuthorization: false);
        try
        {
            await using var first = CreateContext(seed.DatabasePath);
            await using var second = CreateContext(seed.DatabasePath);
            AddAuthorization(first, seed);
            AddAuthorization(second, seed);

            var outcomes = await Task.WhenAll(SaveIgnoringUniqueRaceAsync(first), SaveIgnoringUniqueRaceAsync(second));

            await using var verify = CreateContext(seed.DatabasePath);
            Assert.Equal(1, await verify.ReviewCorrectionAuthorizations.CountAsync());
            Assert.Equal(1, await verify.CollaborationMessages.CountAsync(message => message.Type == CollaborationMessageType.HumanInstruction));
            Assert.Equal(1, await verify.Events.CountAsync(runEvent =>
                runEvent.RunId == seed.RunId
                && runEvent.EventType == RunEventType.CollaborationMessageRecorded
                && runEvent.PayloadJson.Contains("messageId")));
            Assert.Equal(1, outcomes.Count(outcome => outcome));
            Assert.Equal(1, outcomes.Count(outcome => !outcome));
        }
        finally
        {
            DeleteDatabase(seed.DatabasePath);
        }
    }

    [Fact]
    public async Task Two_independent_claims_cannot_consume_one_authorization_and_the_loser_does_not_consume_it()
    {
        var seed = await SeedAsync();
        var attemptOne = Guid.NewGuid();
        var attemptTwo = Guid.NewGuid();
        try
        {
            await using var first = CreateContext(seed.DatabasePath);
            await using var second = CreateContext(seed.DatabasePath);
            var firstAuthorization = await first.ReviewCorrectionAuthorizations.SingleAsync();
            var secondAuthorization = await second.ReviewCorrectionAuthorizations.SingleAsync();
            firstAuthorization.Consume(attemptOne, Now.AddMinutes(1));
            secondAuthorization.Consume(attemptTwo, Now.AddMinutes(1));

            var outcomes = await Task.WhenAll(SaveIgnoringConcurrencyRaceAsync(first), SaveIgnoringConcurrencyRaceAsync(second));

            await using var verify = CreateContext(seed.DatabasePath);
            var persisted = await verify.ReviewCorrectionAuthorizations.SingleAsync();
            Assert.True(persisted.ConsumedByAttemptId == attemptOne || persisted.ConsumedByAttemptId == attemptTwo);
            Assert.Equal(1, outcomes.Count(outcome => outcome));
            Assert.Equal(1, outcomes.Count(outcome => !outcome));
        }
        finally
        {
            DeleteDatabase(seed.DatabasePath);
        }
    }

    [Fact]
    public async Task Restart_preserves_escalation_authorization_and_consumption()
    {
        var seed = await SeedAsync();
        var attemptId = Guid.NewGuid();
        try
        {
            await using (var context = CreateContext(seed.DatabasePath))
            {
                var authorization = await context.ReviewCorrectionAuthorizations.SingleAsync();
                authorization.Consume(attemptId, Now.AddMinutes(1));
                await context.SaveChangesAsync();
            }

            SqliteConnection.ClearPool(new SqliteConnection($"Data Source={seed.DatabasePath}"));
            await using var restarted = CreateContext(seed.DatabasePath);
            Assert.NotNull(await restarted.ReviewCorrectionEscalations.FindAsync(seed.EscalationId));
            var authorizationAfterRestart = await restarted.ReviewCorrectionAuthorizations.SingleAsync();
            Assert.Equal(attemptId, authorizationAfterRestart.ConsumedByAttemptId);
            Assert.False(authorizationAfterRestart.IsAvailable);
        }
        finally
        {
            DeleteDatabase(seed.DatabasePath);
        }
    }

    private static async Task<bool> SaveIgnoringUniqueRaceAsync(DevalCopilotDbContext context)
    {
        try
        {
            await context.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    private static async Task<bool> SaveIgnoringConcurrencyRaceAsync(DevalCopilotDbContext context)
    {
        try
        {
            await context.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    private static void AddAuthorization(DevalCopilotDbContext context, Seed seed)
    {
        var message = HumanInstructionMessage(seed.RunId, seed.EscalationMessageId, Guid.NewGuid());
        context.CollaborationMessages.Add(message);
        context.ReviewCorrectionAuthorizations.Add(
            ReviewCorrectionAuthorization.Create(Guid.NewGuid(), seed.RunId, seed.EscalationId, message.Id, Now.AddMinutes(1)));
        context.Events.Add(RunEvent.Record(
            Guid.NewGuid(), seed.RunId, null, RunEventType.CollaborationMessageRecorded, message.Actor,
            JsonSerializer.Serialize(new { messageId = message.Id, type = message.Type.ToString() }), Now.AddMinutes(1)));
    }

    private static async Task<Seed> SeedAsync(bool includeAuthorization = true)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-review-correction-{Guid.NewGuid():N}.db");
        await using var context = CreateContext(databasePath);
        await context.Database.MigrateAsync();

        var project = Project.Register(Guid.NewGuid(), "Durable correction", $@"C:\repos\durable-{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Durable correction", Now);
        run.Claim(Now);
        var review = Attempt.ClaimAgentCodeReview(Guid.NewGuid(), run.Id, 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(), TimeSpan.FromMinutes(20), 1, 1, Now);
        review.MarkAgentDispatched(Now);
        review.CompleteAgent(AgentOutcome.ReviewChangesRequested, new string('a', 64), Now, processEvidence: TestProcessEvidence.CleanExit);
        var escalationMessage = EscalationMessage(run.Id, Guid.NewGuid());
        var escalation = ReviewCorrectionEscalation.Record(Guid.NewGuid(), run.Id, review.Id, escalationMessage.Id, Now);
        var instruction = includeAuthorization
            ? HumanInstructionMessage(run.Id, escalationMessage.Id, Guid.NewGuid())
            : null;
        var authorization = instruction is null
            ? null
            : ReviewCorrectionAuthorization.Create(Guid.NewGuid(), run.Id, escalation.Id, instruction.Id, Now);

        context.Projects.Add(project);
        context.Runs.Add(run);
        context.Attempts.Add(review);
        context.CollaborationMessages.Add(escalationMessage);
        if (instruction is not null) context.CollaborationMessages.Add(instruction);
        context.ReviewCorrectionEscalations.Add(escalation);
        if (authorization is not null) context.ReviewCorrectionAuthorizations.Add(authorization);
        context.Events.Add(RunEvent.Record(Guid.NewGuid(), run.Id, null, RunEventType.CollaborationMessageRecorded, escalationMessage.Actor, "{}", Now));
        if (instruction is not null)
        {
            context.Events.Add(RunEvent.Record(Guid.NewGuid(), run.Id, null, RunEventType.CollaborationMessageRecorded, instruction.Actor, "{}", Now));
        }
        await context.SaveChangesAsync();
        return new(databasePath, run.Id, review.Id, escalation.Id, escalationMessage.Id);
    }

    private static CollaborationMessage EscalationMessage(Guid runId, Guid id) => CollaborationMessage.Record(
        id, runId, null, CollaborationMessage.ProtocolVersionOne,
        ParticipantIdentity.ForOrchestrator(), ParticipantIdentity.ForHuman(), CollaborationMessageType.Escalation,
        Guid.NewGuid(), "Review correction requires an explicit human decision.",
        "{\"unresolvedDecision\":\"The review remains unresolved.\",\"options\":\"Authorize or stop.\",\"consequences\":\"One bounded correction claim.\",\"evidence\":\"The limit was reached.\",\"recommendedChoice\":\"Stop.\"}",
        CollaborationMessageProvenance.HostConstructed, Now);

    private static CollaborationMessage HumanInstructionMessage(Guid runId, Guid escalationMessageId, Guid id) =>
        CollaborationMessage.RecordHumanInstruction(
            id, runId, escalationMessageId,
            "{\"instruction\":\"Authorize one additional review-correction attempt.\",\"rationale\":\"Continue only after explicit human authorization.\"}",
            Now);

    private static DevalCopilotDbContext CreateContext(string databasePath) => new(
        new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={databasePath}").Options);

    private static void DeleteDatabase(string path)
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={path}"));
        if (File.Exists(path)) File.Delete(path);
    }

    private sealed record Seed(string DatabasePath, Guid RunId, Guid ReviewAttemptId, Guid EscalationId, Guid EscalationMessageId);
}
