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
/// Proves <c>AddDiagnosisCorrectionEscalation</c> (ADR-0018) is additive and truthful against the real parent schema: one
/// table, no backfill and no inferred consent, the database itself enforces one escalation per diagnosis attempt, cascades
/// from the diagnosis attempt and the run, restricts deletion of the escalation message, and the reverse migration drops only
/// that table. The EF model has no pending changes.
/// </summary>
public sealed class AddDiagnosisCorrectionEscalationMigrationTests : IAsyncLifetime
{
    private const string PriorMigration = "20261001185037_AddPlanningImplementationAuthorization";

    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-diagnosis-escalation-migration-{Guid.NewGuid():N}.db");

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

    private sealed record Seed(Guid RunId, Guid DiagnosisAttemptId, Guid ReportId);

    /// <summary>A run, a dispatched Codex diagnosis attempt that recorded findings, and the report it diagnosed.</summary>
    private static async Task<Seed> SeedDiagnosisAsync(DevalCopilotDbContext context, int runNumber = 1)
    {
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        context.Projects.Add(Project.Register(projectId, "Diagnosis", $@"C:\repos\{Guid.NewGuid():N}", Now));
        context.Runs.Add(Run.RecordIntent(runId, projectId, runNumber, "Objective", Now));
        var diagnosis = Attempt.ClaimAgentVerificationDiagnosis(
            Guid.NewGuid(), runId, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10), 1024, 2048, Now, null, null, 1);
        diagnosis.MarkAgentDispatched(Now);
        context.Attempts.Add(diagnosis);
        await context.SaveChangesAsync();
        return new Seed(runId, diagnosis.Id, Guid.NewGuid());
    }

    private static CollaborationMessage HostEscalation(Guid runId, Guid reportId) => CollaborationMessage.Record(
        Guid.NewGuid(), runId, null, CollaborationMessage.ProtocolVersionOne, ParticipantIdentity.ForOrchestrator(),
        ParticipantIdentity.ForHuman(), CollaborationMessageType.Escalation, reportId, "Correcting needs a human decision.",
        "{\"unresolvedDecision\":\"u\",\"options\":\"o\",\"consequences\":\"c\",\"evidence\":\"e\",\"recommendedChoice\":\"r\"}",
        CollaborationMessageProvenance.HostConstructed, Now);

    private static async Task<DiagnosisCorrectionEscalation> AddEscalationAsync(DevalCopilotDbContext context, Seed seed)
    {
        var message = HostEscalation(seed.RunId, seed.ReportId);
        context.CollaborationMessages.Add(message);
        await context.SaveChangesAsync();
        var escalation = DiagnosisCorrectionEscalation.Record(Guid.NewGuid(), seed.RunId, seed.DiagnosisAttemptId, message.Id, Now);
        context.DiagnosisCorrectionEscalations.Add(escalation);
        await context.SaveChangesAsync();
        return escalation;
    }

    [Fact]
    public async Task The_table_and_its_unique_diagnosis_index_exist_and_the_model_has_no_pending_changes()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        Assert.Contains("diagnosis_correction_escalations", await ReadSchemaNamesAsync("table"));
        var indexes = await ReadSchemaNamesAsync("index");
        Assert.Contains("IX_diagnosis_correction_escalations_VerificationDiagnosisAttemptId", indexes);
        Assert.Contains("IX_diagnosis_correction_escalations_CollaborationMessageId", indexes);
        Assert.Contains("IX_diagnosis_correction_escalations_RunId", indexes);
        await using var probe = new SqliteConnection($"Data Source={_databasePath}");
        await probe.OpenAsync();
        await using var command = probe.CreateCommand();
        command.CommandText =
            "SELECT sql FROM sqlite_master WHERE name = 'IX_diagnosis_correction_escalations_VerificationDiagnosisAttemptId';";
        Assert.Contains("UNIQUE", (string)(await command.ExecuteScalarAsync())!, StringComparison.OrdinalIgnoreCase);
        await using var model = CreateContext();
        Assert.False(model.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Historical_review_escalations_authorizations_and_messages_survive_and_no_diagnosis_escalation_is_fabricated()
    {
        var runId = Guid.NewGuid();
        await using (var previous = CreateContext())
        {
            await previous.Database.GetService<IMigrator>().MigrateAsync(PriorMigration);
            var projectId = Guid.NewGuid();
            previous.Projects.Add(Project.Register(projectId, "Historical", $@"C:\repos\{Guid.NewGuid():N}", Now));
            previous.Runs.Add(Run.RecordIntent(runId, projectId, 1, "Historical run", Now));
            var review = Attempt.ClaimAgentCodeReview(
                Guid.NewGuid(), runId, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10), 1024, 2048, Now, 1);
            review.MarkAgentDispatched(Now);
            previous.Attempts.Add(review);
            await previous.SaveChangesAsync();
            var escalationMessage = HostEscalation(runId, Guid.NewGuid());
            previous.CollaborationMessages.Add(escalationMessage);
            await previous.SaveChangesAsync();
            var reviewEscalation = ReviewCorrectionEscalation.Record(Guid.NewGuid(), runId, review.Id, escalationMessage.Id, Now);
            previous.ReviewCorrectionEscalations.Add(reviewEscalation);
            var instruction = CollaborationMessage.RecordHumanInstruction(
                Guid.NewGuid(), runId, escalationMessage.Id, ReviewCorrectionGuidance.BuildStructuredContentJson("Historical reason."), Now);
            previous.CollaborationMessages.Add(instruction);
            await previous.SaveChangesAsync();
            previous.ReviewCorrectionAuthorizations.Add(
                ReviewCorrectionAuthorization.Create(Guid.NewGuid(), runId, reviewEscalation.Id, instruction.Id, Now));
            await previous.SaveChangesAsync();
        }

        await using (var upgraded = CreateContext())
        {
            await upgraded.Database.MigrateAsync();
        }

        await using var reopened = CreateContext();
        Assert.Equal(0, await reopened.DiagnosisCorrectionEscalations.CountAsync());
        Assert.Equal(1, await reopened.ReviewCorrectionEscalations.CountAsync());
        var authorization = await reopened.ReviewCorrectionAuthorizations.AsNoTracking().SingleAsync();
        Assert.True(authorization.IsAvailable);
        Assert.Equal(2, await reopened.CollaborationMessages.CountAsync());
        Assert.Equal(1, await reopened.Attempts.CountAsync());
    }

    [Fact]
    public async Task The_database_itself_refuses_a_second_escalation_for_one_diagnosis_attempt()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        Seed seed;
        await using (var first = CreateContext())
        {
            seed = await SeedDiagnosisAsync(first);
            await AddEscalationAsync(first, seed);
        }

        await using (var second = CreateContext())
        {
            var message = HostEscalation(seed.RunId, seed.ReportId);
            second.CollaborationMessages.Add(message);
            await second.SaveChangesAsync();
            second.DiagnosisCorrectionEscalations.Add(
                DiagnosisCorrectionEscalation.Record(Guid.NewGuid(), seed.RunId, seed.DiagnosisAttemptId, message.Id, Now.AddMinutes(1)));
            await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        }

        await using var verify = CreateContext();
        Assert.Equal(1, await verify.DiagnosisCorrectionEscalations.CountAsync());
    }

    [Fact]
    public async Task Two_independent_contexts_racing_to_record_one_escalation_converge_to_exactly_one_row()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        Seed seed;
        await using (var seedContext = CreateContext())
        {
            seed = await SeedDiagnosisAsync(seedContext);
        }

        await using var firstContext = CreateContext();
        await using var secondContext = CreateContext();
        foreach (var racer in new[] { firstContext, secondContext })
        {
            var message = HostEscalation(seed.RunId, seed.ReportId);
            racer.CollaborationMessages.Add(message);
            racer.DiagnosisCorrectionEscalations.Add(
                DiagnosisCorrectionEscalation.Record(Guid.NewGuid(), seed.RunId, seed.DiagnosisAttemptId, message.Id, Now));
        }

        await firstContext.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateException>(() => secondContext.SaveChangesAsync());

        await using var verify = CreateContext();
        Assert.Equal(1, await verify.DiagnosisCorrectionEscalations.CountAsync());
        Assert.Equal(1, await verify.CollaborationMessages.CountAsync(m => m.Type == CollaborationMessageType.Escalation));
    }

    [Fact]
    public async Task Deleting_the_diagnosis_attempt_cascades_to_its_escalation_but_leaves_the_message()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        var seed = await SeedDiagnosisAsync(context);
        var escalation = await AddEscalationAsync(context, seed);

        await context.Database.ExecuteSqlRawAsync("DELETE FROM attempts WHERE Id = {0}", seed.DiagnosisAttemptId);

        await using var verify = CreateContext();
        Assert.Equal(0, await verify.DiagnosisCorrectionEscalations.CountAsync());
        Assert.Equal(1, await verify.CollaborationMessages.CountAsync(m => m.Id == escalation.CollaborationMessageId));
    }

    [Fact]
    public async Task The_escalation_message_cannot_be_deleted_while_the_escalation_references_it()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        var seed = await SeedDiagnosisAsync(context);
        var escalation = await AddEscalationAsync(context, seed);

        await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync(
            "DELETE FROM collaboration_messages WHERE Id = {0}", escalation.CollaborationMessageId));

        await using var verify = CreateContext();
        Assert.Equal(1, await verify.DiagnosisCorrectionEscalations.CountAsync());
        Assert.Equal(1, await verify.CollaborationMessages.CountAsync(m => m.Id == escalation.CollaborationMessageId));
    }

    [Fact]
    public async Task An_escalation_for_a_run_or_attempt_or_message_that_does_not_exist_is_refused()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        var seed = await SeedDiagnosisAsync(context);
        var message = HostEscalation(seed.RunId, seed.ReportId);
        context.CollaborationMessages.Add(message);
        await context.SaveChangesAsync();

        foreach (var invalid in new[]
                 {
                     DiagnosisCorrectionEscalation.Record(Guid.NewGuid(), Guid.NewGuid(), seed.DiagnosisAttemptId, message.Id, Now),
                     DiagnosisCorrectionEscalation.Record(Guid.NewGuid(), seed.RunId, Guid.NewGuid(), message.Id, Now),
                     DiagnosisCorrectionEscalation.Record(Guid.NewGuid(), seed.RunId, seed.DiagnosisAttemptId, Guid.NewGuid(), Now),
                 })
        {
            await using var other = CreateContext();
            other.DiagnosisCorrectionEscalations.Add(invalid);
            await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
        }

        await using var verify = CreateContext();
        Assert.Equal(0, await verify.DiagnosisCorrectionEscalations.CountAsync());
    }

    [Fact]
    public async Task The_escalation_round_trips_every_field()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        var seed = await SeedDiagnosisAsync(context);
        var written = await AddEscalationAsync(context, seed);

        await using var verify = CreateContext();
        var stored = await verify.DiagnosisCorrectionEscalations.AsNoTracking().SingleAsync();
        Assert.Equal(written.Id, stored.Id);
        Assert.Equal(seed.RunId, stored.RunId);
        Assert.Equal(seed.DiagnosisAttemptId, stored.VerificationDiagnosisAttemptId);
        Assert.Equal(written.CollaborationMessageId, stored.CollaborationMessageId);
        Assert.Equal(Now, stored.CreatedAtUtc);
    }

    [Fact]
    public async Task The_diagnosis_attempt_tuple_and_new_outcomes_round_trip_through_the_real_schema()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        var seed = await SeedDiagnosisAsync(context);
        var attempt = await context.Attempts.SingleAsync(a => a.Id == seed.DiagnosisAttemptId);
        attempt.CompleteAgent(AgentOutcome.VerificationEvidenceChanged, Fingerprint, Now.AddMinutes(1), AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 0, TimeSpan.FromSeconds(1)));
        await context.SaveChangesAsync();

        await using var verify = CreateContext();
        var stored = await verify.Attempts.AsNoTracking().SingleAsync(a => a.Id == seed.DiagnosisAttemptId);
        Assert.True(VerificationDiagnosisPolicy.HasExactTuple(stored));
        Assert.Equal(AgentResponseContract.VerificationDiagnosis, stored.AgentResponseContract);
        Assert.Equal(AgentOutcome.VerificationEvidenceChanged, stored.AgentOutcome);
        Assert.Equal(VerificationDiagnosisPolicy.AdapterContractVersion, stored.AgentAdapterContractVersion);
    }

    [Fact]
    public async Task Down_drops_only_the_new_table_without_touching_other_data()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            await SeedDiagnosisAsync(context);
        }

        var tablesBefore = await ReadSchemaNamesAsync("table");

        await using (var downgrade = CreateContext())
        {
            await downgrade.Database.GetService<IMigrator>().MigrateAsync(PriorMigration);
        }

        var tablesAfter = await ReadSchemaNamesAsync("table");
        Assert.Equal(["diagnosis_correction_escalations"], tablesBefore.Except(tablesAfter));
        Assert.Empty(tablesAfter.Except(tablesBefore));
        await using var reopened = CreateContext();
        Assert.Equal(1, await reopened.Runs.CountAsync());
        Assert.Equal(1, await reopened.Attempts.CountAsync());
    }
}
