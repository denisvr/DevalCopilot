using System.Data.Common;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequenceInspection;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionStepInspection;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionVerificationOutputWindow;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences.ScriptedResultKind;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionInspection;

/// <summary>
/// The reusable harness of the ADR-0061 inspection tests: the ADR-0060 real-file-backed-SQLite scene (whole cycles driven through the
/// production commands), the three real handlers over contexts of their own, a recording artifact store that proves exactly which
/// sealed paths were requested, and a command log that proves what the reads executed. Nothing here writes through an inspection
/// handler; the scene alone changes the world, exactly as another request would.
/// </summary>
internal sealed class LocalExecutionInspectionHarness
{
    public LocalExecutionInspectionHarness(LocalExecutionSequenceTestScene scene)
    {
        Scene = scene;
        Store = new RecordingArtifactStore(scene.Artifacts);
    }

    public LocalExecutionSequenceTestScene Scene { get; }

    public RecordingArtifactStore Store { get; }

    public static async Task<(LocalExecutionInspectionHarness Harness, Guid SequenceId)> ArmedAsync(
        SqliteDatabaseFixture fixture, int recipeCount = 2)
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount);
        var plan = await scene.AddAcceptedRootPlanAsync();
        var armed = await scene.ArmAsync(plan.ProposalId);
        return (new LocalExecutionInspectionHarness(scene), armed.SequenceId);
    }

    /// <summary>A whole cycle that records every one of the six step kinds: the first pass fails one recipe (diagnosis and its
    /// correction), the second pass passes and its review requests changes (review correction), the third pass passes and is approved.</summary>
    public static LocalExecutionStepScript AllKindsScript() => new LocalExecutionStepScript()
        .Verify(1, 0, VerificationFailed)
        .On(Step.CodeReview, 2, ReviewChangesRequested);

    public async Task<GetLocalExecutionSequenceInspectionQueryResult> IndexAsync(Guid? runId = null, params IInterceptor[] interceptors)
    {
        var result = await TryIndexAsync(runId, interceptors);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value;
    }

    public async Task<Result<GetLocalExecutionSequenceInspectionQueryResult>> TryIndexAsync(
        Guid? runId = null, params IInterceptor[] interceptors)
    {
        await using var context = Scene.Fixture.CreateContext(interceptors);
        var result = await new GetLocalExecutionSequenceInspectionQueryHandler(context).HandleAsync(
            new GetLocalExecutionSequenceInspectionQuery(runId ?? Scene.RunId), CancellationToken.None);
        Assert.Empty(context.ChangeTracker.Entries());
        return result;
    }

    public async Task<GetLocalExecutionStepInspectionQueryResult> StepAsync(Guid stepId, Guid? runId = null, params IInterceptor[] interceptors)
    {
        var result = await TryStepAsync(stepId, runId, interceptors);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value;
    }

    public async Task<Result<GetLocalExecutionStepInspectionQueryResult>> TryStepAsync(
        Guid stepId, Guid? runId = null, params IInterceptor[] interceptors)
    {
        await using var context = Scene.Fixture.CreateContext(interceptors);
        var result = await new GetLocalExecutionStepInspectionQueryHandler(context).HandleAsync(
            new GetLocalExecutionStepInspectionQuery(runId ?? Scene.RunId, stepId), CancellationToken.None);
        Assert.Empty(context.ChangeTracker.Entries());
        return result;
    }

    public async Task<GetLocalExecutionVerificationOutputWindowQueryResult> WindowAsync(
        Guid stepId,
        VerificationOutputPurpose stream,
        long offset = 0,
        int maxBytes = GetLocalExecutionVerificationOutputWindowQuery.DefaultMaxBytes,
        params IInterceptor[] interceptors)
    {
        var result = await TryWindowAsync(stepId, stream, offset, maxBytes, null, interceptors);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value;
    }

    public async Task<Result<GetLocalExecutionVerificationOutputWindowQueryResult>> TryWindowAsync(
        Guid stepId,
        VerificationOutputPurpose stream,
        long offset = 0,
        int maxBytes = GetLocalExecutionVerificationOutputWindowQuery.DefaultMaxBytes,
        Guid? runId = null,
        params IInterceptor[] interceptors)
    {
        await using var context = Scene.Fixture.CreateContext(interceptors);
        var result = await new GetLocalExecutionVerificationOutputWindowQueryHandler(context, Store).HandleAsync(
            new GetLocalExecutionVerificationOutputWindowQuery(runId ?? Scene.RunId, stepId, stream, offset, maxBytes),
            CancellationToken.None);
        Assert.Empty(context.ChangeTracker.Entries());
        return result;
    }

    /// <summary>Every durable fact the inspection could disturb, as one comparable value: row counts of the events, messages, attempts,
    /// executions, outputs and steps, and the sequence and step revisions with their update times.</summary>
    public async Task<string> ChangeFingerprintAsync()
    {
        await using var context = Scene.Fixture.CreateContext();
        var events = await context.Events.AsNoTracking().CountAsync();
        var messages = await context.CollaborationMessages.AsNoTracking().CountAsync();
        var attempts = await context.Attempts.AsNoTracking().CountAsync();
        var executions = await context.VerificationExecutions.AsNoTracking().CountAsync();
        var outputs = await context.VerificationOutputArtifacts.AsNoTracking().CountAsync();
        var sequences = await context.LocalExecutionSequences.AsNoTracking()
            .Select(sequence => sequence.Revision + ":" + sequence.State).ToListAsync();
        var steps = (await context.LocalExecutionSequenceSteps.AsNoTracking().ToListAsync())
            .OrderBy(step => step.Ordinal).Select(step => $"{step.Revision}:{step.State}").ToList();
        return $"{events}|{messages}|{attempts}|{executions}|{outputs}|{string.Join(',', sequences)}|{string.Join(',', steps)}";
    }

    /// <summary>Writes damaged storage through a connection of its own (unpooled, so no pragma outlives it) with check constraints and
    /// foreign keys off and the named immutability triggers dropped: the only way to hold rows the Domain refuses to record. The text may
    /// hold several statements; every value is bound with its own storage class.</summary>
    public async Task CorruptAsync(string sql, object?[]? values = null, params string[] triggersToDrop)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(Scene.Fixture.ConnectionString + ";Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        var drops = string.Concat(triggersToDrop.Select(trigger => $"DROP TRIGGER IF EXISTS {trigger};"));
#pragma warning disable CA2100 // Test-owned statement text; every value is bound as a parameter.
        command.CommandText = "PRAGMA ignore_check_constraints = ON; PRAGMA foreign_keys = OFF; " + drops + sql;
#pragma warning restore CA2100
        for (var index = 0; index < (values?.Length ?? 0); index++)
        {
            command.Parameters.AddWithValue($"$p{index}", values![index] ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync();
    }

    public const string SequenceImmutable = "trg_local_execution_sequence_immutable";
    public const string StepImmutable = "trg_local_execution_step_immutable";
    public const string RecipeImmutable = "trg_local_execution_recipe_immutable";

    /// <summary>The recorded step of a driven cycle by its ordinal.</summary>
    public async Task<LocalExecutionSequenceStep> RowAsync(int ordinal) =>
        (await Scene.ReadStepsAsync()).Single(step => step.Ordinal == ordinal);

    /// <summary>The recorded step of a driven cycle by its kind and, for a verification, its pass and recipe.</summary>
    public async Task<LocalExecutionSequenceStep> RowAsync(Step kind, int? pass = null, int? recipe = null) =>
        (await Scene.ReadStepsAsync()).First(step => step.Kind == kind
            && (pass is null || step.PassNumber == pass)
            && (recipe is null || step.RecipeOrdinal == recipe));

    /// <summary>The sealed stream rows of an execution, as recorded by the real result recorder.</summary>
    public async Task<List<VerificationOutputArtifact>> OutputsAsync(Guid executionId)
    {
        await using var context = Scene.Fixture.CreateContext();
        return await context.VerificationOutputArtifacts.AsNoTracking()
            .Where(output => output.VerificationExecutionId == executionId).ToListAsync();
    }
}
