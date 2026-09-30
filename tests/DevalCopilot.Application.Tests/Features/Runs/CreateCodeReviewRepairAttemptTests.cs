using System.Text.Json;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodeReviewAttempt;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The one manual CodeReviewer format-repair claim: it reuses the ordinary code-review claim (same handler, same
/// protections) and adds a source link, exact report and verification-set derivation and comparison, source
/// eligibility checks at the request and again inside the short write-locked claim transaction, and a fixed
/// reminder in both manifest forms. Every refusal below creates no Attempt and leaves no orphaned manifest.
/// </summary>
public sealed class CreateCodeReviewRepairAttemptTests : IAsyncLifetime
{
    private const string NotFound = "agent_attempts.repair_source_not_found";
    private const string Ineligible = "agent_attempts.repair_source_ineligible";
    private const string RepairOfRepair = "agent_attempts.repair_of_repair_forbidden";
    private const string AlreadyRequested = "agent_attempts.repair_already_requested";
    private const string NotLatest = "agent_attempts.repair_source_not_latest";
    private const string CheckpointMismatch = "agent_attempts.repair_source_checkpoint_mismatch";
    private const string InputsMismatch = "agent_attempts.repair_source_inputs_mismatch";

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private CreateCodeReviewAttemptCommandHandler NewHandler(
        IDevalCopilotDbContext db, RepairEvidenceReader reader, RepairArtifactStore store, IAttemptDurabilityProbe? probe = null) =>
        new(db, reader, store, new FixedTimeProvider(RepairTestScene.Now), probe ?? new AttemptDurabilityProbe(_fixture.Options));

    private static string Code<T>(Result<T> result) => Assert.Single(result.Errors).Code;

    private static Task<Result<CreateCodeReviewAttemptCommandResult>> RepairAsync(
        CreateCodeReviewAttemptCommandHandler handler, Guid runId, Guid sourceId) =>
        handler.HandleAsync(CreateCodeReviewAttemptCommand.ForRepair(runId, sourceId), CancellationToken.None);

    private sealed record Setup(
        RepairTestScene Scene,
        Attempt Source,
        RepairTestScene.ImplementationScene Implementation,
        RepairTestScene.VerificationScene Verification);

    private async Task<Setup> SeedAsync(bool corrected = false, int commandCount = 2, int maximumAgentAttempts = 16, TimeSpan? time = null,
        AgentTokenUsageEvidence? sourceUsage = null)
    {
        var scene = await RepairTestScene.CreateAsync(_fixture, maximumAgentAttempts, time);
        scene.SourceTokenUsage = sourceUsage;
        var implementation = corrected ? scene.AddCorrectedImplementation() : scene.AddInitialImplementation();
        var verification = await scene.AddPassedVerificationAsync(implementation, commandCount);
        var source = scene.AddInvalidCodeReview(implementation, verification);
        await scene.SaveAsync();
        return new Setup(scene, source, implementation, verification);
    }

    private RepairEvidenceReader ReaderFor(Setup setup) => RepairEvidenceReader.Matching(setup.Implementation.ReviewFingerprint);

    private async Task AssertRefusedBeforeAnyExternalWorkAsync(
        Result<CreateCodeReviewAttemptCommandResult> result,
        string expectedCode,
        RepairTestScene scene,
        int attemptsBefore,
        RepairEvidenceReader reader,
        RepairArtifactStore store)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, Code(result));
        Assert.Equal(0, reader.Calls);
        Assert.Equal(0, store.SealCount);
        Assert.Empty(store.DeletedSealedFiles);
        Assert.Equal(attemptsBefore, await scene.AttemptCountAsync());
    }

    [Fact]
    public async Task Repair_of_an_initial_implementation_review_claims_a_linked_attempt_with_the_sources_exact_report_and_verification_set()
    {
        var setup = await SeedAsync();
        var reader = ReaderFor(setup);
        var store = new RepairArtifactStore();

        var result = await RepairAsync(NewHandler(setup.Scene.Db, reader, store), setup.Scene.Run.Id, setup.Source.Id);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        Assert.Equal(setup.Source.AttemptNumber + 1, result.Value.AttemptNumber);
        Assert.Equal(setup.Source.Id, result.Value.RepairSourceAttemptId);

        await using var verify = _fixture.CreateContext();
        var repair = await verify.Attempts.SingleAsync(a => a.Id == result.Value.AttemptId);
        Assert.Equal(setup.Source.Id, repair.AgentRepairSourceAttemptId);
        Assert.Equal(AttemptStatus.Running, repair.Status);
        Assert.Equal(AgentProvider.Codex, repair.AgentProvider);
        Assert.Equal(AgentRole.CodeReviewer, repair.AgentRole);
        Assert.Equal(AgentResponseContract.ImplementationReview, repair.AgentResponseContract);
        Assert.Equal(AgentPermissionProfile.ReadOnly, repair.AgentPermissionProfile);
        Assert.Equal("codex-implementation-review-v1", repair.AgentAdapterContractVersion);
        Assert.Equal(setup.Implementation.ReviewCheckpoint.Id, repair.AgentGitCheckpointId);
        Assert.Equal(setup.Implementation.ReviewFingerprint, repair.AgentCheckpointFingerprintSha256);

        var input = Assert.Single(verify.AttemptInputMessages.Where(m => m.AttemptId == repair.Id));
        Assert.Equal(0, input.Sequence);
        Assert.Equal(setup.Implementation.ExecutionReport.Id, input.CollaborationMessageId);
        var evidence = verify.AttemptVerificationEvidence.Where(e => e.AttemptId == repair.Id).OrderBy(e => e.Sequence).ToList();
        Assert.Equal(Enumerable.Range(0, 2), evidence.Select(e => e.Sequence));
        Assert.Equal(setup.Verification.Executions.Select(e => e.Id), evidence.Select(e => e.VerificationExecutionId));
        Assert.Equal(setup.Verification.Commands.Select(c => c.Id), evidence.Select(e => e.VerificationCommandId));
        Assert.Single(verify.Artifacts.Where(a => a.AttemptId == repair.Id && a.Purpose == ArtifactPurpose.AgentContextManifest));
        Assert.Empty(verify.CollaborationMessages.Where(m => m.AttemptId == repair.Id || m.AttemptId == setup.Source.Id));
        Assert.Equal(1, reader.Calls);
        Assert.Empty(store.DeletedSealedFiles);
    }

    [Fact]
    public async Task Repair_of_a_correction_report_review_retains_the_corrected_report_and_the_correction_evidence()
    {
        var setup = await SeedAsync(corrected: true, commandCount: 1);
        var store = new RepairArtifactStore();

        var result = await RepairAsync(NewHandler(setup.Scene.Db, ReaderFor(setup), store), setup.Scene.Run.Id, setup.Source.Id);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        await using var verify = _fixture.CreateContext();
        var input = Assert.Single(verify.AttemptInputMessages.Where(m => m.AttemptId == result.Value.AttemptId));
        Assert.Equal(setup.Implementation.ExecutionReport.Id, input.CollaborationMessageId);
        Assert.Equal(RepairTestScene.CorrectedFingerprint, setup.Implementation.ReviewFingerprint);
        using var manifest = JsonDocument.Parse(store.ReadManifest(setup.Scene.Run.Id, result.Value.AttemptId));
        var members = manifest.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(CodeReviewContextManifestBuilder.FormatRepairNotice, manifest.RootElement.GetProperty("formatRepairNotice").GetString());
        Assert.Equal("correctionEvidence", members[^1]);
        Assert.Equal(1, manifest.RootElement.GetProperty("correctionEvidence").GetProperty("orderedFindings").GetArrayLength());
    }

    [Fact]
    public async Task Repair_manifest_is_the_ordinary_manifest_plus_one_fixed_notice_inside_the_trusted_part()
    {
        var setup = await SeedAsync();
        var store = new RepairArtifactStore();

        var result = await RepairAsync(NewHandler(setup.Scene.Db, ReaderFor(setup), store), setup.Scene.Run.Id, setup.Source.Id);
        Assert.True(result.IsSuccess);
        var manifestText = store.ReadManifest(setup.Scene.Run.Id, result.Value.AttemptId);
        using var manifest = JsonDocument.Parse(manifestText);

        var members = manifest.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        var noticeIndex = members.IndexOf("formatRepairNotice");
        Assert.True(noticeIndex >= 0);
        Assert.Equal("expectedOutputSchema", members[noticeIndex - 1]);
        Assert.Equal("untrustedEvidenceBoundary", members[noticeIndex + 1]);
        Assert.Equal(
            new[]
            {
                "protocolVersion", "expectedResponseContract", "objective", "projectId", "gitWorkspaceId", "resultGitCheckpointId",
                "resultCheckpointFingerprintSha256", "instructionReferences", "instruction", "expectedOutputSchema",
                "untrustedEvidenceBoundary", "resolvedPlan", "executionReport", "verificationEvidence", "changeEvidence",
            },
            members.Where(name => name != "formatRepairNotice"));
        Assert.Equal(2, manifest.RootElement.GetProperty("verificationEvidence").GetArrayLength());
        Assert.DoesNotContain(setup.Source.Id.ToString(), manifestText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InvalidStructuredOutput", manifestText);
        Assert.DoesNotContain("parser", manifestText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_ordinary_request_is_unchanged_by_the_repair_capability()
    {
        var setup = await SeedAsync();
        var store = new RepairArtifactStore();
        await using var context = _fixture.CreateContext();

        var result = await NewHandler(context, ReaderFor(setup), store).HandleAsync(
            new CreateCodeReviewAttemptCommand(setup.Scene.Run.Id, setup.Implementation.ExecutionReport.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        Assert.Null(result.Value.RepairSourceAttemptId);
        Assert.Null((await context.Attempts.SingleAsync(a => a.Id == result.Value.AttemptId)).AgentRepairSourceAttemptId);
        Assert.DoesNotContain("formatRepairNotice", store.ReadManifest(setup.Scene.Run.Id, result.Value.AttemptId));
    }

    [Fact]
    public async Task An_unknown_source_and_a_source_of_another_run_are_the_same_fixed_not_found()
    {
        var setup = await SeedAsync();
        var other = await SeedAsync();
        var attemptsBefore = await setup.Scene.AttemptCountAsync();

        foreach (var sourceId in new[] { Guid.NewGuid(), other.Source.Id })
        {
            var reader = ReaderFor(setup);
            var store = new RepairArtifactStore();
            var result = await RepairAsync(NewHandler(setup.Scene.Db, reader, store), setup.Scene.Run.Id, sourceId);

            await AssertRefusedBeforeAnyExternalWorkAsync(result, NotFound, setup.Scene, attemptsBefore, reader, store);
            Assert.Equal("The selected attempt was not found for this run.", Assert.Single(result.Errors).Description);
        }
    }

    public static TheoryData<string, string> IneligibleSourceCorruptions => new()
    {
        { "wrong permission profile", "AgentPermissionProfile = 'WorkspaceEditOnly'" },
        { "unknown adapter contract version", "AgentAdapterContractVersion = 'codex-implementation-review-v0'" },
        { "no adapter contract version", "AgentAdapterContractVersion = NULL" },
        { "absent process evidence", "AgentProcessOutcome = NULL, AgentProcessExitCode = NULL, AgentProcessDuration = NULL" },
        { "non-clean exit code", "AgentProcessExitCode = 1" },
        { "unexpected message type", "AgentExpectedMessageType = 'Proposal'" },
        { "wrong provider", "AgentProvider = 'ClaudeCode'" },
        { "not dispatched", "AgentDispatchedAtUtc = NULL" },
        { "not concluded", "CompletedAtUtc = NULL" },
        { "wrong outcome", "AgentOutcome = 'ProviderInvocationFailed'" },
        { "success outcome", "AgentOutcome = 'ReviewApproved', Status = 'Completed'" },
        { "still running", "Status = 'Running'" },
        { "unreadable outcome", "AgentOutcome = 'NoSuchOutcome'" },
        { "unreadable role", "AgentRole = 'NoSuchRole'" },
        { "unreadable contract", "AgentResponseContract = 'NoSuchContract'" },
        { "malformed requested model", "AgentRequestedModel = '   '" },
    };

    [Theory]
    [MemberData(nameof(IneligibleSourceCorruptions))]
    public async Task A_source_that_is_not_the_exact_failed_code_review_shape_is_refused_before_any_external_work(
        string description, string assignments)
    {
        var setup = await SeedAsync();
        await setup.Scene.CorruptAsync(setup.Source.Id, assignments);
        var attemptsBefore = await setup.Scene.AttemptCountAsync();
        var reader = ReaderFor(setup);
        var store = new RepairArtifactStore();

        var result = await RepairAsync(NewHandler(setup.Scene.Db, reader, store), setup.Scene.Run.Id, setup.Source.Id);

        await AssertRefusedBeforeAnyExternalWorkAsync(result, Ineligible, setup.Scene, attemptsBefore, reader, store);
        Assert.Equal("The selected attempt cannot be repaired.", Assert.Single(result.Errors).Description);
        Assert.False(string.IsNullOrEmpty(description));
    }

    [Fact]
    public async Task A_source_that_recorded_a_review_finding_message_is_refused()
    {
        var setup = await SeedAsync();
        setup.Scene.Db.CollaborationMessages.Add(CollaborationMessage.RecordAgent(
            setup.Source, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
            CollaborationMessageType.ReviewFinding, setup.Implementation.ExecutionReport.Id, "A finding.",
            JsonSerializer.Serialize(new { severity = "high", category = "correctness", evidence = "E", requiredChange = "C" }),
            RepairTestScene.Now));
        await setup.Scene.SaveAsync();
        var attemptsBefore = await setup.Scene.AttemptCountAsync();
        var reader = ReaderFor(setup);
        var store = new RepairArtifactStore();

        var result = await RepairAsync(NewHandler(setup.Scene.Db, reader, store), setup.Scene.Run.Id, setup.Source.Id);

        await AssertRefusedBeforeAnyExternalWorkAsync(result, Ineligible, setup.Scene, attemptsBefore, reader, store);
    }

    [Fact]
    public async Task A_source_of_a_different_role_is_refused_at_the_code_review_operation()
    {
        var setup = await SeedAsync();
        var (criticalSource, _) = setup.Scene.AddCriticalReviewSource();
        await setup.Scene.SaveAsync();
        var attemptsBefore = await setup.Scene.AttemptCountAsync();
        var reader = ReaderFor(setup);
        var store = new RepairArtifactStore();

        var result = await RepairAsync(NewHandler(setup.Scene.Db, reader, store), setup.Scene.Run.Id, criticalSource.Id);

        await AssertRefusedBeforeAnyExternalWorkAsync(result, Ineligible, setup.Scene, attemptsBefore, reader, store);
    }

    [Fact]
    public async Task A_source_that_is_not_the_latest_agent_attempt_is_refused()
    {
        var setup = await SeedAsync();
        setup.Scene.Lineage.AddRoot();
        await setup.Scene.SaveAsync();
        var attemptsBefore = await setup.Scene.AttemptCountAsync();
        var reader = ReaderFor(setup);
        var store = new RepairArtifactStore();

        var result = await RepairAsync(NewHandler(setup.Scene.Db, reader, store), setup.Scene.Run.Id, setup.Source.Id);

        await AssertRefusedBeforeAnyExternalWorkAsync(result, NotLatest, setup.Scene, attemptsBefore, reader, store);
    }

    [Fact]
    public async Task The_source_is_repaired_at_most_once_and_a_repair_cannot_itself_be_repaired()
    {
        var setup = await SeedAsync();
        var first = await RepairAsync(
            NewHandler(setup.Scene.Db, ReaderFor(setup), new RepairArtifactStore()), setup.Scene.Run.Id, setup.Source.Id);
        Assert.True(first.IsSuccess, first.IsFailure ? Code(first) : null);
        var attemptsBefore = await setup.Scene.AttemptCountAsync();

        var reader = ReaderFor(setup);
        var store = new RepairArtifactStore();
        var second = await RepairAsync(NewHandler(setup.Scene.Db, reader, store), setup.Scene.Run.Id, setup.Source.Id);
        await AssertRefusedBeforeAnyExternalWorkAsync(second, AlreadyRequested, setup.Scene, attemptsBefore, reader, store);

        await using (var completeContext = _fixture.CreateContext())
        {
            var repair = await completeContext.Attempts.SingleAsync(a => a.Id == first.Value.AttemptId);
            repair.MarkAgentDispatched(RepairTestScene.Now);
            repair.CompleteAgent(
                AgentOutcome.InvalidStructuredOutput, setup.Implementation.ReviewFingerprint, RepairTestScene.Now,
                processEvidence: TestProcessEvidence.CleanExit);
            await completeContext.SaveChangesAsync();
        }

        var chainReader = ReaderFor(setup);
        var chainStore = new RepairArtifactStore();
        var chained = await RepairAsync(NewHandler(setup.Scene.Db, chainReader, chainStore), setup.Scene.Run.Id, first.Value.AttemptId);
        await AssertRefusedBeforeAnyExternalWorkAsync(chained, RepairOfRepair, setup.Scene, attemptsBefore, chainReader, chainStore);
    }

    [Fact]
    public async Task A_newer_workspace_checkpoint_refuses_the_repair_because_it_is_a_different_context()
    {
        var setup = await SeedAsync();
        setup.Scene.Db.GitCheckpoints.Add(GitCheckpoint.Capture(
            Guid.NewGuid(), setup.Scene.Workspace.Id, 9, RepairTestScene.Now, new string('d', 40), new string('d', 64), []));
        await setup.Scene.SaveAsync();
        var attemptsBefore = await setup.Scene.AttemptCountAsync();
        var reader = RepairEvidenceReader.Matching(new string('d', 64));
        var store = new RepairArtifactStore();

        var result = await RepairAsync(NewHandler(setup.Scene.Db, reader, store), setup.Scene.Run.Id, setup.Source.Id);

        await AssertRefusedBeforeAnyExternalWorkAsync(result, CheckpointMismatch, setup.Scene, attemptsBefore, reader, store);
    }

    // ---- exact report and verification identity: a rerun, an enabled-command change, a reorder, a replacement,
    // ---- or a partial or gapped recorded set is never substituted into the repair.

    private async Task AssertVerificationRefusalAsync(
        string description, Func<Setup, Task> change, string expectedCode)
    {
        var setup = await SeedAsync();
        await change(setup);
        setup.Scene.Detach();
        var attemptsBefore = await setup.Scene.AttemptCountAsync();
        var reader = ReaderFor(setup);
        var store = new RepairArtifactStore();

        var result = await RepairAsync(NewHandler(setup.Scene.Db, reader, store), setup.Scene.Run.Id, setup.Source.Id);

        Assert.False(string.IsNullOrEmpty(description));
        await AssertRefusedBeforeAnyExternalWorkAsync(result, expectedCode, setup.Scene, attemptsBefore, reader, store);
    }

    [Fact]
    public async Task A_passed_rerun_of_a_verification_command_replaces_the_selection_and_needs_an_ordinary_review()
    {
        await AssertVerificationRefusalAsync(
            "rerun",
            async setup =>
            {
                await using var context = _fixture.CreateContext();
                var command = await context.VerificationCommands.SingleAsync(c => c.Id == setup.Verification.Commands[0].Id);
                var checkpoint = await context.GitCheckpoints.SingleAsync(c => c.Id == setup.Implementation.ReviewCheckpoint.Id);
                var workspace = await context.GitWorkspaces.SingleAsync(w => w.Id == setup.Scene.Workspace.Id);
                var rerun = VerificationExecution.Claim(Guid.NewGuid(), setup.Scene.Project.Id, 3, workspace, checkpoint, command, RepairTestScene.Now);
                rerun.MarkDispatched(RepairTestScene.Now);
                rerun.Complete(VerificationExecutionOutcome.Exited, 0, checkpoint.FingerprintSha256, RepairTestScene.Now);
                context.VerificationExecutions.Add(rerun);
                await context.SaveChangesAsync();
            },
            InputsMismatch);
    }

    [Fact]
    public async Task A_failed_rerun_is_refused_by_the_ordinary_verification_rule()
    {
        await AssertVerificationRefusalAsync(
            "failed rerun",
            async setup =>
            {
                await using var context = _fixture.CreateContext();
                var command = await context.VerificationCommands.SingleAsync(c => c.Id == setup.Verification.Commands[0].Id);
                var checkpoint = await context.GitCheckpoints.SingleAsync(c => c.Id == setup.Implementation.ReviewCheckpoint.Id);
                var workspace = await context.GitWorkspaces.SingleAsync(w => w.Id == setup.Scene.Workspace.Id);
                var rerun = VerificationExecution.Claim(Guid.NewGuid(), setup.Scene.Project.Id, 3, workspace, checkpoint, command, RepairTestScene.Now);
                rerun.MarkDispatched(RepairTestScene.Now);
                rerun.Complete(VerificationExecutionOutcome.Exited, 1, checkpoint.FingerprintSha256, RepairTestScene.Now);
                context.VerificationExecutions.Add(rerun);
                await context.SaveChangesAsync();
            },
            "agent_attempts.verification_evidence_not_passed");
    }

    [Fact]
    public async Task A_disabled_verification_command_changes_the_enabled_selection_and_is_refused_as_a_mismatch()
    {
        await AssertVerificationRefusalAsync(
            "command disabled",
            async setup =>
            {
                await using var context = _fixture.CreateContext();
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE verification_commands SET IsEnabled = 0 WHERE Id = {setup.Verification.Commands[1].Id}");
            },
            InputsMismatch);
    }

    [Fact]
    public async Task A_newly_enabled_verification_command_with_a_passed_execution_is_refused_as_a_mismatch()
    {
        await AssertVerificationRefusalAsync(
            "command added",
            async setup =>
            {
                await using var context = _fixture.CreateContext();
                var checkpoint = await context.GitCheckpoints.SingleAsync(c => c.Id == setup.Implementation.ReviewCheckpoint.Id);
                var workspace = await context.GitWorkspaces.SingleAsync(w => w.Id == setup.Scene.Workspace.Id);
                var added = VerificationCommand.Configure(
                    Guid.NewGuid(), setup.Scene.Project.Id, 3, "Added", RepairTestScene.DotnetExecutablePath, ["test"], 300, true,
                    RepairTestScene.Now);
                context.VerificationCommands.Add(added);
                await context.SaveChangesAsync();
                var execution = VerificationExecution.Claim(Guid.NewGuid(), setup.Scene.Project.Id, 3, workspace, checkpoint, added, RepairTestScene.Now);
                execution.MarkDispatched(RepairTestScene.Now);
                execution.Complete(VerificationExecutionOutcome.Exited, 0, checkpoint.FingerprintSha256, RepairTestScene.Now);
                context.VerificationExecutions.Add(execution);
                await context.SaveChangesAsync();
            },
            InputsMismatch);
    }

    [Fact]
    public async Task A_newly_enabled_command_without_a_passed_execution_is_refused_by_the_ordinary_verification_rule()
    {
        await AssertVerificationRefusalAsync(
            "command added without evidence",
            async setup =>
            {
                await using var context = _fixture.CreateContext();
                context.VerificationCommands.Add(VerificationCommand.Configure(
                    Guid.NewGuid(), setup.Scene.Project.Id, 3, "Added", RepairTestScene.DotnetExecutablePath, ["test"], 300, true,
                    RepairTestScene.Now));
                await context.SaveChangesAsync();
            },
            "agent_attempts.verification_evidence_missing");
    }

    [Fact]
    public async Task Reordered_verification_commands_change_the_selection_order_and_are_refused_as_a_mismatch()
    {
        await AssertVerificationRefusalAsync(
            "commands reordered",
            async setup =>
            {
                await using var context = _fixture.CreateContext();
                var first = setup.Verification.Commands[0].Id;
                var second = setup.Verification.Commands[1].Id;
                await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE verification_commands SET CommandNumber = 9 WHERE Id = {first}");
                await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE verification_commands SET CommandNumber = 1 WHERE Id = {second}");
                await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE verification_commands SET CommandNumber = 2 WHERE Id = {first}");
            },
            InputsMismatch);
    }

    [Fact]
    public async Task All_verification_commands_disabled_is_refused_by_the_ordinary_verification_rule()
    {
        await AssertVerificationRefusalAsync(
            "nothing enabled",
            async setup =>
            {
                await using var context = _fixture.CreateContext();
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE verification_commands SET IsEnabled = 0 WHERE ProjectId = {setup.Scene.Project.Id}");
            },
            "agent_attempts.no_verification_commands_enabled");
    }

    [Fact]
    public async Task A_partial_recorded_verification_set_is_refused_as_a_mismatch()
    {
        await AssertVerificationRefusalAsync(
            "one evidence row dropped",
            async setup =>
            {
                await using var context = _fixture.CreateContext();
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM attempt_verification_evidence WHERE AttemptId = {setup.Source.Id} AND Sequence = 1");
            },
            InputsMismatch);
    }

    [Fact]
    public async Task A_gapped_recorded_verification_sequence_is_refused_as_ineligible()
    {
        await AssertVerificationRefusalAsync(
            "sequence gap",
            async setup =>
            {
                await using var context = _fixture.CreateContext();
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE attempt_verification_evidence SET Sequence = 4 WHERE AttemptId = {setup.Source.Id} AND Sequence = 1");
            },
            Ineligible);
    }

    [Fact]
    public async Task A_source_with_no_recorded_verification_set_is_refused_as_ineligible()
    {
        await AssertVerificationRefusalAsync(
            "no evidence",
            async setup =>
            {
                await using var context = _fixture.CreateContext();
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM attempt_verification_evidence WHERE AttemptId = {setup.Source.Id}");
            },
            Ineligible);
    }

    [Fact]
    public async Task A_source_with_a_missing_or_duplicated_report_input_is_refused_as_ineligible()
    {
        await AssertVerificationRefusalAsync(
            "report input removed",
            async setup =>
            {
                await using var context = _fixture.CreateContext();
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM attempt_input_messages WHERE AttemptId = {setup.Source.Id}");
            },
            Ineligible);
    }

    [Fact]
    public async Task A_report_whose_implementer_chain_is_no_longer_valid_is_refused_by_the_ordinary_validation()
    {
        await AssertVerificationRefusalAsync(
            "implementer outcome changed",
            async setup =>
            {
                await using var context = _fixture.CreateContext();
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE attempts SET AgentOutcome = 'NoChangesProduced', Status = 'Failed' WHERE Id = {setup.Implementation.ExecutionReport.AttemptId}");
            },
            "agent_attempts.implementer_attempt_not_valid");
    }

    [Fact]
    public async Task An_unreadable_report_message_refuses_the_repair_without_a_crash()
    {
        var setup = await SeedAsync();
        await using (var context = _fixture.CreateContext())
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE collaboration_messages SET Type = 'NoSuchType' WHERE Id = {setup.Implementation.ExecutionReport.Id}");
        }

        setup.Scene.Detach();
        var attemptsBefore = await setup.Scene.AttemptCountAsync();
        var reader = ReaderFor(setup);
        var store = new RepairArtifactStore();

        var result = await RepairAsync(NewHandler(setup.Scene.Db, reader, store), setup.Scene.Run.Id, setup.Source.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(0, reader.Calls);
        Assert.Equal(0, store.SealCount);
        Assert.Equal(attemptsBefore, await setup.Scene.AttemptCountAsync());
    }

    [Fact]
    public async Task The_ordinary_claim_gates_still_refuse_a_repair_without_any_external_work()
    {
        var exhausted = await SeedAsync(maximumAgentAttempts: 4);
        await AssertGateAsync(exhausted, "agent_attempts.budget_exhausted");

        var timed = await SeedAsync(time: TimeSpan.FromMinutes(1));
        await AssertGateAsync(timed, "agent_attempts.time_budget_exceeded");

        var stopped = await SeedAsync(sourceUsage: TokenStopTestSupport.CodexUsage(500, 50));
        await TokenStopTestSupport.SetStopAsync(_fixture, stopped.Scene.Run.Id, AgentProvider.Codex, 550);
        await AssertGateAsync(stopped, AgentTokenStopGate.ReachedCode);

        async Task AssertGateAsync(Setup gated, string code)
        {
            var attemptsBefore = await gated.Scene.AttemptCountAsync();
            var gateReader = ReaderFor(gated);
            var gateStore = new RepairArtifactStore();
            await using var freshContext = _fixture.CreateContext();
            var result = await RepairAsync(NewHandler(freshContext, gateReader, gateStore), gated.Scene.Run.Id, gated.Source.Id);
            await AssertRefusedBeforeAnyExternalWorkAsync(result, code, gated.Scene, attemptsBefore, gateReader, gateStore);
        }
    }

    // ---- the actual commit seam

    private sealed record Seam(Setup Setup, DevalCopilotDbContext Racing)
    {
        public RepairTestScene Scene => Setup.Scene;

        public Attempt Source => Setup.Source;
    }

    private async Task AssertSeamRefusalAsync(string description, Func<Seam, Task> seamChange, string expectedCode, bool corrected = false)
    {
        var setup = await SeedAsync(corrected);
        var reader = ReaderFor(setup);
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(setup.Scene.Db)
        {
            BeforeBeginTransaction = async _ =>
            {
                await using var racing = _fixture.CreateContext();
                await seamChange(new Seam(setup, racing));
                await racing.SaveChangesAsync(CancellationToken.None);
            },
        };

        var result = await RepairAsync(NewHandler(faulting, reader, store), setup.Scene.Run.Id, setup.Source.Id);

        Assert.True(result.IsFailure, description);
        Assert.Equal(expectedCode, Code(result));
        Assert.Equal(1, reader.Calls);
        Assert.Equal(1, store.SealCount);
        var deleted = Assert.Single(store.DeletedSealedFiles);
        Assert.Equal(setup.Scene.Run.Id, deleted.RunId);
        Assert.Equal(ArtifactPurpose.AgentContextManifest, deleted.Purpose);
        Assert.Equal(store.SealedAttemptIds[0], deleted.AttemptId);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.Attempts.Where(a => a.Id == deleted.AttemptId));
        Assert.Empty(verify.AttemptInputMessages.Where(m => m.AttemptId == deleted.AttemptId));
        Assert.Empty(verify.AttemptVerificationEvidence.Where(e => e.AttemptId == deleted.AttemptId));
        Assert.Empty(verify.Artifacts.Where(a => a.AttemptId == deleted.AttemptId));
    }

    [Fact]
    public async Task A_competing_repair_committed_at_the_claim_seam_is_refused_and_rolled_back_completely()
    {
        await AssertSeamRefusalAsync(
            "competing repair",
            seam =>
            {
                var number = seam.Scene.Lineage.ReserveAttemptNumber();
                var competing = Attempt.ClaimAgentCodeReviewWithAssignment(
                    Guid.NewGuid(), seam.Scene.Run.Id, number, seam.Scene.Workspace.Id, seam.Setup.Implementation.ReviewCheckpoint.Id,
                    seam.Setup.Implementation.ReviewFingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288,
                    RepairTestScene.Now, null, null, number, seam.Source.Id);
                seam.Racing.Attempts.Add(competing);
                seam.Racing.AttemptInputMessages.Add(
                    AttemptInputMessage.Record(Guid.NewGuid(), competing.Id, seam.Setup.Implementation.ExecutionReport.Id, sequence: 0));
                return Task.CompletedTask;
            },
            AlreadyRequested);
    }

    [Fact]
    public async Task A_newer_agent_attempt_committed_at_the_claim_seam_makes_the_source_not_latest()
    {
        await AssertSeamRefusalAsync(
            "newer attempt",
            seam =>
            {
                var number = seam.Scene.Lineage.ReserveAttemptNumber();
                var planner = Attempt.ClaimAgent(
                    Guid.NewGuid(), seam.Scene.Run.Id, number, seam.Scene.Workspace.Id, seam.Setup.Implementation.ReviewCheckpoint.Id,
                    seam.Setup.Implementation.ReviewFingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288,
                    RepairTestScene.Now, number);
                planner.Fail(RepairTestScene.Now);
                seam.Racing.Attempts.Add(planner);
                return Task.CompletedTask;
            },
            NotLatest);
    }

    [Fact]
    public async Task A_passed_rerun_committed_at_the_claim_seam_is_refused_as_a_mismatch()
    {
        await AssertSeamRefusalAsync(
            "rerun at the seam",
            async seam =>
            {
                var command = await seam.Racing.VerificationCommands.SingleAsync(c => c.Id == seam.Setup.Verification.Commands[0].Id);
                var checkpoint = await seam.Racing.GitCheckpoints.SingleAsync(c => c.Id == seam.Setup.Implementation.ReviewCheckpoint.Id);
                var workspace = await seam.Racing.GitWorkspaces.SingleAsync(w => w.Id == seam.Scene.Workspace.Id);
                var rerun = VerificationExecution.Claim(Guid.NewGuid(), seam.Scene.Project.Id, 3, workspace, checkpoint, command, RepairTestScene.Now);
                rerun.MarkDispatched(RepairTestScene.Now);
                rerun.Complete(VerificationExecutionOutcome.Exited, 0, checkpoint.FingerprintSha256, RepairTestScene.Now);
                seam.Racing.VerificationExecutions.Add(rerun);
            },
            InputsMismatch);
    }

    [Fact]
    public async Task A_verification_command_disabled_at_the_claim_seam_is_refused_as_a_mismatch()
    {
        await AssertSeamRefusalAsync(
            "command disabled at the seam",
            async seam => await seam.Racing.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE verification_commands SET IsEnabled = 0 WHERE Id = {seam.Setup.Verification.Commands[1].Id}"),
            InputsMismatch);
    }

    [Fact]
    public async Task Verification_commands_reordered_at_the_claim_seam_are_refused_as_a_mismatch()
    {
        await AssertSeamRefusalAsync(
            "commands reordered at the seam",
            async seam =>
            {
                var first = seam.Setup.Verification.Commands[0].Id;
                var second = seam.Setup.Verification.Commands[1].Id;
                await seam.Racing.Database.ExecuteSqlInterpolatedAsync($"UPDATE verification_commands SET CommandNumber = 9 WHERE Id = {first}");
                await seam.Racing.Database.ExecuteSqlInterpolatedAsync($"UPDATE verification_commands SET CommandNumber = 1 WHERE Id = {second}");
                await seam.Racing.Database.ExecuteSqlInterpolatedAsync($"UPDATE verification_commands SET CommandNumber = 2 WHERE Id = {first}");
            },
            InputsMismatch);
    }

    [Fact]
    public async Task A_recorded_verification_row_removed_at_the_claim_seam_is_refused_as_a_mismatch()
    {
        await AssertSeamRefusalAsync(
            "evidence row removed",
            async seam => await seam.Racing.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM attempt_verification_evidence WHERE AttemptId = {seam.Source.Id} AND Sequence = 1"),
            InputsMismatch);
    }

    [Fact]
    public async Task The_recorded_report_input_removed_at_the_claim_seam_is_refused()
    {
        await AssertSeamRefusalAsync(
            "report removed",
            async seam => await seam.Racing.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM attempt_input_messages WHERE AttemptId = {seam.Source.Id}"),
            Ineligible);
    }

    [Fact]
    public async Task A_report_chain_broken_at_the_claim_seam_is_refused_by_the_ordinary_validation()
    {
        await AssertSeamRefusalAsync(
            "implementer chain broken",
            async seam => await seam.Racing.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE attempts SET AgentOutcome = 'NoChangesProduced', Status = 'Failed' WHERE Id = {seam.Setup.Implementation.ExecutionReport.AttemptId}"),
            "agent_attempts.implementer_attempt_not_valid");
    }

    // The claim's context stays alive and populated (the scene tracked every seeded row): only untracked reads at the
    // seam can see a row an independent connection changed immediately before BEGIN.

    [Fact]
    public async Task A_recorded_verification_execution_failing_at_the_claim_seam_is_refused_despite_the_tracked_row()
    {
        await AssertSeamRefusalAsync(
            "execution failed",
            async seam => await seam.Racing.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE verification_executions SET Status = 'Failed', ExitCode = 1 WHERE Id = {seam.Setup.Verification.Executions[0].Id}"),
            "agent_attempts.verification_evidence_not_passed");
    }

    [Fact]
    public async Task The_execution_reports_actor_provider_changed_at_the_claim_seam_is_refused_despite_the_tracked_row()
    {
        await AssertSeamRefusalAsync(
            "report actor changed",
            async seam => await seam.Racing.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE collaboration_messages SET ActorAgentProvider = 'Codex' WHERE Id = {seam.Setup.Implementation.ExecutionReport.Id}"),
            "agent_attempts.implementer_attempt_not_valid");
    }

    [Fact]
    public async Task The_implementer_attempts_result_checkpoint_changed_at_the_claim_seam_is_refused_despite_the_tracked_row()
    {
        await AssertSeamRefusalAsync(
            "result checkpoint changed",
            async seam => await seam.Racing.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE attempts SET AgentResultGitCheckpointId = AgentGitCheckpointId WHERE Id = {seam.Setup.Implementation.ExecutionReport.AttemptId}"),
            "agent_attempts.result_checkpoint_mismatch");
    }

    [Fact]
    public async Task A_semantic_message_recorded_by_the_source_at_the_claim_seam_is_refused()
    {
        await AssertSeamRefusalAsync(
            "message recorded",
            seam =>
            {
                seam.Racing.CollaborationMessages.Add(CollaborationMessage.RecordAgent(
                    seam.Source, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
                    CollaborationMessageType.ReviewFinding, seam.Setup.Implementation.ExecutionReport.Id, "A finding.",
                    JsonSerializer.Serialize(new { severity = "high", category = "correctness", evidence = "E", requiredChange = "C" }),
                    RepairTestScene.Now));
                return Task.CompletedTask;
            },
            Ineligible);
    }

    [Fact]
    public async Task A_new_workspace_checkpoint_committed_at_the_claim_seam_is_refused()
    {
        await AssertSeamRefusalAsync(
            "new checkpoint",
            seam =>
            {
                seam.Racing.GitCheckpoints.Add(GitCheckpoint.Capture(
                    Guid.NewGuid(), seam.Scene.Workspace.Id, 9, RepairTestScene.Now, new string('d', 40), new string('d', 64), []));
                return Task.CompletedTask;
            },
            "agent_attempts.checkpoint_not_current");
    }

    [Fact]
    public async Task A_lease_released_at_the_claim_seam_is_refused()
    {
        await AssertSeamRefusalAsync(
            "lease released",
            async seam => await seam.Racing.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE repository_mutation_leases SET Status = 'Released' WHERE WorkspaceId = {seam.Scene.Workspace.Id}"),
            "agent_attempts.lease_not_active");
    }

    [Fact]
    public async Task A_run_that_ended_at_the_claim_seam_is_refused()
    {
        await AssertSeamRefusalAsync(
            "run ended",
            async seam => await seam.Racing.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE runs SET Lifecycle = 'Failed' WHERE Id = {seam.Scene.Run.Id}"),
            "runs.not_running");
    }

    [Fact]
    public async Task A_running_process_attempt_committed_at_the_claim_seam_is_refused_as_an_active_attempt()
    {
        await AssertSeamRefusalAsync(
            "process attempt",
            seam =>
            {
                seam.Racing.Attempts.Add(
                    Attempt.Claim(Guid.NewGuid(), seam.Scene.Run.Id, seam.Scene.Lineage.ReserveAttemptNumber(), RepairTestScene.Now));
                return Task.CompletedTask;
            },
            "attempts.run_has_active_attempt");
    }

    [Fact]
    public async Task A_model_preference_change_committed_at_the_claim_seam_is_refused_by_the_preference_guard()
    {
        await AssertSeamRefusalAsync(
            "preference changed",
            async seam => await seam.Racing.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE runs SET RequestedCodexModel = 'gpt-6-sol', RequestedCodexEffort = 'high' WHERE Id = {seam.Scene.Run.Id}"),
            "agent_attempts.assignment_preference_changed");
    }

    [Fact]
    public async Task A_stop_threshold_change_committed_at_the_claim_seam_is_refused_by_the_policy_guard()
    {
        var setup = await SeedAsync();
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(setup.Scene.Db)
        {
            BeforeBeginTransaction = async _ => await TokenStopTestSupport.SetStopAsync(
                _fixture, setup.Scene.Run.Id, AgentProvider.Codex, 1_000_000),
        };

        var result = await RepairAsync(NewHandler(faulting, ReaderFor(setup), store), setup.Scene.Run.Id, setup.Source.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(CurrentTokenStopPolicy.PolicyChangedDuringClaimCode, Code(result));
        Assert.Single(store.DeletedSealedFiles);
        Assert.DoesNotContain(await setup.Scene.AttemptsAsync(), a => a.AgentRepairSourceAttemptId is not null);
    }

    [Fact]
    public async Task Concurrent_repair_claims_of_one_source_commit_exactly_one_repair()
    {
        var setup = await SeedAsync();
        using var barrier = new Barrier(2);
        RepairEvidenceReader Reader() => new(UntrackedManifestTestSupport.Evidence(setup.Implementation.ReviewFingerprint))
        {
            OnFirstCapture = _ => Task.Run(() => barrier.SignalAndWait(TimeSpan.FromSeconds(30))),
        };

        var firstStore = new RepairArtifactStore();
        var secondStore = new RepairArtifactStore();
        await using var firstContext = _fixture.CreateContext();
        await using var secondContext = _fixture.CreateContext();
        var results = await Task.WhenAll(
            RepairAsync(NewHandler(firstContext, Reader(), firstStore), setup.Scene.Run.Id, setup.Source.Id),
            RepairAsync(NewHandler(secondContext, Reader(), secondStore), setup.Scene.Run.Id, setup.Source.Id));

        var winner = Assert.Single(results, r => r.IsSuccess);
        var loser = Assert.Single(results, r => r.IsFailure);
        Assert.Contains(Code(loser), new[] { AlreadyRequested, NotLatest, "attempts.run_has_active_attempt" });

        await using var verify = _fixture.CreateContext();
        var repair = Assert.Single(verify.Attempts.Where(a => a.AgentRepairSourceAttemptId == setup.Source.Id));
        Assert.Equal(winner.Value.AttemptId, repair.Id);
        var removed = Assert.Single(firstStore.DeletedSealedFiles.Concat(secondStore.DeletedSealedFiles));
        Assert.NotEqual(repair.Id, removed.AttemptId);
        Assert.Single(verify.Artifacts.Where(a => a.AttemptId == repair.Id));
        Assert.Equal(2, verify.Attempts.Count(a => a.RunId == setup.Scene.Run.Id && a.AgentRole == AgentRole.CodeReviewer));
        Assert.Equal(2, verify.AttemptVerificationEvidence.Count(e => e.AttemptId == repair.Id));
    }

    // ---- durability and cancellation at the claim boundary

    [Fact]
    public async Task A_transaction_that_cannot_be_acquired_fails_closed_and_removes_the_sealed_manifest()
    {
        var setup = await SeedAsync();
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(setup.Scene.Db) { ThrowOnBeginTransaction = true };

        var result = await RepairAsync(NewHandler(faulting, ReaderFor(setup), store), setup.Scene.Run.Id, setup.Source.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.persistence_failed", Code(result));
        Assert.Single(store.DeletedSealedFiles);
        Assert.DoesNotContain(await setup.Scene.AttemptsAsync(), a => a.AgentRepairSourceAttemptId is not null);
    }

    [Fact]
    public async Task Cancellation_while_acquiring_the_transaction_propagates_and_removes_the_sealed_manifest()
    {
        var setup = await SeedAsync();
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(setup.Scene.Db) { ThrowCancellationOnBeginTransaction = true };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => RepairAsync(NewHandler(faulting, ReaderFor(setup), store), setup.Scene.Run.Id, setup.Source.Id));

        Assert.Single(store.DeletedSealedFiles);
        Assert.DoesNotContain(await setup.Scene.AttemptsAsync(), a => a.AgentRepairSourceAttemptId is not null);
    }

    [Fact]
    public async Task A_commit_that_fails_without_persisting_is_a_safe_failure_and_removes_the_sealed_manifest()
    {
        var setup = await SeedAsync();
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(setup.Scene.Db) { CommitFailure = FaultInjectingDbContext.CommitFailureMode.BeforeCommit };

        var result = await RepairAsync(NewHandler(faulting, ReaderFor(setup), store), setup.Scene.Run.Id, setup.Source.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.persistence_failed", Code(result));
        Assert.Single(store.DeletedSealedFiles);
        Assert.DoesNotContain(await setup.Scene.AttemptsAsync(), a => a.AgentRepairSourceAttemptId is not null);
    }

    [Fact]
    public async Task A_commit_that_completes_before_the_failure_is_reported_is_resolved_as_success_and_keeps_the_manifest()
    {
        var setup = await SeedAsync();
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(setup.Scene.Db) { CommitFailure = FaultInjectingDbContext.CommitFailureMode.AfterCommit };

        var result = await RepairAsync(NewHandler(faulting, ReaderFor(setup), store), setup.Scene.Run.Id, setup.Source.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(setup.Source.Id, result.Value.RepairSourceAttemptId);
        Assert.Empty(store.DeletedSealedFiles);
        Assert.Contains(await setup.Scene.AttemptsAsync(), a => a.Id == result.Value.AttemptId && a.AgentRepairSourceAttemptId == setup.Source.Id);
    }

    [Fact]
    public async Task An_ambiguous_commit_the_probe_cannot_resolve_is_reported_unresolved_and_never_deletes_the_manifest()
    {
        var setup = await SeedAsync();
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(setup.Scene.Db) { CommitFailure = FaultInjectingDbContext.CommitFailureMode.BeforeCommit };
        var probe = new FixedDurabilityProbe(AttemptDurabilityCheckResult.Unresolved);

        var result = await RepairAsync(NewHandler(faulting, ReaderFor(setup), store, probe), setup.Scene.Run.Id, setup.Source.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.persistence_unresolved", Code(result));
        Assert.Empty(store.DeletedSealedFiles);
        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public async Task Cancellation_before_the_commit_propagates_and_removes_the_manifest_when_nothing_persisted()
    {
        var setup = await SeedAsync();
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(setup.Scene.Db) { CommitFailure = FaultInjectingDbContext.CommitFailureMode.CancellationBeforeCommit };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => RepairAsync(NewHandler(faulting, ReaderFor(setup), store), setup.Scene.Run.Id, setup.Source.Id));

        Assert.Single(store.DeletedSealedFiles);
        Assert.DoesNotContain(await setup.Scene.AttemptsAsync(), a => a.AgentRepairSourceAttemptId is not null);
    }

    [Fact]
    public async Task Cancellation_after_the_commit_propagates_and_preserves_the_persisted_attempts_manifest()
    {
        var setup = await SeedAsync();
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(setup.Scene.Db) { CommitFailure = FaultInjectingDbContext.CommitFailureMode.CancellationAfterCommit };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => RepairAsync(NewHandler(faulting, ReaderFor(setup), store), setup.Scene.Run.Id, setup.Source.Id));

        Assert.Empty(store.DeletedSealedFiles);
        Assert.Contains(await setup.Scene.AttemptsAsync(), a => a.AgentRepairSourceAttemptId == setup.Source.Id);
    }

    [Fact]
    public async Task A_save_that_fails_without_persisting_is_a_safe_failure_and_removes_the_sealed_manifest()
    {
        var setup = await SeedAsync();
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(setup.Scene.Db)
        {
            SaveChangesFailure = FaultInjectingDbContext.SaveChangesFailureMode.UpdateExceptionBeforeSave,
        };

        var result = await RepairAsync(NewHandler(faulting, ReaderFor(setup), store), setup.Scene.Run.Id, setup.Source.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.persistence_failed", Code(result));
        Assert.Single(store.DeletedSealedFiles);
        Assert.DoesNotContain(await setup.Scene.AttemptsAsync(), a => a.AgentRepairSourceAttemptId is not null);
    }

    [Fact]
    public async Task Cancellation_after_the_save_was_applied_still_rolls_back_and_removes_the_manifest()
    {
        var setup = await SeedAsync();
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(setup.Scene.Db)
        {
            SaveChangesFailure = FaultInjectingDbContext.SaveChangesFailureMode.CancellationAfterSave,
        };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => RepairAsync(NewHandler(faulting, ReaderFor(setup), store), setup.Scene.Run.Id, setup.Source.Id));

        Assert.Single(store.DeletedSealedFiles);
        Assert.DoesNotContain(await setup.Scene.AttemptsAsync(), a => a.AgentRepairSourceAttemptId is not null);
    }

    [Fact]
    public async Task Deleting_a_repaired_source_alone_is_refused_while_deleting_the_run_removes_both()
    {
        var setup = await SeedAsync();
        var repair = await RepairAsync(
            NewHandler(setup.Scene.Db, ReaderFor(setup), new RepairArtifactStore()), setup.Scene.Run.Id, setup.Source.Id);
        Assert.True(repair.IsSuccess);

        await using (var context = _fixture.CreateContext())
        {
            await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM attempts WHERE Id = {setup.Source.Id}"));
        }

        await using (var context = _fixture.CreateContext())
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"PRAGMA foreign_keys = ON");
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM runs WHERE Id = {setup.Scene.Run.Id}");
            Assert.Empty(context.Attempts.Where(a => a.RunId == setup.Scene.Run.Id));
        }
    }
}
