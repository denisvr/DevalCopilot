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
/// The shared file-backed SQLite harness of the ADR-0060 persistence tests: it applies the real production migrations, seeds a coherent
/// run/workspace/checkpoint/plan scene through the production entities and writes raw SQL through independent connections, so the
/// real constraints, indexes and write-seam triggers decide every outcome.
/// </summary>
public abstract class LocalExecutionSequenceMigrationTestBase : IAsyncLifetime
{
    protected const string PriorMigration = "20261020170119_AddExecutionCapacityConfiguration";
    protected const string SequenceMigration = "20261020215343_AddLocalExecutionSequence";
    protected const string QueueMigration = "20261022152738_AddLocalExecutionQueue";
    protected const string BlockedMessage = "local_execution_sequence.manual_control_blocked";
    protected const string NotEligibleMessage = "local_execution_sequence.run_not_eligible";
    protected const string TupleMessage = "local_execution_sequence.tuple_incoherent";
    protected const string ImmutableMessage = "local_execution_sequence.immutable";
    protected const string BoundMessage = "local_execution_sequence.bound_exceeded";

    protected static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
    protected static readonly string Fingerprint = new('a', 64);
    protected static string[] TriggerNames => SequenceTriggerNames
        .Concat(QueueTriggerNames)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();

    /// <summary>The ADR-0062 triggers: the queue bound, the admission lifecycle and order guards.</summary>
    protected static readonly string[] QueueTriggerNames =
    [
        "trg_local_execution_admission_immutable",
        "trg_local_execution_admission_insert_bound",
        "trg_local_execution_admission_insert_shape",
        "trg_local_execution_counter_monotonic",
        "trg_local_execution_counter_retained",
        "trg_local_execution_queue_bound",
        "trg_local_execution_step_admission",
    ];

    /// <summary>The ADR-0060 triggers.</summary>
    protected static readonly string[] SequenceTriggerNames =
    [
        "trg_local_execution_guard_arming",
        "trg_local_execution_guard_attempts",
        "trg_local_execution_guard_checkpoints",
        "trg_local_execution_guard_local_commit_operations",
        "trg_local_execution_guard_run_abandonment",
        "trg_local_execution_guard_run_settings",
        "trg_local_execution_guard_tuple",
        "trg_local_execution_guard_verification_commands_delete",
        "trg_local_execution_guard_verification_commands_insert",
        "trg_local_execution_guard_verification_commands_update",
        "trg_local_execution_guard_verification_executions",
        "trg_local_execution_recipe_immutable",
        "trg_local_execution_sequence_immutable",
        "trg_local_execution_step_bounds",
        "trg_local_execution_step_immutable",
    ];

    protected string DatabasePath => databasePath;

    private readonly string databasePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-local-execution-sequence-migration-{Guid.NewGuid():N}.db");

    private int attemptNumber;
    private int executionNumber;
    private int checkpointNumber = 1;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={databasePath}"));
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }

        return Task.CompletedTask;
    }

    protected sealed record Scene(
        Run Run,
        Project Project,
        GitWorkspace Workspace,
        GitCheckpoint Checkpoint,
        Guid PlanMessageId,
        Guid PlannerAttemptId,
        IReadOnlyList<VerificationCommand> Commands);

    protected DevalCopilotDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={databasePath}").Options);

    protected async Task MigrateToAsync(string? target)
    {
        await using var context = CreateContext();
        await context.Database.GetService<IMigrator>().MigrateAsync(target);
    }

    protected async Task<List<string>> ReadNamesAsync(string type, string? like = null)
    {
        await using var probe = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await probe.OpenAsync();
        await using var command = probe.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = $type AND name LIKE $like ORDER BY name;";
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$like", like ?? "%");
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    protected async Task ExecuteAsync(string sql, bool foreignKeys = true)
    {
        await using var probe = new SqliteConnection($"Data Source={databasePath};Foreign Keys={foreignKeys};Pooling=False");
        await probe.OpenAsync();
        await using var command = probe.CreateCommand();
#pragma warning disable CA2100 // Test-only SQL assembled from constants and identifiers this test generated.
        command.CommandText = sql;
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync();
    }

    protected async Task<long> CountAsync(string sql)
    {
        await using var probe = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await probe.OpenAsync();
        await using var command = probe.CreateCommand();
#pragma warning disable CA2100 // Test-only SQL assembled from constants and identifiers this test generated.
        command.CommandText = sql;
#pragma warning restore CA2100
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    protected static string FirstMessage(Exception exception) => exception.GetBaseException().Message;

    protected static string Q(Guid id) => $"'{id.ToString().ToUpperInvariant()}'";

    protected static string Quote(string value) => $"'{value}'";

    protected int NextAttemptNumber() => ++attemptNumber;

    protected async Task<Scene> SeedSceneAsync(DevalCopilotDbContext context, int runNumber = 1, int commandCount = 2, bool running = true)
    {
        var project = Project.Register(Guid.NewGuid(), "Project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordClassifiedIntent(Guid.NewGuid(), project.Id, runNumber, RunExecutionMode.ManualAgent, "Objective", Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), Now, new string('a', 40), Fingerprint, []);
        var commands = Enumerable.Range(1, commandCount)
            .Select(number => VerificationCommand.Configure(
                Guid.NewGuid(), project.Id, number, $"Check {number}", @"C:\tools\dotnet.exe", ["test", number.ToString(System.Globalization.CultureInfo.InvariantCulture)],
                120, true, Now))
            .ToList();
        context.Projects.Add(project);
        context.Runs.Add(run);
        context.GitWorkspaces.Add(workspace);
        context.GitCheckpoints.Add(checkpoint);
        context.VerificationCommands.AddRange(commands);

        var planner = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, NextAttemptNumber(), workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 1024, 2048, Now, agentBudgetSlot: 1);
        planner.CompletePlanningSequenceDispatchRefused(Now);
        context.Attempts.Add(planner);
        var proposal = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, planner.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex),
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal, null,
            "Plan.", "{\"scope\":\"s\",\"implementationSteps\":\"i\",\"risks\":\"r\",\"verificationPlan\":\"v\",\"escalationPoints\":\"e\"}",
            CollaborationMessageProvenance.ProviderObserved, Now);
        context.CollaborationMessages.Add(proposal);
        await context.SaveChangesAsync();

        if (running)
        {
            run.Claim(Now);
            await context.SaveChangesAsync();
        }

        return new Scene(run, project, workspace, checkpoint, proposal.Id, planner.Id, commands);
    }

    protected async Task<GitCheckpoint> AddCheckpointAsync(DevalCopilotDbContext context, Scene scene)
    {
        var checkpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), scene.Workspace.Id, 100 + ++checkpointNumber, Now, new string('a', 40), new string('d', 64), []);
        context.GitCheckpoints.Add(checkpoint);
        await context.SaveChangesAsync();
        return checkpoint;
    }

    protected static LocalExecutionSequence Arm(Scene scene, int recipeCount = 2, int allowance = 2) => LocalExecutionSequence.Arm(
        Guid.NewGuid(), scene.Run.Id, scene.Run.ProjectId, scene.Workspace.Id, scene.Checkpoint.Id, Fingerprint, scene.PlanMessageId,
        LocalExecutionPlanAuthority.AcceptedRoot, new string('b', 64), new string('c', 64), "v1;settings", recipeCount, allowance, Now,
        NextCoordination());

    private static long coordinationNumber;

    protected static long NextCoordination() => Interlocked.Increment(ref coordinationNumber);

    /// <summary>Adds an armed sequence and its pinned recipes (one per requested recipe, in scene command order).</summary>
    protected static LocalExecutionSequence AddArmed(DevalCopilotDbContext context, Scene scene, int recipeCount = 2, int allowance = 2)
    {
        var sequence = Arm(scene, recipeCount, allowance);
        context.LocalExecutionSequences.Add(sequence);
        for (var ordinal = 0; ordinal < recipeCount; ordinal++)
        {
            context.LocalExecutionSequenceRecipes.Add(
                LocalExecutionSequenceRecipe.Pin(Guid.NewGuid(), sequence.Id, ordinal, scene.Commands[ordinal]));
        }

        return sequence;
    }

    protected static LocalExecutionSequenceStep ImplementationStep(Guid sequenceId, Scene scene, Guid? operationId = null) =>
        LocalExecutionSequenceStep.Reserve(
            Guid.NewGuid(), sequenceId, 0, LocalExecutionStepKind.Implementation, 0, null, null, scene.Checkpoint.Id, Fingerprint, null,
            null, operationId ?? Guid.NewGuid(), Guid.NewGuid(), Now);

    protected static LocalExecutionSequenceStep VerificationStep(
        Guid sequenceId, Scene scene, Guid predecessor, int ordinal = 1, int pass = 1, int recipe = 0, Guid? operationId = null) =>
        LocalExecutionSequenceStep.Reserve(
            Guid.NewGuid(), sequenceId, ordinal, LocalExecutionStepKind.Verification, pass, recipe, predecessor, scene.Checkpoint.Id,
            Fingerprint, scene.PlanMessageId, null, operationId ?? Guid.NewGuid(), Guid.NewGuid(), Now);

    protected static LocalExecutionSequenceStep ReviewStep(
        Guid sequenceId, Scene scene, Guid predecessor, int ordinal = 3, int pass = 1, Guid? operationId = null) =>
        LocalExecutionSequenceStep.Reserve(
            Guid.NewGuid(), sequenceId, ordinal, LocalExecutionStepKind.CodeReview, pass, null, predecessor, scene.Checkpoint.Id,
            Fingerprint, scene.PlanMessageId, null, operationId ?? Guid.NewGuid(), Guid.NewGuid(), Now);

    protected static LocalExecutionSequenceStep CorrectionStep(
        Guid sequenceId, Scene scene, Guid predecessor, int ordinal = 4, int pass = 1, Guid? operationId = null) =>
        LocalExecutionSequenceStep.Reserve(
            Guid.NewGuid(), sequenceId, ordinal, LocalExecutionStepKind.ReviewCorrection, pass, null, predecessor, scene.Checkpoint.Id,
            Fingerprint, scene.PlanMessageId, scene.PlannerAttemptId, operationId ?? Guid.NewGuid(), Guid.NewGuid(), Now);

    protected static void Claim(LocalExecutionSequenceStep step) =>
        step.MarkClaimed(step.ReservedOperationId, Now.AddSeconds(10), Now.AddMinutes(30));

    protected Attempt ImplementationAttempt(Scene scene, Guid id) => Attempt.ClaimAgentImplementation(
        id, scene.Run.Id, NextAttemptNumber(), scene.Workspace.Id, scene.Checkpoint.Id, Fingerprint, Guid.NewGuid(),
        TimeSpan.FromMinutes(20), 1024, 2048, Now, agentBudgetSlot: attemptNumber + 1);

    protected Attempt ReviewAttempt(Scene scene, Guid id) => Attempt.ClaimAgentCodeReview(
        id, scene.Run.Id, NextAttemptNumber(), scene.Workspace.Id, scene.Checkpoint.Id, Fingerprint, Guid.NewGuid(),
        TimeSpan.FromMinutes(10), 1024, 2048, Now, agentBudgetSlot: attemptNumber + 1);

    protected Attempt PlannerAttempt(Scene scene, Guid id) => Attempt.ClaimAgent(
        id, scene.Run.Id, NextAttemptNumber(), scene.Workspace.Id, scene.Checkpoint.Id, Fingerprint, Guid.NewGuid(),
        TimeSpan.FromMinutes(10), 1024, 2048, Now, agentBudgetSlot: attemptNumber + 1);

    protected VerificationExecution Execution(Scene scene, Guid id, int command = 0) => VerificationExecution.Claim(
        id, scene.Project.Id, ++executionNumber, scene.Workspace, scene.Checkpoint, scene.Commands[command], Now);

    protected static string Sql(DateTimeOffset value) => $"'{value:yyyy-MM-dd HH:mm:ss}'";

    protected static string Insert(string table, IReadOnlyDictionary<string, string> values) =>
        $"INSERT INTO {table} ({string.Join(", ", values.Keys.Select(key => $"\"{key}\""))}) VALUES ({string.Join(", ", values.Values)});";

    protected static Dictionary<string, string> RawSequence(Scene scene, Guid? id = null) => new()
    {
        ["Id"] = Q(id ?? Guid.NewGuid()),
        ["RunId"] = Q(scene.Run.Id),
        ["ProjectId"] = Q(scene.Run.ProjectId),
        ["WorkspaceId"] = Q(scene.Workspace.Id),
        ["CheckpointId"] = Q(scene.Checkpoint.Id),
        ["FingerprintSha256"] = Quote(Fingerprint),
        ["PlanProposalMessageId"] = Q(scene.PlanMessageId),
        ["PlanAuthority"] = "'AcceptedRoot'",
        ["PlanInputsSha256"] = Quote(new string('b', 64)),
        ["ConfirmationSha256"] = Quote(new string('c', 64)),
        ["SettingsSnapshot"] = "'v1;settings'",
        ["RecipeCount"] = "2",
        ["CorrectionAllowance"] = "2",
        ["AdmissionMode"] = "'Immediate'",
        ["CoordinationNumber"] = NextCoordination().ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["State"] = "'Armed'",
        ["ReasonCode"] = "NULL",
        ["DetailCode"] = "NULL",
        ["Revision"] = "1",
        ["ArmedAtUtc"] = Sql(Now),
        ["AdvancementDeadlineUtc"] = Sql(Now.AddHours(2)),
        ["UpdatedAtUtc"] = Sql(Now),
        ["StopRequestedAtUtc"] = "NULL",
        ["EndedAtUtc"] = "NULL",
        ["FinalReportMessageId"] = "NULL",
        ["FinalReviewAttemptId"] = "NULL",
        ["FinalEscalationMessageId"] = "NULL",
    };

    protected static Dictionary<string, string> RawRecipe(Guid sequenceId, int ordinal = 0) => new()
    {
        ["Id"] = Q(Guid.NewGuid()),
        ["SequenceId"] = Q(sequenceId),
        ["Ordinal"] = ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["VerificationCommandId"] = Q(Guid.NewGuid()),
        ["CommandNumber"] = (ordinal + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["CommandName"] = "'Check'",
        ["ExecutablePath"] = "'C:\\tools\\dotnet.exe'",
        ["ArgumentsJson"] = "'[]'",
        ["TimeoutSeconds"] = "120",
        ["IsEnabled"] = "1",
        ["ConfiguredAtUtc"] = Sql(Now),
        ["UpdatedAtUtc"] = Sql(Now),
    };

    /// <summary>A raw Revoked step of the given kind with a valid shape; overrides apply on top.</summary>
    protected static Dictionary<string, string> RawStep(
        Scene scene, Guid sequenceId, int ordinal, LocalExecutionStepKind kind = LocalExecutionStepKind.CodeReview, Guid? predecessor = null)
    {
        var isImplementation = kind == LocalExecutionStepKind.Implementation;
        var isVerification = kind == LocalExecutionStepKind.Verification;
        var isCorrection = LocalExecutionSequencePolicy.IsCorrectionKind(kind);
        var reserved = Guid.NewGuid();
        return new Dictionary<string, string>
        {
            ["Id"] = Q(Guid.NewGuid()),
            ["SequenceId"] = Q(sequenceId),
            ["Ordinal"] = ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Kind"] = Quote(kind.ToString()),
            ["PassNumber"] = isImplementation ? "0" : "1",
            ["RecipeOrdinal"] = isVerification ? "0" : "NULL",
            ["PredecessorStepId"] = isImplementation ? "NULL" : Q(predecessor ?? Guid.NewGuid()),
            ["SourceCheckpointId"] = Q(scene.Checkpoint.Id),
            ["SourceFingerprintSha256"] = Quote(Fingerprint),
            ["ReportMessageId"] = isImplementation ? "NULL" : Q(scene.PlanMessageId),
            ["SubjectAttemptId"] = isCorrection ? Q(scene.PlannerAttemptId) : "NULL",
            ["State"] = "'Revoked'",
            ["ReservedOperationId"] = Q(reserved),
            ["OperationId"] = "NULL",
            ["PreparationOwnerToken"] = Q(Guid.NewGuid()),
            ["PreparationReservedAtUtc"] = Sql(Now),
            ["PreparationDeadlineUtc"] = Sql(Now.AddMinutes(2)),
            ["ClaimedAtUtc"] = "NULL",
            ["ResultDeadlineUtc"] = "NULL",
            ["ReasonCode"] = "'stopped_before_claim'",
            ["Revision"] = "1",
            ["UpdatedAtUtc"] = Sql(Now),
        };
    }

    /// <summary>Turns a raw step into a Concluded one linked to its own reserved operation.</summary>
    protected static Dictionary<string, string> Concluded(Dictionary<string, string> step)
    {
        step["State"] = "'Concluded'";
        step["OperationId"] = step["ReservedOperationId"];
        step["ClaimedAtUtc"] = Sql(Now.AddSeconds(10));
        step["ResultDeadlineUtc"] = Sql(Now.AddMinutes(30));
        step["ReasonCode"] = "NULL";
        return step;
    }

    /// <summary>Tries to abandon the run on an independent context; true when the database refused it as blocked.</summary>
    protected async Task<bool> IsRunHeldAsync(Guid runId)
    {
        await using var context = CreateContext();
        var run = await context.Runs.SingleAsync(candidate => candidate.Id == runId);
        run.Abandon("Stop here.", Now.AddHours(1));
        try
        {
            await context.SaveChangesAsync();
            return false;
        }
        catch (DbUpdateException exception) when (FirstMessage(exception).Contains(BlockedMessage, StringComparison.Ordinal))
        {
            return true;
        }
    }
}
