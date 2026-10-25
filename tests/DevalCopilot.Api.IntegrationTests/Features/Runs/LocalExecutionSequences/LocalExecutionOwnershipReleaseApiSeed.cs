using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>
/// ADR-0065 API test support: turns an already ARMED sequence of an API scene into a coherent ended cycle whose one implementation step
/// recorded a terminal timeout, through the production domain transitions and the real database guards, with or without the original
/// host's dispatch witness and observation. No supervisor, provider or process runs: the HTTP tests judge only the transport.
/// </summary>
internal static class LocalExecutionOwnershipReleaseApiSeed
{
    public sealed record Terminal(Guid SequenceId, Guid StepId, Guid AttemptId, Guid? ObservationId);

    /// <summary>
    /// Changes one stored fact through the real database with the triggers of that one table and foreign keys suspended for exactly that
    /// statement and restored byte-for-byte afterwards (a direct damaged-storage write the production commands never produce).
    /// </summary>
    public static async Task TamperAsync(DevalCopilotDbContext db, string table, string sql, params object[] parameters)
    {
        await db.Database.OpenConnectionAsync();
        var triggers = new List<(string Name, string Definition)>();
        await using (var read = db.Database.GetDbConnection().CreateCommand())
        {
            read.CommandText = "SELECT name, sql FROM sqlite_master WHERE type = 'trigger' AND tbl_name = $table;";
            var parameter = read.CreateParameter();
            parameter.ParameterName = "$table";
            parameter.Value = table;
            read.Parameters.Add(parameter);
            await using var reader = await read.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                triggers.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        await ExecuteAsync(db, "PRAGMA foreign_keys = OFF;");
        try
        {
            foreach (var (name, _) in triggers)
            {
                await ExecuteAsync(db, "DROP TRIGGER \"" + name + "\";");
            }

            Assert.True(await db.Database.ExecuteSqlRawAsync(sql, parameters) >= 1, "The tampering statement changed no row.");
        }
        finally
        {
            foreach (var (_, definition) in triggers)
            {
                await ExecuteAsync(db, definition);
            }

            await ExecuteAsync(db, "PRAGMA foreign_keys = ON;");
        }
    }

    private static async Task ExecuteAsync(DevalCopilotDbContext db, string statement)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
#pragma warning disable CA2100 // A constant or a definition read from sqlite_master of the disposable test database.
        command.CommandText = statement;
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<Terminal> SeedTimedOutAsync(DevalCopilotDbContext db, Guid runId, bool witnessed, bool observed)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-5);
        var sequence = await db.LocalExecutionSequences.SingleAsync(candidate => candidate.RunId == runId);
        var operationId = Guid.NewGuid();
        var step = LocalExecutionSequenceStep.Reserve(
            Guid.NewGuid(), sequence.Id, 0, LocalExecutionStepKind.Implementation, 0, null, null, sequence.CheckpointId, sequence.FingerprintSha256,
            null, null, operationId, Guid.NewGuid(), now);
        step.MarkClaimed(operationId, now.AddSeconds(1), now.AddMinutes(30));
        db.LocalExecutionSequenceSteps.Add(step);
        await db.SaveChangesAsync();

        var attempt = Attempt.ClaimAgentImplementation(
            operationId, runId, 3, sequence.WorkspaceId, sequence.CheckpointId, sequence.FingerprintSha256, Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 262144, 524288, now.AddSeconds(1), 3);
        attempt.MarkAgentDispatched(now.AddSeconds(2));
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();

        Guid? observationId = null;
        if (witnessed)
        {
            var witness = LocalExecutionDispatchWitness.RecordAgent(
                Guid.NewGuid(), sequence, step, operationId, AgentResponseContract.ImplementationReport, null, now.AddSeconds(2), Guid.NewGuid(),
                Guid.NewGuid());
            db.LocalExecutionDispatchWitnesses.Add(witness);
            await db.SaveChangesAsync();
            if (observed)
            {
                var observation = LocalExecutionTerminationObservation.Record(
                    Guid.NewGuid(), witness, ProcessOutcome.TimedOut, null, TimeSpan.FromSeconds(5), 137, TimeSpan.FromSeconds(4), now.AddSeconds(8));
                db.LocalExecutionTerminationObservations.Add(observation);
                observationId = observation.Id;
            }
        }

        attempt.CompleteImplementation(
            AgentOutcome.ProviderInvocationFailed,
            null,
            now.AddSeconds(8),
            AgentProcessExecutionEvidence.Create(ProcessOutcome.TimedOut, null, TimeSpan.FromSeconds(5)));
        sequence.End(LocalExecutionSequenceState.NeedsAttention, LocalExecutionSequenceReason.ExitEvidenceMissing, detailCode: null, now.AddSeconds(9));
        await db.SaveChangesAsync();
        return new Terminal(sequence.Id, step.Id, attempt.Id, observationId);
    }
}
