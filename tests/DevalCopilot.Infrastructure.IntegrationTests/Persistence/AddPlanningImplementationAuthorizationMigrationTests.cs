using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Proves <c>AddPlanningImplementationAuthorization</c> is additive and truthful against the real parent schema: it adds
/// one table, backfills nothing and infers no consent (a historical depth-two escalation and a historical review-correction
/// authorization stay exactly as they were, with no grant), enforces its uniqueness and consumption backstops in the
/// database, and the reverse migration drops only that table.
/// </summary>
public sealed class AddPlanningImplementationAuthorizationMigrationTests : IAsyncLifetime
{
    private const string PriorMigration = "20261001131016_AddDirectHumanGuidance";

    private static readonly DateTimeOffset Now = new(2026, 10, 1, 18, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-planning-authorization-migration-{Guid.NewGuid():N}.db");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }

    private DevalCopilotDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={_databasePath}").Options);

    private async Task<List<string>> ReadSchemaNamesAsync(string type)
    {
        await using var probe = new SqliteConnection($"Data Source={_databasePath}");
        await probe.OpenAsync();
        await using var command = probe.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = $type ORDER BY name;";
        command.Parameters.AddWithValue("$type", type);
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private Attempt? _seededPlanner;

    private async Task<(Guid RunId, Guid EscalationId, Guid FinalProposalId, Guid InstructionId, Guid AttemptId)> SeedFreshAsync(
        DevalCopilotDbContext context, bool historicalSchema = false)
    {
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        context.Projects.Add(Project.Register(projectId, "Fresh", $@"C:\repos\{Guid.NewGuid():N}", Now));
        context.Runs.Add(Run.RecordIntent(runId, projectId, 1, "Objective", Now));
        var planner = Attempt.ClaimAgent(
            attemptId, runId, 1, workspaceId, Guid.NewGuid(), Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10), 1024, 2048, Now, 1);
        planner.MarkAgentDispatched(Now);
        _seededPlanner = planner;
        if (historicalSchema)
        {
            // A schema older than the current model cannot take the entity as an ordinary save would write it.
            await context.SaveChangesAsync();
            await HistoricalAgentAttemptRow.InsertAsync(context, planner);
        }
        else
        {
            context.Attempts.Add(planner);
        }

        var proposal = CollaborationMessage.Record(
            Guid.NewGuid(), runId, attemptId, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex),
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal, null,
            "Plan.", "{\"scope\":\"s\",\"implementationSteps\":\"i\",\"risks\":\"r\",\"verificationPlan\":\"v\",\"escalationPoints\":\"e\"}",
            CollaborationMessageProvenance.ProviderObserved, Now);
        context.CollaborationMessages.Add(proposal);
        await context.SaveChangesAsync();
        var escalation = CollaborationMessage.Record(
            Guid.NewGuid(), runId, null, CollaborationMessage.ProtocolVersionOne, ParticipantIdentity.ForOrchestrator(),
            ParticipantIdentity.ForHuman(), CollaborationMessageType.Escalation, proposal.Id, "Needs a human.",
            "{\"unresolvedDecision\":\"u\",\"options\":\"o\",\"consequences\":\"c\",\"evidence\":\"e\",\"recommendedChoice\":\"r\"}",
            CollaborationMessageProvenance.HostConstructed, Now);
        context.CollaborationMessages.Add(escalation);
        await context.SaveChangesAsync();
        var instruction = CollaborationMessage.RecordPlanningImplementationAuthorization(
            Guid.NewGuid(), runId, escalation.Id, PlanningImplementationInstruction.BuildStructuredContentJson("Reason."), Now);
        context.CollaborationMessages.Add(instruction);
        await context.SaveChangesAsync();
        return (runId, escalation.Id, proposal.Id, instruction.Id, attemptId);
    }

    [Fact]
    public async Task The_latest_schema_fresh_seed_persists_every_fact_the_claimed_attempt_supplies()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            await SeedFreshAsync(context);
        }

        await using var reopened = CreateContext();
        var persisted = await reopened.Attempts.AsNoTracking().SingleAsync();
        HistoricalAgentAttemptRow.AssertEveryFactSurvived(reopened, _seededPlanner!, persisted);
    }

    [Fact]
    public async Task Historical_messages_inputs_and_review_correction_authorizations_survive_and_no_grant_is_fabricated()
    {
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var escalationId = Guid.NewGuid();
        var instructionId = Guid.NewGuid();
        Attempt attempt;
        await using (var previous = CreateContext())
        {
            await previous.Database.GetService<IMigrator>().MigrateAsync(PriorMigration);
            previous.Projects.Add(Project.Register(projectId, "Historical", $@"C:\repos\{Guid.NewGuid():N}", Now));
            previous.Runs.Add(Run.RecordIntent(runId, projectId, 1, "Historical depth-two run", Now));
            await previous.SaveChangesAsync();
            attempt = Attempt.ClaimAgent(
                Guid.NewGuid(), runId, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10), 1024, 2048, Now, 1);
            attempt.MarkAgentDispatched(Now);
            await HistoricalAgentAttemptRow.InsertAsync(previous, attempt);
            var proposal = CollaborationMessage.Record(
                Guid.NewGuid(), runId, attempt.Id, CollaborationMessage.ProtocolVersionOne,
                ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex),
                ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal, null,
                "Historical plan.", "{\"scope\":\"s\",\"implementationSteps\":\"i\",\"risks\":\"r\",\"verificationPlan\":\"v\",\"escalationPoints\":\"e\"}",
                CollaborationMessageProvenance.ProviderObserved, Now);
            previous.CollaborationMessages.Add(proposal);
            previous.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, proposal.Id, 0));
            await previous.SaveChangesAsync();
            previous.CollaborationMessages.Add(CollaborationMessage.Record(
                escalationId, runId, null, CollaborationMessage.ProtocolVersionOne, ParticipantIdentity.ForOrchestrator(),
                ParticipantIdentity.ForHuman(), CollaborationMessageType.Escalation, proposal.Id,
                "The second challenge-resolution round is complete and needs a human decision.",
                "{\"unresolvedDecision\":\"u\",\"options\":\"o\",\"consequences\":\"c\",\"evidence\":\"e\",\"recommendedChoice\":\"r\"}",
                CollaborationMessageProvenance.HostConstructed, Now));
            await previous.SaveChangesAsync();
            previous.CollaborationMessages.Add(CollaborationMessage.RecordHumanInstruction(
                instructionId, runId, escalationId, ReviewCorrectionGuidance.BuildStructuredContentJson("Historical reason."), Now));
            await previous.SaveChangesAsync();
        }

        await using (var upgraded = CreateContext())
        {
            await upgraded.Database.MigrateAsync();
        }

        await using var reopened = CreateContext();
        Assert.Equal(0, await reopened.PlanningImplementationAuthorizations.CountAsync());
        Assert.Equal(3, await reopened.CollaborationMessages.CountAsync());
        Assert.Equal(1, await reopened.AttemptInputMessages.CountAsync());
        Assert.Equal(1, await reopened.Attempts.CountAsync());
        HistoricalAgentAttemptRow.AssertEveryFactSurvived(reopened, attempt, await reopened.Attempts.AsNoTracking().SingleAsync(item => item.Id == attempt.Id));
        var historicalInstruction = await reopened.CollaborationMessages.AsNoTracking().SingleAsync(message => message.Id == instructionId);
        Assert.Equal(ReviewCorrectionGuidance.BuildStructuredContentJson("Historical reason."), historicalInstruction.StructuredContentJson);
        Assert.Equal("Human authorized one additional review-correction attempt.", historicalInstruction.Summary);
    }

    [Fact]
    public async Task The_new_table_has_the_uniqueness_and_consumption_indexes_and_the_model_has_no_pending_changes()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        Assert.Contains("planning_implementation_authorizations", await ReadSchemaNamesAsync("table"));
        var indexes = await ReadSchemaNamesAsync("index");
        Assert.Contains("ix_planning_implementation_authorizations_escalation", indexes);
        Assert.Contains("ix_planning_implementation_authorizations_final_proposal", indexes);
        Assert.Contains("ix_planning_implementation_authorizations_instruction", indexes);
        Assert.Contains("ix_planning_implementation_authorizations_attempt_consumed", indexes);
        await using var model = CreateContext();
        Assert.False(model.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task The_database_refuses_a_second_grant_for_one_escalation_proposal_instruction_or_consuming_attempt()
    {
        Guid runId;
        Guid escalationId;
        Guid proposalId;
        Guid instructionId;
        Guid attemptId;
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            (runId, escalationId, proposalId, instructionId, attemptId) = await SeedFreshAsync(context);
            context.PlanningImplementationAuthorizations.Add(PlanningImplementationAuthorization.Create(
                Guid.NewGuid(), runId, escalationId, proposalId, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, instructionId, Now));
            await context.SaveChangesAsync();
        }

        // A second grant on the same escalation, final Proposal, or instruction message is refused by its unique index.
        foreach (var duplicate in new Func<DevalCopilotDbContext, PlanningImplementationAuthorization>[]
                 {
                     _ => PlanningImplementationAuthorization.Create(
                         Guid.NewGuid(), runId, escalationId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(), Now),
                     _ => PlanningImplementationAuthorization.Create(
                         Guid.NewGuid(), runId, Guid.NewGuid(), proposalId, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(), Now),
                     _ => PlanningImplementationAuthorization.Create(
                         Guid.NewGuid(), runId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Fingerprint, instructionId, Now),
                 })
        {
            await using var other = CreateContext();
            other.PlanningImplementationAuthorizations.Add(duplicate(other));
            await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
        }

        await using var verify = CreateContext();
        Assert.Equal(1, await verify.PlanningImplementationAuthorizations.CountAsync());
        Assert.NotEqual(Guid.Empty, attemptId);
    }

    [Fact]
    public async Task Two_connections_cannot_both_consume_one_grant_and_one_attempt_cannot_consume_two()
    {
        Guid grantId;
        Guid attemptId;
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            var seeded = await SeedFreshAsync(context);
            attemptId = seeded.AttemptId;
            var grant = PlanningImplementationAuthorization.Create(
                Guid.NewGuid(), seeded.RunId, seeded.EscalationId, seeded.FinalProposalId, Guid.NewGuid(), Guid.NewGuid(), Fingerprint,
                seeded.InstructionId, Now);
            grantId = grant.Id;
            context.PlanningImplementationAuthorizations.Add(grant);
            await context.SaveChangesAsync();
        }

        await using var first = CreateContext();
        await using var second = CreateContext();
        var firstGrant = await first.PlanningImplementationAuthorizations.SingleAsync(grant => grant.Id == grantId);
        var secondGrant = await second.PlanningImplementationAuthorizations.SingleAsync(grant => grant.Id == grantId);
        firstGrant.Consume(attemptId, Now.AddMinutes(1));
        secondGrant.Consume(attemptId, Now.AddMinutes(2));

        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using var verify = CreateContext();
        var stored = await verify.PlanningImplementationAuthorizations.AsNoTracking().SingleAsync(grant => grant.Id == grantId);
        Assert.Equal(attemptId, stored.ConsumedByAttemptId);
        Assert.Equal(Now.AddMinutes(1), stored.ConsumedAtUtc);
    }

    [Fact]
    public async Task Down_drops_only_the_new_table_without_touching_other_data()
    {
        await using (var context = CreateContext())
        {
            // Stopped at this migration itself: a later migration (which drops its own table on Down) is not part of this proof.
            await context.Database.GetService<IMigrator>().MigrateAsync("20261001185037_AddPlanningImplementationAuthorization");
            await SeedFreshAsync(context, historicalSchema: true);
        }

        var tablesBefore = await ReadSchemaNamesAsync("table");

        await using (var downgrade = CreateContext())
        {
            await downgrade.Database.GetService<IMigrator>().MigrateAsync(PriorMigration);
        }

        var tablesAfter = await ReadSchemaNamesAsync("table");
        Assert.Equal(["planning_implementation_authorizations"], tablesBefore.Except(tablesAfter));
        Assert.Empty(tablesAfter.Except(tablesBefore));
        await using var reopened = CreateContext();
        Assert.Equal(3, await reopened.CollaborationMessages.CountAsync());
        Assert.Equal(1, await reopened.Runs.CountAsync());
    }
}
