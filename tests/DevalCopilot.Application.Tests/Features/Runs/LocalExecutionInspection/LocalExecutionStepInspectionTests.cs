using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequenceInspection;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionStepInspection;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionInspection.LocalExecutionInspectionHarness;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences.ScriptedResultKind;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionInspection;

/// <summary>The selected operation of one recorded step (ADR-0061): the fixed 404 for an unknown run, a run without a sequence and a step
/// outside the run's sequence; every step kind and link state with the exact immutable Agent header or the proved verification and its
/// two sealed stream descriptors; the coherent no-link shape; and Unavailable, for that selected step only, for every contradictory
/// operation. Metadata discloses no path, executable, argument or hash and reads no artifact byte.</summary>
public sealed class LocalExecutionStepInspectionTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<(LocalExecutionInspectionHarness Harness, Guid SequenceId)> CompletedAsync(int recipeCount = 2)
    {
        var (harness, sequenceId) = await ArmedAsync(fixture, recipeCount);
        await harness.Scene.DriveAsync(sequenceId, AllKindsScript());
        return (harness, sequenceId);
    }

    [Fact]
    public async Task An_unknown_run_a_run_without_a_sequence_and_a_step_of_another_runs_sequence_are_one_fixed_not_found()
    {
        var (harness, _) = await CompletedAsync();
        var step = (await harness.RowAsync(3)).Id;
        var other = await LocalExecutionSequenceTestScene.CreateAsync(fixture);

        var unknownRun = await harness.TryStepAsync(step, Guid.NewGuid());
        var noSequence = await harness.TryStepAsync(step, other.RunId);
        var unknownStep = await harness.TryStepAsync(Guid.NewGuid());
        var emptyStep = await harness.TryStepAsync(Guid.Empty);

        foreach (var result in new[] { unknownRun, noSequence, unknownStep, emptyStep })
        {
            Assert.True(result.IsFailure);
            Assert.Equal(LocalExecutionInspectionErrors.NotFoundCode, result.Errors[0].Code);
        }

        // A step of another run's armed sequence is outside this run's sequence, too.
        var sibling = await ArmedAsync(fixture);
        var foreign = await sibling.Harness.Scene.ObserveReservingAsync(sibling.SequenceId);
        var crossed = await harness.TryStepAsync(foreign.StepId);
        Assert.Equal(LocalExecutionInspectionErrors.NotFoundCode, crossed.Errors[0].Code);
        var own = await sibling.Harness.StepAsync(foreign.StepId);
        Assert.Equal(LocalExecutionStepInspectionStatus.NoOperation, own.Status);
    }

    [Fact]
    public async Task Every_agent_step_returns_its_exact_step_facts_and_only_the_immutable_attempt_header()
    {
        var (harness, sequenceId) = await CompletedAsync();
        var index = await harness.IndexAsync();
        var attempts = (await harness.Scene.ReadAttemptsAsync()).ToDictionary(attempt => attempt.Id);

        foreach (var member in index.Steps.Where(candidate => candidate.Kind != Step.Verification))
        {
            var inspected = await harness.StepAsync(member.StepId);

            Assert.Equal(LocalExecutionStepInspectionStatus.Recorded, inspected.Status);
            Assert.Equal(harness.Scene.RunId, inspected.RunId);
            Assert.Equal(sequenceId, inspected.SequenceId);
            Assert.Equal(member.StepId, inspected.StepId);
            Assert.Equal(member, inspected.Step);
            Assert.Null(inspected.Verification);
            var attempt = attempts[member.OperationId!.Value];
            var header = inspected.Agent!;
            Assert.Equal(attempt.Id, header.Id);
            Assert.Equal(attempt.AttemptNumber, header.AttemptNumber);
            Assert.Equal(attempt.AgentRole, header.Role);
            Assert.Equal(attempt.AgentProvider, header.Provider);
            Assert.Equal(attempt.AgentResponseContract, header.ResponseContract);
            Assert.Equal(LocalExecutionSequencePolicy.ContractOf(member.Kind), header.ResponseContract);
        }
    }

    [Fact]
    public async Task A_claimed_agent_operation_without_any_message_is_inspectable()
    {
        var (harness, sequenceId) = await ArmedAsync(fixture);
        var ticket = await harness.Scene.ReserveAndClaimAsync(sequenceId);

        var inspected = await harness.StepAsync(ticket.StepId);

        Assert.Equal(LocalExecutionStepInspectionStatus.Recorded, inspected.Status);
        Assert.Equal(ticket.ReservedOperationId, inspected.Agent!.Id);
        Assert.Equal(AgentRole.Implementer, inspected.Agent.Role);
        Assert.Equal(AgentResponseContract.ImplementationReport, inspected.Agent.ResponseContract);
        Assert.Equal(LocalExecutionSequenceStepState.Claimed, inspected.Step!.RecordedState);
    }

    [Fact]
    public async Task A_failed_verification_discloses_its_proved_facts_and_two_recorded_sealed_stream_descriptors()
    {
        var (harness, _) = await CompletedAsync();
        var row = await harness.RowAsync(1);
        var execution = await harness.Scene.ReadExecutionAsync(row.OperationId!.Value);
        var outputs = (await harness.OutputsAsync(execution.Id)).ToDictionary(output => output.Purpose);

        var inspected = await harness.StepAsync(row.Id);

        Assert.Equal(LocalExecutionStepInspectionStatus.Recorded, inspected.Status);
        Assert.Null(inspected.Agent);
        var verification = inspected.Verification!;
        var facts = verification.Facts;
        Assert.Equal(execution.Id, facts.ExecutionId);
        Assert.Equal(execution.ExecutionNumber, facts.ExecutionNumber);
        Assert.Equal("Verification 1", facts.CommandName);
        Assert.Equal(VerificationExecutionStatus.Failed, facts.Status);
        Assert.Equal(VerificationExecutionOutcome.Exited, facts.Outcome);
        Assert.Equal(1, facts.ExitCode);
        Assert.Equal(execution.ClaimedAtUtc, facts.ClaimedAtUtc);
        Assert.Equal(execution.DispatchedAtUtc, facts.DispatchedAtUtc);
        Assert.Equal(execution.CompletedAtUtc, facts.CompletedAtUtc);
        AssertRecorded(verification.StandardOutput, outputs[VerificationOutputPurpose.StandardOutput]);
        AssertRecorded(verification.StandardError, outputs[VerificationOutputPurpose.StandardError]);
    }

    private static void AssertRecorded(RecordedVerificationOutputDescriptor descriptor, VerificationOutputArtifact output)
    {
        Assert.Equal(RecordedVerificationOutputDescriptorStatus.Recorded, descriptor.Status);
        Assert.Equal(output.Id, descriptor.ArtifactId);
        Assert.Equal(output.ByteLength, descriptor.ByteLength);
        Assert.Equal(VerificationOutputCaptureOutcome.CapturedWithKnownTruncation, descriptor.CaptureOutcome);
        Assert.False(descriptor.Truncated);
    }

    [Fact]
    public async Task A_passed_verification_is_inspectable_with_both_streams_not_recorded()
    {
        var (harness, _) = await CompletedAsync();
        var row = await harness.RowAsync(2);

        var inspected = await harness.StepAsync(row.Id);

        var verification = inspected.Verification!;
        Assert.Equal(VerificationExecutionStatus.Passed, verification.Facts.Status);
        Assert.Equal(0, verification.Facts.ExitCode);
        Assert.Equal(RecordedVerificationOutputDescriptorStatus.NotRecorded, verification.StandardOutput.Status);
        Assert.Equal(RecordedVerificationOutputDescriptorStatus.NotRecorded, verification.StandardError.Status);
        Assert.Empty(harness.Store.Reads);
    }

    [Theory]
    [InlineData(VerificationTimedOut, VerificationExecutionStatus.TimedOut, VerificationExecutionOutcome.TimedOut)]
    [InlineData(VerificationSourceChanged, VerificationExecutionStatus.SourceChanged, VerificationExecutionOutcome.Exited)]
    public async Task A_timed_out_or_source_changed_verification_keeps_its_recorded_status_and_is_never_a_clean_exit(
        ScriptedResultKind result, VerificationExecutionStatus status, VerificationExecutionOutcome outcome)
    {
        var (harness, sequenceId) = await ArmedAsync(fixture);
        await harness.Scene.DriveAsync(sequenceId, new LocalExecutionStepScript().Verify(1, 0, result));
        var row = await harness.RowAsync(Step.Verification, 1, 0);

        var inspected = await harness.StepAsync(row.Id);

        Assert.Equal(LocalExecutionStepInspectionStatus.Recorded, inspected.Status);
        Assert.Equal(status, inspected.Verification!.Facts.Status);
        Assert.Equal(outcome, inspected.Verification.Facts.Outcome);
        Assert.NotEqual(VerificationExecutionStatus.Passed, inspected.Verification.Facts.Status);
    }

    [Fact]
    public async Task A_dispatched_verification_left_running_is_running_with_no_outcome_or_exit_code()
    {
        var (harness, sequenceId) = await ArmedAsync(fixture);
        await harness.Scene.DriveAsync(sequenceId, new LocalExecutionStepScript().Verify(1, 0, LeaveRunning));
        var row = await harness.RowAsync(Step.Verification, 1, 0);

        var inspected = await harness.StepAsync(row.Id);

        var facts = inspected.Verification!.Facts;
        Assert.Equal(VerificationExecutionStatus.Running, facts.Status);
        Assert.Null(facts.Outcome);
        Assert.Null(facts.ExitCode);
        Assert.NotNull(facts.DispatchedAtUtc);
        Assert.Null(facts.CompletedAtUtc);
    }

    [Fact]
    public async Task A_dispatch_refused_verification_is_a_refusal_with_no_process_facts()
    {
        var (harness, sequenceId) = await ArmedAsync(fixture);
        await harness.Scene.DriveAsync(sequenceId, new LocalExecutionStepScript().Verify(1, 0, LeaveUndispatched));
        var row = await harness.RowAsync(Step.Verification, 1, 0);
        await harness.Scene.StopAsync(sequenceId);
        harness.Scene.Clock.Advance(TimeSpan.FromHours(1));
        await harness.Scene.ObserveAsync(sequenceId);
        var refused = await harness.Scene.RecordVerificationDispatchRefusalAsync(row.OperationId!.Value);
        Assert.True(refused.IsSuccess, refused.IsFailure ? refused.Errors[0].Code : null);

        var inspected = await harness.StepAsync(row.Id);

        var facts = inspected.Verification!.Facts;
        Assert.Equal(VerificationExecutionStatus.LocalExecutionSequenceDispatchRefused, facts.Status);
        Assert.Null(facts.Outcome);
        Assert.Null(facts.ExitCode);
        Assert.Null(facts.DispatchedAtUtc);
        Assert.NotNull(facts.CompletedAtUtc);
    }

    [Fact]
    public async Task A_reserved_a_revoked_and_an_unresolved_step_are_the_coherent_no_operation_shape_with_no_payloads()
    {
        var (reservedHarness, reservedSequence) = await ArmedAsync(fixture);
        var reserved = await reservedHarness.Scene.ObserveReservingAsync(reservedSequence);

        var reservedResult = await reservedHarness.StepAsync(reserved.StepId);

        Assert.Equal(LocalExecutionStepInspectionStatus.NoOperation, reservedResult.Status);
        Assert.Null(reservedResult.Step);
        Assert.Null(reservedResult.Agent);
        Assert.Null(reservedResult.Verification);
        Assert.Equal(reserved.StepId, reservedResult.StepId);

        await reservedHarness.Scene.ResolveAsync(
            reserved, "execution_capacity.limit_reached", new ConfigurableLocalExecutionStepProbe(LocalExecutionStepProbeResult.NotClaimed));
        Assert.Equal(LocalExecutionStepInspectionStatus.NoOperation, (await reservedHarness.StepAsync(reserved.StepId)).Status);

        await using var other = new SqliteDatabaseFixture();
        await other.InitializeAsync();
        var (unresolvedHarness, unresolvedSequence) = await ArmedAsync(other);
        var unresolved = await unresolvedHarness.Scene.ObserveReservingAsync(unresolvedSequence);
        await unresolvedHarness.Scene.ResolveAsync(
            unresolved, "attempts.persistence_failed", new ConfigurableLocalExecutionStepProbe(LocalExecutionStepProbeResult.Unresolved));
        Assert.Equal(LocalExecutionStepInspectionStatus.NoOperation, (await unresolvedHarness.StepAsync(unresolved.StepId)).Status);
    }

    [Fact]
    public async Task Reading_a_step_changes_nothing_executes_only_selects_and_selects_no_path_hash_token_or_other_recipe_argument()
    {
        var (harness, _) = await CompletedAsync();
        var verification = await harness.RowAsync(1);
        var agent = await harness.RowAsync(7);
        var before = await harness.ChangeFingerprintAsync();

        var agentLog = new StatementLog();
        Assert.Equal(LocalExecutionStepInspectionStatus.Recorded, (await harness.StepAsync(agent.Id, null, agentLog)).Status);
        var verificationLog = new StatementLog();
        Assert.Equal(LocalExecutionStepInspectionStatus.Recorded, (await harness.StepAsync(verification.Id, null, verificationLog)).Status);

        Assert.Equal(before, await harness.ChangeFingerprintAsync());
        Assert.Empty(harness.Store.Reads);
        foreach (var log in new[] { agentLog, verificationLog })
        {
            log.AssertReadOnly();
            log.AssertNeverSelects(
                "\"PreparationOwnerToken\"", "\"SettingsSnapshot\"", "\"WorkspacePath\"", "\"RelativeStoragePath\"", "\"ContentHash\"",
                "\"StructuredContentJson\"", "\"Summary\"");
        }

        agentLog.AssertNeverSelects("\"ExecutablePath\"", "\"ArgumentsJson\"");
        var recipeReads = verificationLog.Statements.Where(statement => statement.Contains("\"ArgumentsJson\"", StringComparison.Ordinal)).ToList();
        Assert.Single(recipeReads);
        Assert.Contains("\"Ordinal\" = ", recipeReads[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Historical_operations_stay_inspectable_after_newer_recipe_edits_checkpoints_and_runs()
    {
        var (harness, _) = await CompletedAsync();
        var verification = await harness.RowAsync(1);
        var agent = await harness.RowAsync(7);
        var before = (await harness.StepAsync(verification.Id), await harness.StepAsync(agent.Id));

        var recipes = await harness.Scene.ReadRecipesAsync();
        await harness.Scene.EditRecipeAsync(recipes[0].Id, executablePath: @"C:\tools\renamed.exe", timeoutSeconds: 120);
        await harness.Scene.EditRecipeAsync(recipes[1].Id, enabled: false);
        await LocalExecutionSequenceTestScene.CreateAsync(fixture);

        Assert.Equal(before.Item1, await harness.StepAsync(verification.Id));
        Assert.Equal(before.Item2, await harness.StepAsync(agent.Id));
    }

    private static readonly Dictionary<string, string> AgentDamage = new()
    {
        ["attempt_row_deleted"] = "DELETE FROM attempts WHERE Id = {op}",
        ["attempt_of_another_run"] = "UPDATE attempts SET RunId = (SELECT Id FROM runs WHERE Id <> (SELECT RunId FROM attempts WHERE Id = {op}) LIMIT 1) WHERE Id = {op}",
        ["contract_of_another_kind"] = "UPDATE attempts SET AgentResponseContract = 'ImplementationReport' WHERE Id = {op}",
        ["contract_unknown"] = "UPDATE attempts SET AgentResponseContract = 'Bogus' WHERE Id = {op}",
        ["role_incoherent_with_contract"] = "UPDATE attempts SET AgentRole = 'Planner' WHERE Id = {op}",
        ["provider_unknown"] = "UPDATE attempts SET AgentProvider = 'Bogus' WHERE Id = {op}",
        ["status_unknown"] = "UPDATE attempts SET Status = 'Bogus' WHERE Id = {op}",
        ["workspace_differs"] = $"UPDATE attempts SET AgentGitWorkspaceId = {ForeignGuid} WHERE Id = {{op}}",
        ["checkpoint_differs"] = $"UPDATE attempts SET AgentGitCheckpointId = {ForeignGuid} WHERE Id = {{op}}",
        ["fingerprint_differs"] = "UPDATE attempts SET AgentCheckpointFingerprintSha256 = replace(AgentCheckpointFingerprintSha256, substr(AgentCheckpointFingerprintSha256, 1, 1), 'd') WHERE Id = {op}",
        ["fingerprint_oversized"] = "UPDATE attempts SET AgentCheckpointFingerprintSha256 = hex(zeroblob(100)) WHERE Id = {op}",
        ["fingerprint_hidden_nul"] = "UPDATE attempts SET AgentCheckpointFingerprintSha256 = 'a' || char(0) || hex(zeroblob(100)) WHERE Id = {op}",
        ["model_oversized_storage"] = "UPDATE attempts SET AgentRequestedModel = hex(zeroblob(300)) WHERE Id = {op}",
    };

    private const string ForeignGuid = "(SELECT lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-4000-8000-' || lower(hex(randomblob(6))))";

    public static TheoryData<string> AgentDamageNames => new(AgentDamage.Keys);

    [Theory]
    [MemberData(nameof(AgentDamageNames))]
    public async Task A_contradictory_agent_operation_is_unavailable_for_that_step_only(string name)
    {
        var (harness, sequenceId) = await CompletedAsync();
        await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var review = await harness.RowAsync(7);
        var diagnosis = await harness.RowAsync(3);
        var sql = AgentDamage[name].Replace("{op}", $"'{review.OperationId!.Value.ToString().ToUpperInvariant()}'", StringComparison.Ordinal);

        await harness.CorruptAsync(sql);

        var damaged = await harness.StepAsync(review.Id);
        Assert.Equal(LocalExecutionStepInspectionStatus.Unavailable, damaged.Status);
        Assert.Equal(sequenceId, damaged.SequenceId);
        Assert.Null(damaged.Step);
        Assert.Null(damaged.Agent);
        Assert.Null(damaged.Verification);
        Assert.Equal(LocalExecutionStepInspectionStatus.Recorded, (await harness.StepAsync(diagnosis.Id)).Status);
        Assert.Equal(LocalExecutionSequenceInspectionStatus.Recorded, (await harness.IndexAsync()).Status);
    }

    private static readonly Dictionary<string, (string Sql, string[] Triggers)> VerificationDamage = new()
    {
        ["exec_args_drift"] = ("UPDATE verification_executions SET Arguments = '[\"--check\",\"99\"]' WHERE Id = {op}", []),
        ["exec_args_reordered"] = ("UPDATE verification_executions SET Arguments = '[\"1\",\"--check\"]' WHERE Id = {op}", []),
        ["exec_args_extra_member"] = ("UPDATE verification_executions SET Arguments = '[\"--check\",\"1\",\"x\"]' WHERE Id = {op}", []),
        ["exec_args_dropped"] = ("UPDATE verification_executions SET Arguments = '' WHERE Id = {op}", []),
        ["exec_args_not_json"] = ("UPDATE verification_executions SET Arguments = 'not json' WHERE Id = {op}", []),
        ["exec_args_json_null"] = ("UPDATE verification_executions SET Arguments = 'null' WHERE Id = {op}", []),
        ["exec_args_non_string_member"] = ("UPDATE verification_executions SET Arguments = '[\"--check\",1]' WHERE Id = {op}", []),
        ["exec_args_null_member"] = ("UPDATE verification_executions SET Arguments = '[\"--check\",null]' WHERE Id = {op}", []),
        ["exec_args_over_2_mib"] = ("UPDATE verification_executions SET Arguments = '[\"' || hex(zeroblob(1100000)) || '\"]' WHERE Id = {op}", []),
        ["exec_args_hidden_nul"] = ("UPDATE verification_executions SET Arguments = '[\"--check\",\"1\"]' || char(0) || hex(zeroblob(1100000)) WHERE Id = {op}", []),
        ["exec_args_invalid_utf8"] = ("UPDATE verification_executions SET Arguments = CAST(X'5B22C328225D' AS TEXT) WHERE Id = {op}", []),
        ["exec_executable_differs"] = ("UPDATE verification_executions SET ExecutablePath = 'C:\\other.exe' WHERE Id = {op}", []),
        ["exec_executable_over_128_kib"] = ("UPDATE verification_executions SET ExecutablePath = hex(zeroblob(70000)) WHERE Id = {op}", []),
        ["exec_name_differs"] = ("UPDATE verification_executions SET CommandName = 'Renamed' WHERE Id = {op}", []),
        ["exec_timeout_differs"] = ("UPDATE verification_executions SET TimeoutSeconds = 5 WHERE Id = {op}", []),
        ["exec_timeout_text"] = ("UPDATE verification_executions SET TimeoutSeconds = 'x' WHERE Id = {op}", []),
        ["exec_command_differs"] = ($"UPDATE verification_executions SET VerificationCommandId = {ForeignGuid} WHERE Id = {{op}}", []),
        ["exec_project_foreign"] = ($"UPDATE verification_executions SET ProjectId = {ForeignGuid} WHERE Id = {{op}}", []),
        ["exec_workspace_foreign"] = ($"UPDATE verification_executions SET GitWorkspaceId = {ForeignGuid} WHERE Id = {{op}}", []),
        ["exec_checkpoint_foreign"] = ($"UPDATE verification_executions SET GitCheckpointId = {ForeignGuid} WHERE Id = {{op}}", []),
        ["exec_claim_fingerprint_differs"] = ($"UPDATE verification_executions SET CheckpointFingerprintSha256 = '{new string('d', 64)}' WHERE Id = {{op}}", []),
        ["exec_number_zero"] = ("UPDATE verification_executions SET ExecutionNumber = 0 WHERE Id = {op}", []),
        ["exec_number_text"] = ("UPDATE verification_executions SET ExecutionNumber = 'x' WHERE Id = {op}", []),
        ["exec_status_unknown"] = ("UPDATE verification_executions SET Status = 'Bogus' WHERE Id = {op}", []),
        ["exec_outcome_unknown"] = ("UPDATE verification_executions SET Outcome = 'Bogus' WHERE Id = {op}", []),
        ["exec_exit_real"] = ("UPDATE verification_executions SET ExitCode = 1.5 WHERE Id = {op}", []),
        ["exec_exit_text"] = ("UPDATE verification_executions SET ExitCode = 'x' WHERE Id = {op}", []),
        ["exec_failed_with_zero_exit"] = ("UPDATE verification_executions SET ExitCode = 0 WHERE Id = {op}", []),
        ["exec_marked_passed_with_nonzero_exit"] = ("UPDATE verification_executions SET Status = 'Passed' WHERE Id = {op}", []),
        ["exec_completed_before_dispatch"] = ("UPDATE verification_executions SET CompletedAtUtc = '2020-01-01 00:00:00+00:00' WHERE Id = {op}", []),
        ["exec_dispatched_before_claim"] = ("UPDATE verification_executions SET DispatchedAtUtc = '2020-01-01 00:00:00+00:00' WHERE Id = {op}", []),
        ["exec_completion_fingerprint_differs"] = ($"UPDATE verification_executions SET CompletionFingerprintSha256 = '{new string('d', 64)}' WHERE Id = {{op}}", []),
        ["exec_row_deleted"] = ("DELETE FROM verification_executions WHERE Id = {op}", []),
        ["recipe_args_differ"] = ("UPDATE local_execution_sequence_recipes SET ArgumentsJson = '[\"--check\",\"2\"]' WHERE Ordinal = 0", [RecipeImmutable]),
        ["recipe_args_not_json"] = ("UPDATE local_execution_sequence_recipes SET ArgumentsJson = 'not json' WHERE Ordinal = 0", [RecipeImmutable]),
        ["recipe_args_over_2_mib"] = ("UPDATE local_execution_sequence_recipes SET ArgumentsJson = '[\"' || hex(zeroblob(1100000)) || '\"]' WHERE Ordinal = 0", [RecipeImmutable]),
        ["recipe_executable_differs"] = ("UPDATE local_execution_sequence_recipes SET ExecutablePath = 'C:\\other.exe' WHERE Ordinal = 0", [RecipeImmutable]),
        ["recipe_executable_over_128_kib"] = ("UPDATE local_execution_sequence_recipes SET ExecutablePath = hex(zeroblob(70000)) WHERE Ordinal = 0", [RecipeImmutable]),
        ["recipe_timeout_differs"] = ("UPDATE local_execution_sequence_recipes SET TimeoutSeconds = 5 WHERE Ordinal = 0", [RecipeImmutable]),
        ["recipe_timeout_text"] = ("UPDATE local_execution_sequence_recipes SET TimeoutSeconds = 'x' WHERE Ordinal = 0", [RecipeImmutable]),
        ["recipe_command_differs"] = ($"UPDATE local_execution_sequence_recipes SET VerificationCommandId = {ForeignGuid} WHERE Ordinal = 0", [RecipeImmutable]),
        ["recipe_name_differs"] = ("UPDATE local_execution_sequence_recipes SET CommandName = 'Renamed' WHERE Ordinal = 0", [RecipeImmutable]),
        ["both_args_past_64_members"] = (
            "UPDATE local_execution_sequence_recipes SET ArgumentsJson = '[' || replace(replace(hex(zeroblob(65)), '00', '\"a\",'), ',', ',') || '\"z\"]' WHERE Ordinal = 0; "
            + "UPDATE verification_executions SET Arguments = (SELECT ArgumentsJson FROM local_execution_sequence_recipes WHERE Ordinal = 0) WHERE Id = {op}",
            [RecipeImmutable]),
        ["both_args_member_over_4096_bytes"] = (
            "UPDATE local_execution_sequence_recipes SET ArgumentsJson = '[\"' || replace(hex(zeroblob(2049)), '0', 'a') || '\"]' WHERE Ordinal = 0; "
            + "UPDATE verification_executions SET Arguments = (SELECT ArgumentsJson FROM local_execution_sequence_recipes WHERE Ordinal = 0) WHERE Id = {op}",
            [RecipeImmutable]),
    };

    public static TheoryData<string> VerificationDamageNames => new(VerificationDamage.Keys);

    [Theory]
    [MemberData(nameof(VerificationDamageNames))]
    public async Task A_contradictory_verification_is_unavailable_for_that_step_only_and_its_window_reads_nothing(string name)
    {
        var (harness, sequenceId) = await CompletedAsync();
        await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var target = await harness.RowAsync(1);
        var sibling = await harness.RowAsync(2);
        var (sql, triggers) = VerificationDamage[name];
        sql = sql.Replace("{op}", $"'{target.OperationId!.Value.ToString().ToUpperInvariant()}'", StringComparison.Ordinal);

        await harness.CorruptAsync(sql, null, triggers);

        var damaged = await harness.StepAsync(target.Id);
        Assert.Equal(LocalExecutionStepInspectionStatus.Unavailable, damaged.Status);
        Assert.Equal(sequenceId, damaged.SequenceId);
        Assert.Null(damaged.Step);
        Assert.Null(damaged.Agent);
        Assert.Null(damaged.Verification);
        foreach (var stream in new[] { VerificationOutputPurpose.StandardOutput, VerificationOutputPurpose.StandardError })
        {
            var window = await harness.WindowAsync(target.Id, stream);
            Assert.Equal(DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionVerificationOutputWindow
                .LocalExecutionVerificationOutputWindowStatus.Unavailable, window.Status);
            Assert.Null(window.ExecutionId);
        }

        Assert.Empty(harness.Store.Reads);
        Assert.Equal(LocalExecutionSequenceInspectionStatus.Recorded, (await harness.IndexAsync()).Status);
        if (!name.StartsWith("both_", StringComparison.Ordinal) && !name.StartsWith("recipe_name", StringComparison.Ordinal))
        {
            // Another recipe's execution is independent of the damaged one (the damaged recipe is position zero only).
            Assert.Equal(LocalExecutionStepInspectionStatus.Recorded, (await harness.StepAsync(sibling.Id)).Status);
        }
    }

    [Fact]
    public async Task Another_pinned_recipes_oversized_arguments_and_path_are_never_materialized_by_a_selected_step()
    {
        var (harness, _) = await CompletedAsync();
        var first = await harness.RowAsync(1);
        await harness.CorruptAsync(
            "UPDATE local_execution_sequence_recipes SET ArgumentsJson = '[\"' || hex(zeroblob(1100000)) || '\"]', "
            + "ExecutablePath = hex(zeroblob(70000)) WHERE Ordinal = 1",
            null,
            RecipeImmutable);

        var inspected = await harness.StepAsync(first.Id);
        var index = await harness.IndexAsync();

        Assert.Equal(LocalExecutionStepInspectionStatus.Recorded, inspected.Status);
        Assert.Equal(LocalExecutionSequenceInspectionStatus.Recorded, index.Status);
    }

    [Theory]
    [InlineData("byte_length_negative", "UPDATE verification_output_artifacts SET ByteLength = -1 WHERE VerificationExecutionId = {op} AND Purpose = 'StandardOutput'")]
    [InlineData("byte_length_real", "UPDATE verification_output_artifacts SET ByteLength = 3.5 WHERE VerificationExecutionId = {op} AND Purpose = 'StandardOutput'")]
    [InlineData("byte_length_text", "UPDATE verification_output_artifacts SET ByteLength = 'x' WHERE VerificationExecutionId = {op} AND Purpose = 'StandardOutput'")]
    [InlineData("capture_unknown", "UPDATE verification_output_artifacts SET CaptureOutcome = 'Bogus' WHERE VerificationExecutionId = {op} AND Purpose = 'StandardOutput'")]
    [InlineData("truncation_unknown_pair", "UPDATE verification_output_artifacts SET CaptureOutcome = 'RecoveredAfterHostInterruption' WHERE VerificationExecutionId = {op} AND Purpose = 'StandardOutput'")]
    [InlineData("truncated_two", "UPDATE verification_output_artifacts SET Truncated = 2 WHERE VerificationExecutionId = {op} AND Purpose = 'StandardOutput'")]
    [InlineData("truncated_negative_one", "UPDATE verification_output_artifacts SET Truncated = -1 WHERE VerificationExecutionId = {op} AND Purpose = 'StandardOutput'")]
    [InlineData("truncated_seven", "UPDATE verification_output_artifacts SET Truncated = 7 WHERE VerificationExecutionId = {op} AND Purpose = 'StandardOutput'")]
    [InlineData("truncated_real", "UPDATE verification_output_artifacts SET Truncated = 1.5 WHERE VerificationExecutionId = {op} AND Purpose = 'StandardOutput'")]
    [InlineData("truncated_text", "UPDATE verification_output_artifacts SET Truncated = 'x' WHERE VerificationExecutionId = {op} AND Purpose = 'StandardOutput'")]
    [InlineData("truncated_blob", "UPDATE verification_output_artifacts SET Truncated = X'0102' WHERE VerificationExecutionId = {op} AND Purpose = 'StandardOutput'")]
    [InlineData("truncated_oversized", "UPDATE verification_output_artifacts SET Truncated = replace(hex(zeroblob(150)), '00', '1') WHERE VerificationExecutionId = {op} AND Purpose = 'StandardOutput'")]
    public async Task A_bad_stream_row_is_unavailable_for_that_stream_only_and_hides_neither_its_sibling_nor_the_verification(
        string name, string sql)
    {
        var (harness, _) = await CompletedAsync();
        var row = await harness.RowAsync(1);
        Assert.NotEmpty(name);
        await harness.CorruptAsync(sql.Replace("{op}", $"'{row.OperationId!.Value.ToString().ToUpperInvariant()}'", StringComparison.Ordinal));

        var inspected = await harness.StepAsync(row.Id);

        Assert.Equal(LocalExecutionStepInspectionStatus.Recorded, inspected.Status);
        Assert.Equal(VerificationExecutionStatus.Failed, inspected.Verification!.Facts.Status);
        Assert.Equal(RecordedVerificationOutputDescriptorStatus.Unavailable, inspected.Verification.StandardOutput.Status);
        Assert.Equal(RecordedVerificationOutputDescriptorStatus.Recorded, inspected.Verification.StandardError.Status);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    public async Task Canonical_stored_truncation_values_are_recorded_exactly(string stored, bool expected)
    {
        var (harness, _) = await CompletedAsync();
        var row = await harness.RowAsync(1);
        await harness.CorruptAsync(
            $"UPDATE verification_output_artifacts SET Truncated = {stored} WHERE VerificationExecutionId = {{op}} AND Purpose = 'StandardOutput'"
                .Replace("{op}", $"'{row.OperationId!.Value.ToString().ToUpperInvariant()}'", StringComparison.Ordinal));

        var inspected = await harness.StepAsync(row.Id);

        Assert.Equal(RecordedVerificationOutputDescriptorStatus.Recorded, inspected.Verification!.StandardOutput.Status);
        Assert.Equal(expected, inspected.Verification.StandardOutput.Truncated);
    }

    [Fact]
    public async Task Ambiguous_rows_for_one_stream_are_unavailable_for_that_stream_and_at_most_three_are_read()
    {
        var (harness, _) = await CompletedAsync();
        var row = await harness.RowAsync(1);
        var op = $"'{row.OperationId!.Value.ToString().ToUpperInvariant()}'";
        var copies = string.Concat(Enumerable.Range(0, 4).Select(_ =>
            "INSERT INTO verification_output_artifacts (Id, VerificationExecutionId, Purpose, RelativeStoragePath, ContentHash, ByteLength, Truncated, "
            + $"CaptureOutcome, CreatedAtUtc) SELECT {ForeignGuid}, VerificationExecutionId, Purpose, RelativeStoragePath, ContentHash, ByteLength, "
            + $"Truncated, CaptureOutcome, CreatedAtUtc FROM verification_output_artifacts WHERE VerificationExecutionId = {op} AND Purpose = 'StandardOutput' LIMIT 1;"));
        await harness.CorruptAsync("DROP INDEX IF EXISTS IX_verification_output_artifacts_VerificationExecutionId_Purpose;" + copies);
        var log = new StatementLog();

        var inspected = await harness.StepAsync(row.Id, null, log);

        Assert.Equal(RecordedVerificationOutputDescriptorStatus.Unavailable, inspected.Verification!.StandardOutput.Status);
        Assert.Equal(RecordedVerificationOutputDescriptorStatus.Recorded, inspected.Verification.StandardError.Status);
        Assert.Contains(log.Statements, statement => statement.Contains("LIMIT 3", StringComparison.Ordinal)
            || statement.Contains("LIMIT @", StringComparison.Ordinal));
    }
}
