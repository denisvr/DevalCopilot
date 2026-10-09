using System.Text.Json;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.AbandonManualRun;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>ADR-0031 on real file-backed SQLite: the atomic closure of a created or running manual run, its frozen clock and retained
/// history, every refusal with nothing written, exact duplicate arbitration and the rollback of a failed save.</summary>
public sealed class AbandonManualRunCommandHandlerTests(SqliteDatabaseFixture fixture) : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Later = AbandonmentScene.Created.AddHours(3);

    private Task<AbandonmentScene> SceneAsync(bool running = false, bool withWorkspace = false, RunExecutionMode mode = RunExecutionMode.ManualAgent) =>
        AbandonmentScene.CreateAsync(fixture, running, withWorkspace, mode);

    private async Task<Result<AbandonManualRunCommandResult>> AbandonAsync(
        Guid runId, string reason = AbandonmentRaceSupport.Reason, DateTimeOffset? now = null, IRunEventNotifier? notifier = null)
    {
        await using var db = fixture.CreateContext();
        return await new AbandonManualRunCommandHandler(db, new FixedTimeProvider(now ?? Later), notifier)
            .HandleAsync(new AbandonManualRunCommand(runId, reason), CancellationToken.None);
    }

    private static string Code<T>(Result<T> result) => Assert.Single(result.Errors).Code;

    // ---- the closure ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_created_manual_run_is_abandoned_with_one_human_event_and_no_counted_time()
    {
        var scene = await SceneAsync();

        var result = await AbandonAsync(scene.RunId);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        Assert.Equal(scene.RunId, result.Value.RunId);
        Assert.Equal(1, result.Value.ExecutionNumber);
        Assert.Equal(AbandonmentRaceSupport.Reason, result.Value.Reason);
        Assert.Equal(Later, result.Value.AbandonedAtUtc);
        await using var db = fixture.CreateContext();
        var run = await db.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == scene.RunId);
        Assert.Equal(RunLifecycle.Abandoned, run.Lifecycle);
        Assert.Equal(RunStage.Intake, run.Stage);
        Assert.Equal(0, run.AccumulatedAutonomousSeconds);
        Assert.Equal(Later, run.LastAdvancedAtUtc);
        Assert.Equal(Later, run.AbandonedAtUtc);
        Assert.Equal(AbandonmentRaceSupport.Reason, run.AbandonmentReason);
        Assert.Equal(ParticipantIdentity.None(), run.ActiveParticipant);
        var events = await db.Events.AsNoTracking().Where(candidate => candidate.RunId == scene.RunId).OrderBy(candidate => candidate.Sequence).ToListAsync();
        Assert.Equal([RunEventType.RunStarted, RunEventType.RunAbandoned], events.Select(candidate => candidate.EventType));
        var abandoned = events[1];
        Assert.Equal(ParticipantIdentity.ForHuman(), abandoned.Actor);
        Assert.Null(abandoned.AttemptId);
        Assert.Equal(Later, abandoned.OccurredAtUtc);
        using var payload = JsonDocument.Parse(abandoned.PayloadJson);
        Assert.Equal(AbandonmentRaceSupport.Reason, payload.RootElement.GetProperty("reason").GetString());
        Assert.True(events[1].Sequence > events[0].Sequence);
    }

    [Fact]
    public async Task A_running_manual_run_freezes_its_time_keeps_its_stage_and_history_and_clears_the_participant()
    {
        var scene = await SceneAsync(running: true);
        await scene.AddAttemptAsync(attempt => attempt.Complete(AbandonmentScene.Created.AddSeconds(20)));
        await using (var setup = fixture.CreateContext())
        {
            var run = await setup.Runs.SingleAsync(candidate => candidate.Id == scene.RunId);
            run.AdvanceStage(RunStage.Plan, ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), AbandonmentScene.Created.AddSeconds(40));
            await setup.SaveChangesAsync();
        }

        var before = await scene.SnapshotAsync();
        var result = await AbandonAsync(scene.RunId, now: AbandonmentScene.Created.AddSeconds(100));

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        var after = await scene.SnapshotAsync();
        Assert.Equal(nameof(RunLifecycle.Abandoned), after.Lifecycle);
        Assert.Equal(nameof(RunStage.Plan), after.Stage);
        // 30 seconds before the stage advance (claimed at +10, advanced at +40) plus 60 up to the abandonment.
        Assert.Equal(90, after.AccumulatedSeconds);
        Assert.Equal(ParticipantKind.None, after.ActiveParticipant);
        Assert.Equal(before.Attempts, after.Attempts);
        Assert.Equal(before.ProjectEvents + 1, after.ProjectEvents);
        Assert.Equal(1, after.AbandonedEvents);

        // A later read of the same row never adds more time: the clock is frozen at the abandonment.
        var replay = await AbandonAsync(scene.RunId, now: AbandonmentScene.Created.AddDays(30));
        Assert.True(replay.IsSuccess);
        Assert.Equal(90, (await scene.SnapshotAsync()).AccumulatedSeconds);
    }

    [Fact]
    public async Task The_reason_is_normalized_before_it_is_persisted_and_compared()
    {
        var scene = await SceneAsync();

        var result = await AbandonAsync(scene.RunId, "  First line\r\nSecond line \r\n");

        Assert.True(result.IsSuccess);
        Assert.Equal("First line\nSecond line", result.Value.Reason);
        Assert.Equal("First line\nSecond line", (await scene.SnapshotAsync()).Reason);
    }

    // ---- validation -------------------------------------------------------------------------------------------------------

    public static TheoryData<string?> InvalidReasons() =>
    [
        null,
        "",
        "   \r\n ",
        "lone\rcarriage return",
        "tab\tseparated",
        "nul\0byte",
        new string('a', RunAbandonmentPolicy.MaximumReasonUtf8Bytes + 1),
        new string('漢', 683),
        "before" + char.ConvertFromUtf32(0xE0001) + "after",
        "before" + char.ConvertFromUtf32(0xE0020) + "after",
        "😀" + char.ConvertFromUtf32(0xE007F),
    ];

    [Fact]
    public async Task Ordinary_supplementary_text_is_recorded_exactly_in_the_row_and_the_event()
    {
        var scene = await SceneAsync();
        var reason = "Replaced " + char.ConvertFromUtf32(0x1F600) + char.ConvertFromUtf32(0x20000) + " by a smaller change";

        var result = await AbandonAsync(scene.RunId, reason);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        Assert.Equal(reason, (await scene.SnapshotAsync()).Reason);
        await using var db = fixture.CreateContext();
        var payload = await db.Events.AsNoTracking()
            .Where(candidate => candidate.RunId == scene.RunId && candidate.EventType == RunEventType.RunAbandoned)
            .Select(candidate => candidate.PayloadJson).SingleAsync();
        using var document = JsonDocument.Parse(payload);
        Assert.Equal(reason, document.RootElement.GetProperty("reason").GetString());
    }

    [Theory]
    [MemberData(nameof(InvalidReasons))]
    public async Task An_invalid_reason_is_refused_before_anything_is_read_or_written(string? reason)
    {
        var scene = await SceneAsync();
        var before = await scene.SnapshotAsync();

        var result = await AbandonAsync(scene.RunId, reason!);
        var validation = await new AbandonManualRunCommandValidator().ValidateAsync(new AbandonManualRunCommand(scene.RunId, reason!));

        Assert.Equal(RunAbandonmentErrors.InvalidReasonCode, Code(result));
        Assert.False(validation.IsValid);
        Assert.Equal(before, await scene.SnapshotAsync());
    }

    [Fact]
    public async Task The_validator_requires_a_run_identifier_and_accepts_a_normalizable_reason()
    {
        var validator = new AbandonManualRunCommandValidator();

        Assert.False((await validator.ValidateAsync(new AbandonManualRunCommand(Guid.Empty, "Reason"))).IsValid);
        Assert.True((await validator.ValidateAsync(new AbandonManualRunCommand(Guid.NewGuid(), "  Reason\r\nbody "))).IsValid);
    }

    // ---- refusals write nothing --------------------------------------------------------------------------------------------

    public static TheoryData<string, string> Refusals() => new()
    {
        { "legacy", RunAbandonmentErrors.RunNotManualCode },
        { "simulated", RunAbandonmentErrors.RunNotManualCode },
        { "mode-real", RunAbandonmentErrors.RunNotManualCode },
        { "mode-text", RunAbandonmentErrors.RunNotManualCode },
        { "mode-undefined", RunAbandonmentErrors.RunNotManualCode },
        { "mode-blob", RunAbandonmentErrors.RunNotManualCode },
        { "completed", RunAbandonmentErrors.RunNotAbandonableCode },
        { "failed", RunAbandonmentErrors.RunNotAbandonableCode },
        { "interrupted", RunAbandonmentErrors.RunNotAbandonableCode },
        { "unknown-lifecycle", RunAbandonmentErrors.RunNotAbandonableCode },
        { "running-attempt", RunAbandonmentErrors.ActiveAttemptCode },
        { "unknown-attempt", RunAbandonmentErrors.ActiveAttemptCode },
        { "other-run-running-attempt", RunAbandonmentErrors.ActiveAttemptCode },
        { "running-verification", RunAbandonmentErrors.ActiveVerificationCode },
        { "unknown-verification", RunAbandonmentErrors.ActiveVerificationCode },
        { "prepared-commit", RunAbandonmentErrors.LocalCommitOpenCode },
        { "executing-commit", RunAbandonmentErrors.LocalCommitOpenCode },
        { "attention-commit", RunAbandonmentErrors.LocalCommitOpenCode },
        { "unknown-commit", RunAbandonmentErrors.LocalCommitOpenCode },
        { "preparing-workspace", RunAbandonmentErrors.WorkspaceBusyCode },
        { "committing-workspace", RunAbandonmentErrors.WorkspaceBusyCode },
        { "unknown-workspace", RunAbandonmentErrors.WorkspaceBusyCode },
    };

    private async Task<AbandonmentScene> RefusalSceneAsync(string name)
    {
        var scene = await SceneAsync(running: true, withWorkspace: true, mode: name == "legacy" ? RunExecutionMode.Legacy : name == "simulated" ? RunExecutionMode.Simulated : RunExecutionMode.ManualAgent);
        switch (name)
        {
            case "mode-real": await scene.SetRunModeAsync("2.0000001"); break;
            case "mode-text": await scene.SetRunModeAsync("'ManualAgent'"); break;
            case "mode-undefined": await scene.SetRunModeAsync("7"); break;
            case "mode-blob": await scene.SetRunModeAsync("X'02'"); break;
            case "completed": await scene.SqlAsync("UPDATE runs SET Lifecycle = 'Completed' WHERE Id = {0}", scene.RunId); break;
            case "failed": await scene.SqlAsync("UPDATE runs SET Lifecycle = 'Failed' WHERE Id = {0}", scene.RunId); break;
            case "interrupted": await scene.SqlAsync("UPDATE runs SET Lifecycle = 'Interrupted' WHERE Id = {0}", scene.RunId); break;
            case "unknown-lifecycle": await scene.SqlAsync("UPDATE runs SET Lifecycle = 'Paused' WHERE Id = {0}", scene.RunId); break;
            case "running-attempt": await scene.AddAttemptAsync(); break;
            case "unknown-attempt":
                var id = await scene.AddAttemptAsync(attempt => attempt.Complete(AbandonmentScene.Created.AddSeconds(20)));
                await scene.SqlAsync("UPDATE attempts SET Status = 'Paused' WHERE Id = {0}", id);
                break;
            case "other-run-running-attempt":
                await using (var db = fixture.CreateContext())
                {
                    var project = await db.Projects.SingleAsync(candidate => candidate.Id == scene.ProjectId);
                    var other = Run.RecordClassifiedIntent(
                        Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Earlier", AbandonmentScene.Created);
                    other.Claim(AbandonmentScene.Created);
                    db.Runs.Add(other);
                    db.Attempts.Add(Attempt.Claim(Guid.NewGuid(), other.Id, 1, AbandonmentScene.Created));
                    await db.SaveChangesAsync();
                }

                break;
            case "running-verification": await scene.AddVerificationAsync(); break;
            case "unknown-verification":
                var executionId = await scene.AddVerificationAsync(execution =>
                {
                    execution.MarkDispatched(AbandonmentScene.Created);
                    execution.Complete(VerificationExecutionOutcome.Exited, 0, new string('b', 64), AbandonmentScene.Created);
                });
                await scene.SqlAsync("UPDATE verification_executions SET Status = 'Paused' WHERE Id = {0}", executionId);
                break;
            case "prepared-commit": await scene.AddLocalCommitOperationAsync(LocalCommitStatus.Prepared); break;
            case "executing-commit": await scene.AddLocalCommitOperationAsync(LocalCommitStatus.Executing); break;
            case "attention-commit": await scene.AddLocalCommitOperationAsync(LocalCommitStatus.NeedsAttention); break;
            case "unknown-commit":
                var operationId = await scene.AddLocalCommitOperationAsync(LocalCommitStatus.Completed);
                await scene.SqlAsync("UPDATE local_commit_operations SET Status = 'Paused' WHERE Id = {0}", operationId);
                break;
            case "preparing-workspace": await scene.SqlAsync("UPDATE git_workspaces SET Status = 'Preparing' WHERE Id = {0}", scene.WorkspaceId!); break;
            case "committing-workspace": await scene.SqlAsync("UPDATE git_workspaces SET Status = 'Committing' WHERE Id = {0}", scene.WorkspaceId!); break;
            case "unknown-workspace": await scene.SqlAsync("UPDATE git_workspaces SET Status = 'Paused' WHERE Id = {0}", scene.WorkspaceId!); break;
        }

        return scene;
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task A_refusal_names_a_fixed_reason_and_writes_nothing(string name, string expectedCode)
    {
        var scene = await RefusalSceneAsync(name);
        var before = await scene.SnapshotAsync();

        var result = await AbandonAsync(scene.RunId);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, Code(result));
        Assert.Equal(before, await scene.SnapshotAsync());
    }

    [Fact]
    public async Task An_unknown_run_is_not_found()
    {
        var result = await AbandonAsync(Guid.NewGuid());

        Assert.Equal(RunAbandonmentErrors.RunNotFoundCode, Code(result));
    }

    public static TheoryData<string> RetainedWorkspaceStates() => ["Ready", "FailedToPrepare", "MissingExternally", "AlteredExternally", "NeedsAttention"];

    [Theory]
    [MemberData(nameof(RetainedWorkspaceStates))]
    public async Task A_workspace_in_any_other_known_state_is_retained_unchanged_and_never_blocks(string status)
    {
        var scene = await SceneAsync(running: true, withWorkspace: true);
        await scene.SqlAsync("UPDATE git_workspaces SET Status = {0} WHERE Id = {1}", status, scene.WorkspaceId!);
        await scene.AddAttemptAsync(attempt => attempt.Complete(AbandonmentScene.Created.AddSeconds(20)));
        await scene.AddVerificationAsync(execution =>
        {
            execution.MarkDispatched(AbandonmentScene.Created);
            execution.Complete(VerificationExecutionOutcome.Exited, 0, new string('b', 64), AbandonmentScene.Created);
        });

        var result = await AbandonAsync(scene.RunId);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        await using var db = fixture.CreateContext();
        var workspace = await db.GitWorkspaces.AsNoTracking().SingleAsync(candidate => candidate.Id == scene.WorkspaceId);
        Assert.Equal(Enum.Parse<WorkspaceStatus>(status), workspace.Status);
        var lease = await db.RepositoryMutationLeases.AsNoTracking().SingleAsync(candidate => candidate.WorkspaceId == scene.WorkspaceId);
        Assert.Equal(LeaseStatus.Active, lease.Status);
        Assert.Equal(1, await db.GitCheckpoints.CountAsync(candidate => candidate.WorkspaceId == scene.WorkspaceId));
    }

    [Fact]
    public async Task A_created_run_without_any_workspace_can_be_abandoned()
    {
        var scene = await SceneAsync();

        Assert.True((await AbandonAsync(scene.RunId)).IsSuccess);
        await using var db = fixture.CreateContext();
        Assert.Empty(db.GitWorkspaces.Where(candidate => candidate.ProjectId == scene.ProjectId));
    }

    [Theory]
    [InlineData(LocalCommitStatus.Completed)]
    [InlineData(LocalCommitStatus.Failed)]
    [InlineData(LocalCommitStatus.Interrupted)]
    public async Task A_terminal_local_commit_operation_of_the_project_does_not_block(LocalCommitStatus status)
    {
        var scene = await SceneAsync(running: true, withWorkspace: true);
        await scene.AddLocalCommitOperationAsync(status);

        var result = await AbandonAsync(scene.RunId);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
    }

    // ---- replay and arbitration ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_same_normalized_reason_replays_the_original_result_without_another_event_or_time()
    {
        var scene = await SceneAsync(running: true);
        var first = await AbandonAsync(scene.RunId, now: Later);
        var snapshot = await scene.SnapshotAsync();

        var replay = await AbandonAsync(scene.RunId, " " + AbandonmentRaceSupport.Reason + "\r\n", now: Later.AddDays(5));

        Assert.True(replay.IsSuccess);
        Assert.Equal(first.Value, replay.Value);
        Assert.Equal(Later, replay.Value.AbandonedAtUtc);
        Assert.Equal(snapshot, await scene.SnapshotAsync());
    }

    [Fact]
    public async Task A_different_reason_conflicts_and_changes_nothing()
    {
        var scene = await SceneAsync();
        Assert.True((await AbandonAsync(scene.RunId)).IsSuccess);
        var snapshot = await scene.SnapshotAsync();

        var result = await AbandonAsync(scene.RunId, "Another reason entirely", now: Later.AddDays(1));

        Assert.Equal(RunAbandonmentErrors.ReasonConflictCode, Code(result));
        Assert.Equal(snapshot, await scene.SnapshotAsync());
        Assert.Equal(AbandonmentRaceSupport.Reason, snapshot.Reason);
    }

    public static TheoryData<string> IncoherentAbandonments() =>
    [
        "no-reason",
        "padded-reason",
        "no-time",
        "time-differs-from-last-advance",
        "active-participant",
        "no-event",
        "second-event",
        "agent-event",
        "attempt-event",
        "other-reason-event",
        "malformed-payload",
        "event-at-another-time",
        "legacy-mode",
    ];

    private async Task<AbandonmentScene> IncoherentSceneAsync(string name)
    {
        var scene = await SceneAsync(running: true);
        Assert.True((await AbandonAsync(scene.RunId)).IsSuccess);
        switch (name)
        {
            case "no-reason": await scene.SqlAsync("UPDATE runs SET AbandonmentReason = NULL WHERE Id = {0}", scene.RunId); break;
            case "padded-reason": await scene.SqlAsync("UPDATE runs SET AbandonmentReason = ' ' || AbandonmentReason WHERE Id = {0}", scene.RunId); break;
            case "no-time": await scene.SqlAsync("UPDATE runs SET AbandonedAtUtc = NULL WHERE Id = {0}", scene.RunId); break;
            case "time-differs-from-last-advance": await scene.SqlAsync("UPDATE runs SET AbandonedAtUtc = '2030-01-01 00:00:00+00:00' WHERE Id = {0}", scene.RunId); break;
            case "active-participant": await scene.SqlAsync("UPDATE runs SET ActiveParticipantKind = 'Orchestrator' WHERE Id = {0}", scene.RunId); break;
            case "no-event": await scene.SqlAsync("DELETE FROM events WHERE EventType = 'run.abandoned' AND RunId = {0}", scene.RunId); break;
            case "second-event":
                await scene.SqlAsync(
                    "INSERT INTO events (Id, RunId, AttemptId, EventType, ActorKind, ActorAgentRole, ActorAgentProvider, PayloadJson, OccurredAtUtc) "
                    + "SELECT {0}, RunId, AttemptId, EventType, ActorKind, ActorAgentRole, ActorAgentProvider, PayloadJson, OccurredAtUtc FROM events "
                    + "WHERE EventType = 'run.abandoned' AND RunId = {1}",
                    Guid.NewGuid(), scene.RunId);
                break;
            case "agent-event": await scene.SqlAsync("UPDATE events SET ActorKind = 'Agent' WHERE EventType = 'run.abandoned' AND RunId = {0}", scene.RunId); break;
            case "attempt-event":
                var attemptId = await scene.AddAttemptAsync(attempt => attempt.Complete(AbandonmentScene.Created.AddSeconds(20)));
                await scene.SqlAsync("UPDATE events SET AttemptId = {0} WHERE EventType = 'run.abandoned' AND RunId = {1}", attemptId, scene.RunId);
                break;
            case "other-reason-event":
                await scene.SqlAsync(
                    "UPDATE events SET PayloadJson = {0} WHERE EventType = 'run.abandoned' AND RunId = {1}",
                    JsonSerializer.Serialize(new { reason = "Forged" }), scene.RunId);
                break;
            case "malformed-payload": await scene.SqlAsync("UPDATE events SET PayloadJson = 'not json' WHERE EventType = 'run.abandoned' AND RunId = {0}", scene.RunId); break;
            case "event-at-another-time": await scene.SqlAsync("UPDATE events SET OccurredAtUtc = '2030-01-01 00:00:00+00:00' WHERE EventType = 'run.abandoned' AND RunId = {0}", scene.RunId); break;
            case "legacy-mode": await scene.SetRunModeAsync("0"); break;
        }

        return scene;
    }

    [Theory]
    [MemberData(nameof(IncoherentAbandonments))]
    public async Task An_incoherent_abandoned_run_answers_neither_a_replay_nor_a_conflict_and_changes_nothing(string name)
    {
        var scene = await IncoherentSceneAsync(name);
        var before = await scene.SnapshotAsync();

        var result = await AbandonAsync(scene.RunId, now: Later.AddDays(1));

        Assert.True(result.IsFailure);
        Assert.Contains(Code(result), new[] { RunAbandonmentErrors.AbandonmentIncoherentCode, RunAbandonmentErrors.RunNotManualCode });
        Assert.Equal(before, await scene.SnapshotAsync());
    }

    [Fact]
    public async Task Two_overlapping_requests_with_the_same_reason_record_exactly_one_event_and_one_time()
    {
        var scene = await SceneAsync(running: true);
        Result<AbandonManualRunCommandResult> winner = default!;
        await using var inner = fixture.CreateContext();
        var racing = new FaultInjectingDbContext(inner)
        {
            BeforeBeginTransaction = async _ => winner = await AbandonAsync(scene.RunId, now: Later),
        };

        var loser = await new AbandonManualRunCommandHandler(racing, new FixedTimeProvider(Later.AddMinutes(9)))
            .HandleAsync(new AbandonManualRunCommand(scene.RunId, AbandonmentRaceSupport.Reason), CancellationToken.None);

        Assert.True(winner.IsSuccess);
        Assert.True(loser.IsSuccess);
        Assert.Equal(winner.Value, loser.Value);
        Assert.Equal(Later, loser.Value.AbandonedAtUtc);
        Assert.Equal(1, (await scene.SnapshotAsync()).AbandonedEvents);
    }

    [Fact]
    public async Task Two_overlapping_requests_with_different_reasons_leave_the_first_reason_and_conflict_the_second()
    {
        var scene = await SceneAsync(running: true);
        Result<AbandonManualRunCommandResult> winner = default!;
        await using var inner = fixture.CreateContext();
        var racing = new FaultInjectingDbContext(inner)
        {
            BeforeBeginTransaction = async _ => winner = await AbandonAsync(scene.RunId, "The winning reason", now: Later),
        };

        var loser = await new AbandonManualRunCommandHandler(racing, new FixedTimeProvider(Later.AddMinutes(9)))
            .HandleAsync(new AbandonManualRunCommand(scene.RunId, "The losing reason"), CancellationToken.None);

        Assert.True(winner.IsSuccess);
        Assert.Equal(RunAbandonmentErrors.ReasonConflictCode, Code(loser));
        var snapshot = await scene.SnapshotAsync();
        Assert.Equal("The winning reason", snapshot.Reason);
        Assert.Equal(1, snapshot.AbandonedEvents);
    }

    // ---- fresh untracked authority --------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_active_attempt_check_reads_the_database_not_the_trackers_stale_copy()
    {
        var scene = await SceneAsync(running: true);
        var attemptId = await scene.AddAttemptAsync();
        await using var db = fixture.CreateContext();
        var staleTracked = await db.Attempts.SingleAsync(candidate => candidate.Id == attemptId);
        Assert.Equal(AttemptStatus.Running, staleTracked.Status);
        await scene.SqlAsync("UPDATE attempts SET Status = 'Completed' WHERE Id = {0}", attemptId);

        var result = await new AbandonManualRunCommandHandler(db, new FixedTimeProvider(Later))
            .HandleAsync(new AbandonManualRunCommand(scene.RunId, AbandonmentRaceSupport.Reason), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        Assert.Equal(AttemptStatus.Running, staleTracked.Status);
    }

    [Fact]
    public async Task A_completed_attempt_tracked_as_finished_is_refused_when_another_request_reopened_it()
    {
        var scene = await SceneAsync(running: true);
        var attemptId = await scene.AddAttemptAsync(attempt => attempt.Complete(AbandonmentScene.Created.AddSeconds(20)));
        await using var db = fixture.CreateContext();
        Assert.Equal(AttemptStatus.Completed, (await db.Attempts.SingleAsync(candidate => candidate.Id == attemptId)).Status);
        await scene.SqlAsync("UPDATE attempts SET Status = 'Running' WHERE Id = {0}", attemptId);
        var before = await scene.SnapshotAsync();

        var result = await new AbandonManualRunCommandHandler(db, new FixedTimeProvider(Later))
            .HandleAsync(new AbandonManualRunCommand(scene.RunId, AbandonmentRaceSupport.Reason), CancellationToken.None);

        Assert.Equal(RunAbandonmentErrors.ActiveAttemptCode, Code(result));
        Assert.Equal(before, await scene.SnapshotAsync());
    }

    [Fact]
    public async Task A_competing_local_commit_admission_committed_at_the_seam_makes_the_abandonment_refuse_and_write_nothing()
    {
        var scene = await SceneAsync(running: true, withWorkspace: true);
        var before = await scene.SnapshotAsync();
        await using var inner = fixture.CreateContext();
        var racing = new FaultInjectingDbContext(inner)
        {
            // An admission commits its operation and reserves the workspace strictly before the abandonment takes its write lock.
            BeforeBeginTransaction = async _ => await scene.AddLocalCommitOperationAsync(LocalCommitStatus.Prepared),
        };

        var result = await new AbandonManualRunCommandHandler(racing, new FixedTimeProvider(Later))
            .HandleAsync(new AbandonManualRunCommand(scene.RunId, AbandonmentRaceSupport.Reason), CancellationToken.None);

        Assert.Equal(RunAbandonmentErrors.LocalCommitOpenCode, Code(result));
        var after = await scene.SnapshotAsync();
        Assert.Equal(nameof(RunLifecycle.Running), after.Lifecycle);
        Assert.Null(after.Reason);
        Assert.Equal(0, after.AbandonedEvents);
    }

    [Fact]
    public async Task A_verification_execution_claimed_at_the_seam_makes_the_abandonment_refuse()
    {
        var scene = await SceneAsync(running: true, withWorkspace: true);
        await using var inner = fixture.CreateContext();
        var racing = new FaultInjectingDbContext(inner) { BeforeBeginTransaction = async _ => await scene.AddVerificationAsync() };

        var result = await new AbandonManualRunCommandHandler(racing, new FixedTimeProvider(Later))
            .HandleAsync(new AbandonManualRunCommand(scene.RunId, AbandonmentRaceSupport.Reason), CancellationToken.None);

        Assert.Equal(RunAbandonmentErrors.ActiveVerificationCode, Code(result));
        Assert.Equal(nameof(RunLifecycle.Running), (await scene.SnapshotAsync()).Lifecycle);
    }

    // ---- atomicity ----------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("events")]
    [InlineData("runs")]
    public async Task A_save_that_fails_after_part_of_the_batch_rolls_back_the_transition_and_the_event_together(string failingTable)
    {
        var scene = await SceneAsync(running: true);
        var before = await scene.SnapshotAsync();
        var trigger = failingTable == "events"
            ? "CREATE TRIGGER fail_abandon_event BEFORE INSERT ON events WHEN NEW.EventType = 'run.abandoned' BEGIN SELECT RAISE(ABORT, 'injected'); END;"
            : "CREATE TRIGGER fail_abandon_run BEFORE UPDATE ON runs WHEN NEW.Lifecycle = 'Abandoned' BEGIN SELECT RAISE(ABORT, 'injected'); END;";
        var name = failingTable == "events" ? "fail_abandon_event" : "fail_abandon_run";
        await scene.SqlAsync(trigger);
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => AbandonAsync(scene.RunId));
        }
        finally
        {
            await scene.SqlAsync($"DROP TRIGGER IF EXISTS {name}");
        }

        Assert.Equal(before, await scene.SnapshotAsync());
        var retry = await AbandonAsync(scene.RunId);
        Assert.True(retry.IsSuccess, retry.IsFailure ? Code(retry) : null);
        Assert.Equal(1, (await scene.SnapshotAsync()).AbandonedEvents);
    }

    // The write lock taken by the abandonment's first statement is what makes its decision atomic: while its save is pending, another
    // connection cannot change the run row at all, so nothing can slip between the fresh reads and the saved transition.
    [Fact]
#pragma warning disable EF1002
    public async Task No_competing_writer_can_change_the_run_between_the_decision_and_the_save()
    {
        var scene = await SceneAsync(running: true);
        Exception? competingWrite = null;
        await using var inner = fixture.CreateContext();
        var racing = new FaultInjectingDbContext(inner)
        {
            BeforeSaveChanges = async _ =>
            {
                await using var other = fixture.CreateContext();
                other.Database.SetCommandTimeout(1);
                competingWrite = await Record.ExceptionAsync(() => other.Database.ExecuteSqlRawAsync(
                    "UPDATE runs SET Lifecycle = 'Failed' WHERE Id = {0}", scene.RunId));
            },
        };

        var result = await new AbandonManualRunCommandHandler(racing, new FixedTimeProvider(Later))
            .HandleAsync(new AbandonManualRunCommand(scene.RunId, AbandonmentRaceSupport.Reason), CancellationToken.None);

        Assert.NotNull(competingWrite);
        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        Assert.Equal(nameof(RunLifecycle.Abandoned), (await scene.SnapshotAsync()).Lifecycle);
    }
#pragma warning restore EF1002

    private sealed class StatementRecorder : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public List<string> Statements { get; } = [];

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Statements.Add(command.CommandText);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Statements.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    // The abandonment's first statement is a write, so the database write lock is taken before anything is read and a competing
    // request waits for it instead of reading the same state and failing at its own save.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_first_statement_of_the_command_is_the_locking_write(bool refused)
    {
        var scene = await SceneAsync(running: true);
        if (refused)
        {
            await scene.AddAttemptAsync();
        }

        var recorder = new StatementRecorder();
        await using var db = fixture.CreateContext(recorder);

        await new AbandonManualRunCommandHandler(db, new FixedTimeProvider(Later))
            .HandleAsync(new AbandonManualRunCommand(scene.RunId, AbandonmentRaceSupport.Reason), CancellationToken.None);

        Assert.True(recorder.Statements.Count > 1);
        Assert.StartsWith("UPDATE \"runs\"", recorder.Statements[0], StringComparison.Ordinal);
    }

    // ---- notification -------------------------------------------------------------------------------------------------------------

    private sealed class RecordingNotifier(bool fail = false) : IRunEventNotifier
    {
        public List<(Guid RunId, long Sequence)> Notifications { get; } = [];

        public Task NotifyRunAdvancedAsync(Guid runId, long latestSequence, CancellationToken cancellationToken)
        {
            Notifications.Add((runId, latestSequence));
            return fail ? throw new InvalidOperationException("notifier down") : Task.CompletedTask;
        }
    }

    [Fact]
    public async Task The_committed_event_is_announced_once_and_a_failing_notifier_never_hides_the_recorded_abandonment()
    {
        var scene = await SceneAsync();
        var notifier = new RecordingNotifier(fail: true);

        var result = await AbandonAsync(scene.RunId, notifier: notifier);

        Assert.True(result.IsSuccess);
        var notification = Assert.Single(notifier.Notifications);
        Assert.Equal(scene.RunId, notification.RunId);
        await using var db = fixture.CreateContext();
        var recorded = await db.Events.AsNoTracking().SingleAsync(candidate => candidate.RunId == scene.RunId && candidate.EventType == RunEventType.RunAbandoned);
        Assert.Equal(recorded.Sequence, notification.Sequence);
    }

    [Fact]
    public async Task A_refusal_or_a_replay_announces_nothing()
    {
        var scene = await SceneAsync();
        var notifier = new RecordingNotifier();
        Assert.True((await AbandonAsync(scene.RunId, notifier: notifier)).IsSuccess);
        notifier.Notifications.Clear();

        Assert.True((await AbandonAsync(scene.RunId, notifier: notifier)).IsSuccess);
        Assert.True((await AbandonAsync(scene.RunId, "Different", notifier: notifier)).IsFailure);

        Assert.Empty(notifier.Notifications);
    }
}
