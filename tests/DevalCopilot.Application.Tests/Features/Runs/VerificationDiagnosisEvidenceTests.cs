using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Policies.VerificationDiagnosis;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.DiagnosisTestScene;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The host-derived complete verification selection a diagnosis is claimed against (ADR-0018): the latest execution of
/// every enabled command for the exact result checkpoint and fingerprint, each a coherent terminal Passed or Failed with at
/// least one Failed, with both sealed output rows for every failure. Real file-backed SQLite and the real read.
/// </summary>
public sealed class VerificationDiagnosisEvidenceTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static async Task<VerificationDiagnosisEvidence.ReadResult> ReadAsync(
        DiagnosisTestScene scene, bool asNoTracking = true, Microsoft.EntityFrameworkCore.DbContext? context = null)
    {
        await using var fresh = scene.Fixture.CreateContext();
        var db = context as DevalCopilot.Infrastructure.Persistence.DevalCopilotDbContext ?? fresh;
        return await VerificationDiagnosisEvidence.ReadAsync(
            db, scene.Run.ProjectId, scene.Scene.Workspace.Id, scene.Implementation.ReviewCheckpoint, asNoTracking, CancellationToken.None);
    }

    private static string Code(VerificationDiagnosisEvidence.ReadResult result) => result.Error!.Code;

    [Fact]
    public async Task A_mixed_complete_selection_is_ordered_by_command_number_and_pins_the_sealed_output_of_each_failure_only()
    {
        var scene = await CreateAsync(
            _fixture, [Spec.Passed(), Spec.Failed(), Spec.Passed(), Spec.Failed("two out", "two err")]);

        var result = await ReadAsync(scene);

        Assert.Null(result.Error);
        var entries = result.Value!.Entries;
        Assert.Equal([1, 2, 3, 4], entries.Select(e => e.Command.CommandNumber));
        Assert.Equal(scene.Commands.Select(c => c.Id), entries.Select(e => e.Command.Id));
        Assert.Equal([false, true, false, true], entries.Select(e => e.Failed));
        Assert.Equal(scene.Executions.Select(e => e.Id), result.Value.OrderedExecutionIds);
        Assert.Equal(scene.Commands.Zip(scene.Executions).Select(p => (p.First.Id, p.Second.Id)), result.Value.OrderedPairs);
        Assert.All(entries.Where(e => !e.Failed), e =>
        {
            Assert.Null(e.StandardOutput);
            Assert.Null(e.StandardError);
        });
        Assert.All(entries.Where(e => e.Failed), e =>
        {
            Assert.NotNull(e.StandardOutput);
            Assert.NotNull(e.StandardError);
            Assert.NotEqual(e.StandardOutput!.ArtifactId, e.StandardError!.ArtifactId);
        });
    }

    [Fact]
    public async Task Enabled_commands_are_ordered_by_command_number_not_by_creation_or_execution_order()
    {
        var scene = await CreateAsync(_fixture, [Spec.Failed(), Spec.Passed()]);
        // The second command (number 2) gets the older execution number.
        await scene.CorruptExecutionAsync(scene.Executions[0].Id, "ExecutionNumber = 99");

        var result = await ReadAsync(scene);

        Assert.Null(result.Error);
        Assert.Equal([1, 2], result.Value!.Entries.Select(e => e.Command.CommandNumber));
        Assert.Equal([true, false], result.Value.Entries.Select(e => e.Failed));
    }

    [Fact]
    public async Task No_enabled_command_at_all_is_refused()
    {
        var scene = await CreateAsync(_fixture, []);

        Assert.Equal(VerificationDiagnosisEvidence.NoCommandsEnabledCode, Code(await ReadAsync(scene)));
    }

    [Fact]
    public async Task Only_disabled_commands_count_as_no_enabled_command()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(enabled: false), Spec.Failed() with { Enabled = false }]);

        Assert.Equal(VerificationDiagnosisEvidence.NoCommandsEnabledCode, Code(await ReadAsync(scene)));
    }

    [Fact]
    public async Task An_enabled_command_without_an_execution_for_the_checkpoint_is_missing_evidence()
    {
        var scene = await CreateAsync(_fixture, [Spec.Failed()]);
        await scene.AddEnabledCommandAsync();

        Assert.Equal(VerificationDiagnosisEvidence.EvidenceMissingCode, Code(await ReadAsync(scene)));
    }

    [Fact]
    public async Task A_running_latest_execution_is_not_yet_evidence()
    {
        var scene = await CreateAsync(_fixture, [Spec.Failed(), new Spec(Kind.Running)]);

        Assert.Equal(VerificationDiagnosisEvidence.EvidenceRunningCode, Code(await ReadAsync(scene)));
    }

    [Theory]
    [InlineData("TimedOut")]
    [InlineData("Cancelled")]
    [InlineData("Interrupted")]
    [InlineData("SourceChanged")]
    [InlineData("StartFailure")]
    public async Task A_latest_execution_that_is_not_a_clean_pass_or_failure_makes_the_selection_not_diagnosable(string kind)
    {
        var scene = await CreateAsync(_fixture, [Spec.Failed(), new Spec(Enum.Parse<Kind>(kind))]);

        Assert.Equal(VerificationDiagnosisEvidence.NotDiagnosableCode, Code(await ReadAsync(scene)));
    }

    [Fact]
    public async Task A_selection_where_every_enabled_command_passed_has_no_failure_to_diagnose()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Passed()]);

        Assert.Equal(VerificationDiagnosisEvidence.NoFailedVerificationCode, Code(await ReadAsync(scene)));
    }

    // ---- LATEST-execution semantics -------------------------------------------------------------------------------------

    [Fact]
    public async Task An_older_failure_followed_by_a_newer_pass_of_the_same_command_is_a_pass()
    {
        var scene = await CreateAsync(_fixture, [Spec.Failed(), Spec.Failed()]);
        var newerPass = await scene.AddExecutionAsync(0, Spec.Passed());

        var result = await ReadAsync(scene);

        Assert.Null(result.Error);
        Assert.Equal([false, true], result.Value!.Entries.Select(e => e.Failed));
        Assert.Equal(newerPass.Id, result.Value.Entries[0].Execution.Id);
        Assert.DoesNotContain(scene.Executions[0].Id, result.Value.OrderedExecutionIds);
    }

    [Fact]
    public async Task A_newer_pass_of_the_only_failing_command_leaves_nothing_to_diagnose()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        await scene.AddExecutionAsync(1, Spec.Passed());

        Assert.Equal(VerificationDiagnosisEvidence.NoFailedVerificationCode, Code(await ReadAsync(scene)));
    }

    [Fact]
    public async Task An_older_pass_followed_by_a_newer_failure_of_the_same_command_is_a_failure_with_its_own_output()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Passed()]);
        var newerFailure = await scene.AddExecutionAsync(1, Spec.Failed("newer out", "newer err"));

        var result = await ReadAsync(scene);

        Assert.Null(result.Error);
        Assert.Equal([false, true], result.Value!.Entries.Select(e => e.Failed));
        Assert.Equal(newerFailure.Id, result.Value.Entries[1].Execution.Id);
        Assert.NotNull(result.Value.Entries[1].StandardOutput);
    }

    [Fact]
    public async Task A_newer_timed_out_execution_supersedes_an_older_failure_and_is_not_diagnosable()
    {
        var scene = await CreateAsync(_fixture, [Spec.Failed(), Spec.Passed()]);
        await scene.AddExecutionAsync(0, new Spec(Kind.TimedOut));

        Assert.Equal(VerificationDiagnosisEvidence.NotDiagnosableCode, Code(await ReadAsync(scene)));
    }

    [Fact]
    public async Task A_newer_running_execution_supersedes_an_older_failure()
    {
        var scene = await CreateAsync(_fixture, [Spec.Failed()]);
        await scene.AddExecutionAsync(0, new Spec(Kind.Running));

        Assert.Equal(VerificationDiagnosisEvidence.EvidenceRunningCode, Code(await ReadAsync(scene)));
    }

    // ---- Scope: checkpoint, fingerprint, enabled set, ownership ---------------------------------------------------------

    [Fact]
    public async Task Executions_bound_to_another_checkpoint_are_ignored_even_when_newer_or_failed()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Passed()]);
        // A failed execution and a timed-out one bound to the earlier (starting) checkpoint and numbered later.
        await scene.AddExecutionAsync(0, Spec.Failed(), scene.Scene.Checkpoint);
        await scene.AddExecutionAsync(1, new Spec(Kind.TimedOut), scene.Scene.Checkpoint);

        Assert.Equal(VerificationDiagnosisEvidence.NoFailedVerificationCode, Code(await ReadAsync(scene)));
    }

    [Fact]
    public async Task A_command_whose_only_execution_is_for_another_checkpoint_is_missing_evidence()
    {
        var scene = await CreateAsync(_fixture, [Spec.Failed(), Spec.Passed()]);
        // Command 2's only execution is moved to the earlier (starting) checkpoint.
        await scene.CorruptExecutionAsync(
            scene.Executions[1].Id,
            $"GitCheckpointId = '{scene.Scene.Checkpoint.Id.ToString().ToUpperInvariant()}', CheckpointFingerprintSha256 = '{RepairTestScene.Fingerprint}'");

        Assert.Equal(VerificationDiagnosisEvidence.EvidenceMissingCode, Code(await ReadAsync(scene)));
    }

    [Fact]
    public async Task An_execution_recorded_against_a_different_fingerprint_of_the_same_checkpoint_is_ignored()
    {
        var scene = await CreateAsync(_fixture, [Spec.Failed(), Spec.Passed()]);
        // The newer failed run of command 2 carries another fingerprint: it is not evidence for this checkpoint.
        var other = await scene.AddExecutionAsync(1, Spec.Failed());
        await scene.CorruptExecutionAsync(other.Id, $"CheckpointFingerprintSha256 = '{new string('9', 64)}'");

        var result = await ReadAsync(scene);

        Assert.Null(result.Error);
        Assert.Equal(scene.Executions.Select(e => e.Id), result.Value!.OrderedExecutionIds);
    }

    [Fact]
    public async Task Disabled_commands_are_ignored_whatever_their_latest_execution_is()
    {
        var scene = await CreateAsync(
            _fixture,
            [Spec.Failed(), new Spec(Kind.TimedOut, Enabled: false), new Spec(Kind.Running, Enabled: false), Spec.Passed()]);

        var result = await ReadAsync(scene);

        Assert.Null(result.Error);
        Assert.Equal([1, 4], result.Value!.Entries.Select(e => e.Command.CommandNumber));
        Assert.Equal([scene.Executions[0].Id, scene.Executions[3].Id], result.Value.OrderedExecutionIds);
    }

    [Fact]
    public async Task Disabling_the_only_failing_command_removes_it_from_the_selection()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        await scene.SetEnabledAsync(1, enabled: false);

        Assert.Equal(VerificationDiagnosisEvidence.NoFailedVerificationCode, Code(await ReadAsync(scene)));
    }

    [Theory]
    [InlineData("foreign-workspace")]
    [InlineData("foreign-project")]
    public async Task A_failed_execution_owned_by_another_workspace_or_project_is_not_diagnosable(string ownership)
    {
        var scene = await CreateAsync(_fixture, [Spec.Failed()]);
        await using (var other = _fixture.CreateContext())
        {
            if (ownership == "foreign-workspace")
            {
                var foreign = GitWorkspace.Prepare(
                    Guid.NewGuid(), scene.Run.ProjectId, 2, $@"C:\workspaces\{Guid.NewGuid():N}", "branch2", new string('a', 40), "main", RepairTestScene.Now);
                other.GitWorkspaces.Add(foreign);
                await other.SaveChangesAsync();
                await scene.CorruptExecutionAsync(scene.Executions[0].Id, $"GitWorkspaceId = '{foreign.Id.ToString().ToUpperInvariant()}'");
            }
            else
            {
                var foreign = Project.Register(Guid.NewGuid(), "Other", $@"C:\repos\{Guid.NewGuid():N}", RepairTestScene.Now);
                other.Projects.Add(foreign);
                await other.SaveChangesAsync();
                await scene.CorruptExecutionAsync(scene.Executions[0].Id, $"ProjectId = '{foreign.Id.ToString().ToUpperInvariant()}'");
            }
        }

        Assert.Equal(VerificationDiagnosisEvidence.NotDiagnosableCode, Code(await ReadAsync(scene)));
    }

    [Theory]
    [InlineData("Status = 'Passed', ExitCode = 1")]
    [InlineData("Status = 'Failed', ExitCode = 0")]
    [InlineData("Status = 'Passed', Outcome = 'TimedOut'")]
    [InlineData("Status = 'Failed', ExitCode = NULL")]
    [InlineData("Outcome = NULL")]
    [InlineData("DispatchedAtUtc = NULL")]
    [InlineData("CompletedAtUtc = NULL")]
    [InlineData("CompletionFingerprintSha256 = NULL")]
    [InlineData("CompletionFingerprintSha256 = 'ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff'")]
    public async Task A_contradictory_or_incomplete_failed_execution_row_is_not_diagnosable(string assignments)
    {
        var scene = await CreateAsync(_fixture, [Spec.Failed()]);
        await scene.CorruptExecutionAsync(scene.Executions[0].Id, assignments);

        Assert.Equal(VerificationDiagnosisEvidence.NotDiagnosableCode, Code(await ReadAsync(scene)));
    }

    [Theory]
    [InlineData("ExitCode = 1")]
    [InlineData("Outcome = 'TimedOut'")]
    [InlineData("CompletionFingerprintSha256 = 'ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff'")]
    [InlineData("DispatchedAtUtc = NULL")]
    public async Task A_contradictory_passed_execution_row_is_not_diagnosable(string assignments)
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        await scene.CorruptExecutionAsync(scene.Executions[0].Id, assignments);

        Assert.Equal(VerificationDiagnosisEvidence.NotDiagnosableCode, Code(await ReadAsync(scene)));
    }

    // ---- Sealed output rows ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_failed_execution_without_any_output_rows_makes_the_output_unavailable()
    {
        var scene = await CreateAsync(_fixture, [new Spec(Kind.Failed, WithOutputs: false)]);

        Assert.Equal(VerificationDiagnosisEvidence.OutputUnavailableCode, Code(await ReadAsync(scene)));
    }

    [Theory]
    [InlineData(VerificationOutputPurpose.StandardOutput)]
    [InlineData(VerificationOutputPurpose.StandardError)]
    public async Task A_failed_execution_missing_one_of_its_two_output_rows_makes_the_output_unavailable(VerificationOutputPurpose missing)
    {
        var scene = await CreateAsync(_fixture, [Spec.Failed()]);
        await using (var other = _fixture.CreateContext())
        {
            var executionId = scene.Executions[0].Id;
            await other.VerificationOutputArtifacts.Where(o => o.VerificationExecutionId == executionId && o.Purpose == missing).ExecuteDeleteAsync();
        }

        Assert.Equal(VerificationDiagnosisEvidence.OutputUnavailableCode, Code(await ReadAsync(scene)));
    }

    [Fact]
    public async Task A_duplicated_output_row_is_ambiguous_and_makes_the_output_unavailable()
    {
        var scene = await CreateAsync(_fixture, [Spec.Failed()]);
        await using (var other = _fixture.CreateContext())
        {
            await other.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_verification_output_artifacts_VerificationExecutionId_Purpose");
            var existing = await other.VerificationOutputArtifacts.AsNoTracking()
                .FirstAsync(o => o.VerificationExecutionId == scene.Executions[0].Id && o.Purpose == VerificationOutputPurpose.StandardOutput);
            other.VerificationOutputArtifacts.Add(VerificationOutputArtifact.Record(
                Guid.NewGuid(), existing.VerificationExecutionId, existing.Purpose, existing.RelativeStoragePath, existing.ContentHash,
                existing.ByteLength, existing.Truncated, existing.CaptureOutcome, RepairTestScene.Now));
            await other.SaveChangesAsync();
        }

        Assert.Equal(VerificationDiagnosisEvidence.OutputUnavailableCode, Code(await ReadAsync(scene)));
    }

    [Fact]
    public async Task Output_rows_of_a_passed_execution_are_never_required_or_pinned()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);

        var result = await ReadAsync(scene);

        Assert.Null(result.Error);
        Assert.Null(result.Value!.Entries[0].StandardOutput);
    }

    // ---- Sameness -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Two_reads_of_unchanged_evidence_are_the_same_selection()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);

        var first = (await ReadAsync(scene)).Value!;
        var second = (await ReadAsync(scene)).Value!;

        Assert.True(first.SameAs(second));
        Assert.True(second.SameAs(first));
    }

    [Fact]
    public async Task A_new_latest_execution_or_a_replaced_output_row_is_not_the_same_selection()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        var before = (await ReadAsync(scene)).Value!;

        // The failed output row identity changes (same execution, a different pinned artifact row).
        await using (var other = _fixture.CreateContext())
        {
            var executionId = scene.Executions[1].Id;
            await other.VerificationOutputArtifacts
                .Where(o => o.VerificationExecutionId == executionId && o.Purpose == VerificationOutputPurpose.StandardError)
                .ExecuteUpdateAsync(set => set.SetProperty(o => o.Id, Guid.NewGuid()));
        }

        var afterOutput = (await ReadAsync(scene)).Value!;
        Assert.False(before.SameAs(afterOutput));

        await scene.AddExecutionAsync(0, Spec.Passed());
        var afterExecution = (await ReadAsync(scene)).Value!;
        Assert.False(afterOutput.SameAs(afterExecution));
    }

    [Fact]
    public async Task An_untracked_read_sees_a_change_made_after_the_tracked_context_loaded_the_same_rows()
    {
        // The tracked context loads the Running state of the later command...
        var scene = await CreateAsync(_fixture, [Spec.Failed(), new Spec(Kind.Running)]);
        var tracked = await ReadAsync(scene, asNoTracking: false, context: scene.Db);
        Assert.Equal(VerificationDiagnosisEvidence.EvidenceRunningCode, Code(tracked));

        // ...and a concurrent writer completes it as Passed.
        await scene.CorruptExecutionAsync(
            scene.Executions[1].Id,
            $"Status = 'Passed', Outcome = 'Exited', ExitCode = 0, CompletedAtUtc = DispatchedAtUtc, CompletionFingerprintSha256 = '{RepairTestScene.ResultFingerprint}'");

        var staleTracked = await ReadAsync(scene, asNoTracking: false, context: scene.Db);
        var freshUntracked = await ReadAsync(scene, asNoTracking: true, context: scene.Db);

        Assert.Equal(VerificationDiagnosisEvidence.EvidenceRunningCode, Code(staleTracked));
        Assert.Null(freshUntracked.Error);
        Assert.Equal([true, false], freshUntracked.Value!.Entries.Select(e => e.Failed));
    }
}
