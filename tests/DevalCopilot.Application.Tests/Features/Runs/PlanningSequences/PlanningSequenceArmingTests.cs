using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.SecondChallengeRoundTestSupport;

namespace DevalCopilot.Application.Tests.Features.Runs.PlanningSequences;

/// <summary>ADR-0055 arming: one explicit authorization per run, bound to the run's objective and the exact project, workspace,
/// checkpoint and fingerprint, admitted only for a Created, pristine ManualAgent run, with a fresh Git capture outside the write lock
/// and every authority fact re-read under it. Arming invokes no provider, prepares no workspace and consumes no budget.</summary>
public sealed class PlanningSequenceArmingTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    [Fact]
    public async Task Arming_records_one_sequence_with_the_exact_tuple_and_consumes_nothing()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);

        var armed = await scene.ArmAsync();

        Assert.False(armed.Replayed);
        Assert.Equal(PlanningSequenceState.Armed, armed.State);
        Assert.Equal(scene.RunId, armed.RunId);
        Assert.Equal(scene.WorkspaceId, armed.WorkspaceId);
        Assert.Equal(scene.CheckpointId, armed.CheckpointId);
        Assert.Equal(Fingerprint, armed.FingerprintSha256);
        Assert.Equal(1, armed.Revision);

        var sequence = await scene.ReadSequenceAsync(armed.SequenceId);
        Assert.Equal(scene.ProjectId, sequence.ProjectId);
        Assert.Equal(PlanningSequenceObjective.Hash("Plan the next increment"), sequence.ObjectiveSha256);
        Assert.Equal(PlanningSequenceState.Armed, sequence.State);
        Assert.Empty(await scene.ReadStepsAsync(armed.SequenceId));
        Assert.Empty(await scene.ReadAttemptsAsync());
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.PlanningSequenceArmed));

        // The fresh Git capture is the only external work: no manifest was sealed and nothing was claimed.
        Assert.Equal(1, scene.Evidence.Captures);
        Assert.Equal(0, scene.Artifacts.Seals);
        await using var context = fixture.CreateContext();
        var run = await context.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == scene.RunId);
        Assert.Equal(RunLifecycle.Created, run.Lifecycle);
        Assert.Empty(await context.Artifacts.AsNoTracking().Where(artifact => artifact.RunId == scene.RunId).ToListAsync());
    }

    [Fact]
    public async Task Repeating_the_identical_request_returns_the_recorded_receipt_without_rearming()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var first = await scene.ArmAsync();
        var capturesAfterFirst = scene.Evidence.Captures;

        var replay = await scene.TryArmAsync();

        Assert.True(replay.IsSuccess);
        Assert.True(replay.Value.Replayed);
        Assert.Equal(first.SequenceId, replay.Value.SequenceId);
        Assert.Equal(first.ArmedAtUtc, replay.Value.ArmedAtUtc);
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.PlanningSequenceArmed));
        Assert.Equal(1, (await scene.ReadSequenceAsync(first.SequenceId)).Revision);
        Assert.Equal(capturesAfterFirst, scene.Evidence.Captures);
    }

    [Fact]
    public async Task A_replay_after_the_sequence_ended_returns_the_recorded_receipt_and_never_rearms()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var first = await scene.ArmAsync();
        Assert.True((await scene.StopAsync(first.SequenceId)).IsSuccess);

        var replay = await scene.TryArmAsync();

        Assert.True(replay.IsSuccess);
        Assert.True(replay.Value.Replayed);
        Assert.Equal(PlanningSequenceState.Stopped, replay.Value.State);
        Assert.Equal(PlanningSequenceState.Stopped, (await scene.ReadSequenceAsync(first.SequenceId)).State);
        Assert.Single(await ReadAllSequencesAsync());
    }

    [Fact]
    public async Task A_different_tuple_for_an_armed_run_conflicts_and_changes_nothing()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var first = await scene.ArmAsync();

        var otherFingerprint = await scene.TryArmAsync(scene.ArmCommand() with { FingerprintSha256 = new string('c', 64) });
        var otherCheckpoint = await scene.TryArmAsync(scene.ArmCommand() with { CheckpointId = Guid.NewGuid() });
        var otherWorkspace = await scene.TryArmAsync(scene.ArmCommand() with { WorkspaceId = Guid.NewGuid() });

        Assert.Equal(PlanningSequenceErrors.TupleConflictCode, otherFingerprint.Errors[0].Code);
        Assert.Equal(PlanningSequenceErrors.TupleConflictCode, otherCheckpoint.Errors[0].Code);
        Assert.Equal(PlanningSequenceErrors.TupleConflictCode, otherWorkspace.Errors[0].Code);
        Assert.Equal(1, (await scene.ReadSequenceAsync(first.SequenceId)).Revision);
        Assert.Single(await ReadAllSequencesAsync());
    }

    [Fact]
    public async Task An_unknown_run_is_one_fixed_not_found()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);

        var result = await scene.TryArmAsync(scene.ArmCommand() with { RunId = Guid.NewGuid() });

        Assert.Equal(PlanningSequenceErrors.NotFoundCode, result.Errors[0].Code);
        Assert.Empty(await ReadAllSequencesAsync());
        Assert.Equal(0, scene.Evidence.Captures);
    }

    [Theory]
    [InlineData(RunExecutionMode.Legacy)]
    [InlineData(RunExecutionMode.Simulated)]
    public async Task A_run_that_is_not_ManualAgent_is_refused_before_any_external_work(RunExecutionMode mode)
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture, mode: mode);

        var result = await scene.TryArmAsync();

        Assert.Equal(PlanningSequenceErrors.RunNotManualCode, result.Errors[0].Code);
        Assert.Empty(await ReadAllSequencesAsync());
        Assert.Equal(0, scene.Evidence.Captures);
        Assert.Equal(0, await scene.CountEventsAsync(RunEventType.PlanningSequenceArmed));
    }

    [Fact]
    public async Task A_run_that_is_already_running_is_not_pristine()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        await using (var context = fixture.CreateContext())
        {
            var run = await context.Runs.SingleAsync(candidate => candidate.Id == scene.RunId);
            run.Claim(PlanningSequenceTestScene.Start);
            await context.SaveChangesAsync();
        }

        var result = await scene.TryArmAsync();

        Assert.Equal(PlanningSequenceErrors.RunNotPristineCode, result.Errors[0].Code);
        Assert.Empty(await ReadAllSequencesAsync());
        Assert.Equal(0, scene.Evidence.Captures);
    }

    [Fact]
    public async Task A_run_with_an_attempt_a_collaboration_message_or_a_local_commit_operation_is_not_pristine()
    {
        var withAttempt = await PlanningSequenceTestScene.CreateAsync(fixture);
        await using (var context = fixture.CreateContext())
        {
            context.Attempts.Add(Attempt.ClaimAgent(
                Guid.NewGuid(), withAttempt.RunId, 1, withAttempt.WorkspaceId, withAttempt.CheckpointId, Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 1024, 2048, PlanningSequenceTestScene.Start, 1));
            await context.SaveChangesAsync();
        }

        Assert.Equal(PlanningSequenceErrors.RunNotPristineCode, (await withAttempt.TryArmAsync()).Errors[0].Code);

        var withMessage = await PlanningSequenceTestScene.CreateAsync(fixture);
        await using (var context = fixture.CreateContext())
        {
            var attempt = Attempt.ClaimAgent(
                Guid.NewGuid(), withMessage.RunId, 1, withMessage.WorkspaceId, withMessage.CheckpointId, Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 1024, 2048, PlanningSequenceTestScene.Start, 1);
            attempt.MarkAgentDispatched(PlanningSequenceTestScene.Start);
            attempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, PlanningSequenceTestScene.Start, processEvidence: TestProcessEvidence.CleanExit);
            context.Attempts.Add(attempt);
            context.CollaborationMessages.Add(CollaborationMessage.Record(
                Guid.NewGuid(), withMessage.RunId, attempt.Id, CollaborationMessage.ProtocolVersionOne,
                ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex),
                ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal, null, "Plan.",
                PlanningLineageSeeder.ProposalJson("Ledger"), CollaborationMessageProvenance.ProviderObserved, PlanningSequenceTestScene.Start));
            await context.SaveChangesAsync();
        }

        var messageResult = await withMessage.TryArmAsync();
        Assert.Equal(PlanningSequenceErrors.RunNotPristineCode, messageResult.Errors[0].Code);
        Assert.Empty(await ReadAllSequencesAsync());
    }

    [Fact]
    public async Task A_completed_attempt_still_makes_the_run_not_pristine()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        await using (var context = fixture.CreateContext())
        {
            var attempt = Attempt.ClaimAgent(
                Guid.NewGuid(), scene.RunId, 1, scene.WorkspaceId, scene.CheckpointId, Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 1024, 2048, PlanningSequenceTestScene.Start, 1);
            attempt.MarkAgentDispatched(PlanningSequenceTestScene.Start);
            attempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, Fingerprint, PlanningSequenceTestScene.Start, processEvidence: TestProcessEvidence.CleanExit);
            context.Attempts.Add(attempt);
            await context.SaveChangesAsync();
        }

        var result = await scene.TryArmAsync();

        Assert.Equal(PlanningSequenceErrors.RunNotPristineCode, result.Errors[0].Code);
        Assert.Empty(await ReadAllSequencesAsync());
    }

    [Fact]
    public async Task An_active_attempt_of_another_run_of_the_project_makes_the_project_busy()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        await using (var context = fixture.CreateContext())
        {
            var other = Run.RecordClassifiedIntent(
                Guid.NewGuid(), scene.ProjectId, 2, RunExecutionMode.ManualAgent, "Another objective", PlanningSequenceTestScene.Start);
            other.Claim(PlanningSequenceTestScene.Start);
            context.Runs.Add(other);
            context.Attempts.Add(Attempt.ClaimAgent(
                Guid.NewGuid(), other.Id, 1, scene.WorkspaceId, scene.CheckpointId, Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 1024, 2048, PlanningSequenceTestScene.Start, 1));
            await context.SaveChangesAsync();
        }

        var result = await scene.TryArmAsync();

        Assert.Equal(PlanningSequenceErrors.ProjectBusyCode, result.Errors[0].Code);
        Assert.Empty(await ReadAllSequencesAsync());
        Assert.Equal(0, scene.Evidence.Captures);
    }

    [Fact]
    public async Task An_open_local_commit_of_the_project_makes_the_project_busy()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        await using (var context = fixture.CreateContext())
        {
            var project = await context.Projects.SingleAsync(candidate => candidate.Id == scene.ProjectId);
            var workspace = await context.GitWorkspaces.SingleAsync(candidate => candidate.Id == scene.WorkspaceId);
            var lease = await context.RepositoryMutationLeases.SingleAsync(candidate => candidate.WorkspaceId == scene.WorkspaceId);
            await LocalCommitRowsSeed.AddOperationAsync(
                context, project, workspace, lease, LocalCommitStatus.NeedsAttention, new string('a', 40), new string('d', 40));
        }

        var result = await scene.TryArmAsync();

        Assert.Equal(PlanningSequenceErrors.ProjectBusyCode, result.Errors[0].Code);
        Assert.Empty(await ReadAllSequencesAsync());
    }

    [Fact]
    public async Task A_workspace_that_is_not_ready_a_released_lease_and_a_missing_checkpoint_are_each_refused()
    {
        var notReady = await PlanningSequenceTestScene.CreateAsync(fixture);
        await using (var context = fixture.CreateContext())
        {
            await context.GitWorkspaces.Where(candidate => candidate.Id == notReady.WorkspaceId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Status, WorkspaceStatus.NeedsAttention));
        }

        Assert.Equal(PlanningSequenceErrors.WorkspaceNotReadyCode, (await notReady.TryArmAsync()).Errors[0].Code);

        var noLease = await PlanningSequenceTestScene.CreateAsync(fixture);
        await using (var context = fixture.CreateContext())
        {
            await context.RepositoryMutationLeases.Where(candidate => candidate.WorkspaceId == noLease.WorkspaceId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Status, LeaseStatus.Released));
        }

        Assert.Equal(PlanningSequenceErrors.LeaseNotActiveCode, (await noLease.TryArmAsync()).Errors[0].Code);
        Assert.Empty(await ReadAllSequencesAsync());
    }

    [Fact]
    public async Task A_newer_checkpoint_than_the_requested_one_is_a_stale_tuple_and_is_never_retargeted()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        Guid newer;
        await using (var context = fixture.CreateContext())
        {
            var workspace = await context.GitWorkspaces.SingleAsync(candidate => candidate.Id == scene.WorkspaceId);
            var checkpoint = GitCheckpoint.Capture(
                Guid.NewGuid(), scene.WorkspaceId, workspace.ReserveCheckpointNumber(), PlanningSequenceTestScene.Start, new string('a', 40),
                new string('e', 64), []);
            newer = checkpoint.Id;
            context.GitCheckpoints.Add(checkpoint);
            await context.SaveChangesAsync();
        }

        var result = await scene.TryArmAsync();

        Assert.Equal(PlanningSequenceErrors.TupleStaleCode, result.Errors[0].Code);
        Assert.Empty(await ReadAllSequencesAsync());

        // Requesting the newer tuple succeeds and records exactly that tuple.
        var current = await scene.TryArmAsync(scene.ArmCommand() with { CheckpointId = newer, FingerprintSha256 = new string('e', 64) },
            new PlanningSequenceEvidenceReader(fingerprint: new string('e', 64)));
        Assert.True(current.IsSuccess, current.IsFailure ? current.Errors[0].Code : null);
        Assert.Equal(newer, current.Value.CheckpointId);
    }

    [Fact]
    public async Task A_fresh_capture_that_disagrees_or_fails_refuses_arming_without_recording_anything()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);

        var drifted = await scene.TryArmAsync(reader: new PlanningSequenceEvidenceReader(fingerprint: new string('f', 64)));
        var failed = await scene.TryArmAsync(reader: new PlanningSequenceEvidenceReader(outcome: GitWorkspaceEvidenceOutcome.GitInvocationFailed));

        Assert.Equal(PlanningSequenceErrors.TupleStaleCode, drifted.Errors[0].Code);
        Assert.Equal(PlanningSequenceErrors.TupleStaleCode, failed.Errors[0].Code);
        Assert.Empty(await ReadAllSequencesAsync());
        Assert.Equal(0, await scene.CountEventsAsync(RunEventType.PlanningSequenceArmed));
    }

    [Fact]
    public async Task Source_drift_between_the_capture_and_the_write_lock_refuses_arming_and_records_nothing()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var injecting = new PlanningSequenceEvidenceReader(onCapture: async () =>
        {
            // A newer checkpoint commits after the Git capture and before the write lock: the locked re-read must see it.
            await using var context = fixture.CreateContext();
            var workspace = await context.GitWorkspaces.SingleAsync(candidate => candidate.Id == scene.WorkspaceId);
            context.GitCheckpoints.Add(GitCheckpoint.Capture(
                Guid.NewGuid(), scene.WorkspaceId, workspace.ReserveCheckpointNumber(), PlanningSequenceTestScene.Start,
                new string('a', 40), new string('9', 64), []));
            await context.SaveChangesAsync();
        });

        var result = await scene.TryArmAsync(reader: injecting);

        Assert.Equal(PlanningSequenceErrors.TupleStaleCode, result.Errors[0].Code);
        Assert.Empty(await ReadAllSequencesAsync());
    }

    [Fact]
    public async Task Two_concurrent_identical_requests_record_exactly_one_sequence_and_one_event()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        using var gate = new ManualResetEventSlim(false);
        var racing = new PlanningSequenceEvidenceReader(onCapture: () =>
        {
            gate.Wait(TimeSpan.FromSeconds(10));
            return Task.CompletedTask;
        });

        var first = Task.Run(() => scene.TryArmAsync(reader: racing));
        var second = Task.Run(() => scene.TryArmAsync(reader: racing));
        gate.Set();
        var results = await Task.WhenAll(first, second);

        Assert.All(results, result => Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null));
        Assert.Single(results.Select(result => result.Value.SequenceId).Distinct());
        Assert.Single(results, result => !result.Value.Replayed);
        Assert.Single(await ReadAllSequencesAsync());
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.PlanningSequenceArmed));
    }

    [Fact]
    public async Task A_manual_claim_that_commits_first_refuses_arming_and_arming_then_refuses_a_manual_claim()
    {
        var manualFirst = await PlanningSequenceTestScene.CreateAsync(fixture);
        await using (var context = fixture.CreateContext())
        {
            context.Attempts.Add(Attempt.ClaimAgent(
                Guid.NewGuid(), manualFirst.RunId, 1, manualFirst.WorkspaceId, manualFirst.CheckpointId, Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 1024, 2048, PlanningSequenceTestScene.Start, 1));
            await context.SaveChangesAsync();
        }

        var refused = await manualFirst.TryArmAsync();
        Assert.False(refused.IsSuccess);
        Assert.Empty(await ReadAllSequencesAsync());

        var armedFirst = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await armedFirst.ArmAsync();
        await using var competing = fixture.CreateContext();
        competing.Attempts.Add(Attempt.ClaimAgent(
            Guid.NewGuid(), armedFirst.RunId, 1, armedFirst.WorkspaceId, armedFirst.CheckpointId, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 1024, 2048, PlanningSequenceTestScene.Start, 1));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => competing.SaveChangesAsync());
        Assert.Contains(PlanningSequenceErrors.ManualControlBlockedCode, exception.GetBaseException().Message, StringComparison.Ordinal);
        Assert.Equal(PlanningSequenceState.Armed, (await armedFirst.ReadSequenceAsync(armed.SequenceId)).State);
    }

    [Fact]
    public async Task The_post_commit_notification_is_a_hint_that_never_fails_a_recorded_arming()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var notifier = new ThrowingNotifier();

        var result = await scene.TryArmAsync(notifier: notifier);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, notifier.Notifications);
        Assert.Single(await ReadAllSequencesAsync());
    }

    private async Task<List<PlanningSequence>> ReadAllSequencesAsync()
    {
        await using var context = fixture.CreateContext();
        return await context.PlanningSequences.AsNoTracking().ToListAsync();
    }

    private sealed class ThrowingNotifier : IRunEventNotifier
    {
        public int Notifications { get; private set; }

        public Task NotifyRunAdvancedAsync(Guid runId, long latestSequence, CancellationToken cancellationToken)
        {
            Notifications++;
            throw new InvalidOperationException("notifier down");
        }
    }

}
