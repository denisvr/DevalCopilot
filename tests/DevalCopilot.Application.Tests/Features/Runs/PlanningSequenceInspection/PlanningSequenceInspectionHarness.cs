using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Queries.GetPlanningSequenceInspection;
using DevalCopilot.Application.Features.Runs.Queries.GetPlanningStepInspection;
using DevalCopilot.Application.Tests.Features.Runs.PlanningSequences;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.PlanningSequenceInspection;

/// <summary>
/// The reusable harness of the ADR-0068 inspection tests: the ADR-0055 real-file-backed-SQLite scene (whole sequences driven through the
/// production arming, observation, claim and stop handlers and the Domain transitions a provider result produces), the two real
/// handlers over contexts of their own, and the command log and fingerprint that prove what a read executed and disturbed. Nothing
/// here writes through an inspection handler; the scene alone changes the world, exactly as another request would.
/// </summary>
internal sealed class PlanningSequenceInspectionHarness
{
    public const string SequenceTable = "planning_sequences";
    public const string StepTable = "planning_sequence_steps";
    public const string SequenceImmutable = "trg_planning_sequence_immutable";
    public const string StepImmutable = "trg_planning_sequence_step_immutable";

    public PlanningSequenceInspectionHarness(PlanningSequenceTestScene scene, Guid sequenceId)
    {
        Scene = scene;
        SequenceId = sequenceId;
    }

    public PlanningSequenceTestScene Scene { get; }

    public Guid SequenceId { get; }

    public static async Task<PlanningSequenceInspectionHarness> ArmedAsync(SqliteDatabaseFixture fixture)
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        return new PlanningSequenceInspectionHarness(scene, armed.SequenceId);
    }

    /// <summary>Reserves, claims and completes the next step with the given result, returning its attempt.</summary>
    public async Task<Guid> RunStepAsync(StepResult result, int challenges = 1)
    {
        var (_, claim) = await Scene.ReserveAndClaimAsync(SequenceId);
        await Scene.CompleteStepAsync(claim.AttemptId, result, challenges);
        return claim.AttemptId;
    }

    /// <summary>The first <paramref name="count"/> of the five possible steps of the two-round path (Planner, Challenged review,
    /// Resolver, Challenged review, Resolver). Each next reservation is the observation that concludes its predecessor, so every step
    /// but the last is Concluded and the last is Claimed with its completed attempt, until one more observation records the outcome.</summary>
    public async Task<List<Guid>> DriveStepsAsync(int count)
    {
        StepResult[] script = [StepResult.Proposed, StepResult.Challenged, StepResult.Resolved, StepResult.Challenged, StepResult.Resolved];
        var attempts = new List<Guid>();
        for (var index = 0; index < count; index++)
        {
            attempts.Add(await RunStepAsync(script[index], challenges: index == 1 ? 2 : 1));
        }

        return attempts;
    }

    public async Task<GetPlanningSequenceInspectionQueryResult> IndexAsync(Guid? runId = null, params IInterceptor[] interceptors)
    {
        var result = await TryIndexAsync(runId, interceptors);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value;
    }

    public async Task<Result<GetPlanningSequenceInspectionQueryResult>> TryIndexAsync(
        Guid? runId = null, params IInterceptor[] interceptors)
    {
        await using var context = Scene.Fixture.CreateContext(interceptors);
        var result = await new GetPlanningSequenceInspectionQueryHandler(context).HandleAsync(
            new GetPlanningSequenceInspectionQuery(runId ?? Scene.RunId), CancellationToken.None);
        Assert.Empty(context.ChangeTracker.Entries());
        return result;
    }

    public async Task<GetPlanningStepInspectionQueryResult> StepAsync(Guid stepId, Guid? runId = null, params IInterceptor[] interceptors)
    {
        var result = await TryStepAsync(stepId, runId, interceptors);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value;
    }

    public async Task<Result<GetPlanningStepInspectionQueryResult>> TryStepAsync(
        Guid stepId, Guid? runId = null, params IInterceptor[] interceptors)
    {
        await using var context = Scene.Fixture.CreateContext(interceptors);
        var result = await new GetPlanningStepInspectionQueryHandler(context).HandleAsync(
            new GetPlanningStepInspectionQuery(runId ?? Scene.RunId, stepId), CancellationToken.None);
        Assert.Empty(context.ChangeTracker.Entries());
        return result;
    }

    /// <summary>The identity of every recorded step by ordinal.</summary>
    public async Task<List<PlanningSequenceStep>> RowsAsync() => await Scene.ReadStepsAsync(SequenceId);

    /// <summary>Every durable fact an inspection could disturb, as one comparable value: row counts of the events, messages, attempts,
    /// and steps, and the sequence and step revisions, states and update times. It deliberately does not claim whole-database identity.</summary>
    public async Task<string> ChangeFingerprintAsync()
    {
        await using var context = Scene.Fixture.CreateContext();
        var events = await context.Events.AsNoTracking().CountAsync();
        var messages = await context.CollaborationMessages.AsNoTracking().CountAsync();
        var attempts = await context.Attempts.AsNoTracking().CountAsync();
        var inputs = await context.AttemptInputMessages.AsNoTracking().CountAsync();
        var sequences = (await context.PlanningSequences.AsNoTracking().ToListAsync())
            .Select(sequence => $"{sequence.Revision}:{sequence.State}:{sequence.UpdatedAtUtc:O}").ToList();
        var steps = (await context.PlanningSequenceSteps.AsNoTracking().ToListAsync())
            .OrderBy(step => step.Ordinal).Select(step => $"{step.Revision}:{step.State}:{step.UpdatedAtUtc:O}").ToList();
        return $"{events}|{messages}|{attempts}|{inputs}|{string.Join(',', sequences)}|{string.Join(',', steps)}";
    }

    /// <summary>Writes damaged storage through a connection of its own (unpooled, so no pragma outlives it) with check constraints and
    /// foreign keys off and the named triggers dropped: the only way to hold rows the Domain refuses to record. The text may hold several
    /// statements; every value is bound with its own storage class.</summary>
    public async Task CorruptAsync(string sql, object?[]? values = null, params string[] triggersToDrop)
    {
        await using var connection = new SqliteConnection(Scene.Fixture.ConnectionString + ";Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        var drops = string.Concat(triggersToDrop.Select(trigger => $"DROP TRIGGER IF EXISTS {trigger};"));
        if (triggersToDrop.Length == 0)
        {
            // No named trigger: the damaged value is written beside every guard of this disposable database, so a guard of another
            // table (such as an attempt identity guard) never decides which stored fact a case reaches.
            await using var listing = connection.CreateCommand();
            listing.CommandText = "SELECT name FROM sqlite_master WHERE type = 'trigger'";
            var names = new List<string>();
            await using (var reader = await listing.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    names.Add(reader.GetString(0));
                }
            }

            drops = string.Concat(names.Select(name => $"DROP TRIGGER IF EXISTS \"{name}\";"));
        }

#pragma warning disable CA2100 // Test-owned statement text; every value is bound as a parameter.
        command.CommandText = "PRAGMA ignore_check_constraints = ON; PRAGMA foreign_keys = OFF; " + drops + sql;
#pragma warning restore CA2100
        for (var index = 0; index < (values?.Length ?? 0); index++)
        {
            command.Parameters.AddWithValue($"$p{index}", values![index] ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync();
    }
}
