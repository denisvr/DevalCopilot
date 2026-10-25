using DevalCopilot.Application.Features.Runs.Commands.ReleaseLocalExecutionOwnership;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionOwnershipRelease;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences.ScriptedResultKind;
using Rig = DevalCopilot.Application.Tests.Features.Runs.LocalExecutionOwnershipReleases.LocalExecutionOwnershipReleaseRig;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionOwnershipReleases;

/// <summary>
/// ADR-0065 eligibility and the one Human command, over real SQLite: only a recognized terminal linked operation that still retains
/// quarantine for missing ordinary Exited proof, on an ended sequence, with complete original-host evidence, is Available. Everything
/// else is a fixed NotEligible or Unavailable answer; the hold stays; a stale, mismatched, expired or over-long confirmation is refused
/// without retargeting; and an uncertain commit is never inferred or resubmitted.
/// </summary>
public sealed class LocalExecutionOwnershipReleaseEligibilityTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    // ---- the closed read -------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("operation_not_terminal")]
    [InlineData("claimed_undispatched_is_still_running")]
    [InlineData("dispatch_refused")]
    [InlineData("already_free")]
    [InlineData("sequence_not_ended")]
    [InlineData("evidence_missing_no_exit")]
    [InlineData("evidence_missing_timeout_unobserved")]
    [InlineData("evidence_missing_verification")]
    public async Task A_history_that_is_not_a_complete_terminal_quarantine_is_never_available(string name)
    {
        var (script, ticks) = name switch
        {
            "operation_not_terminal" => (new LocalExecutionStepScript().On(Step.Implementation, LeaveRunning), 1),
            "claimed_undispatched_is_still_running" => (new LocalExecutionStepScript().On(Step.Implementation, LeaveUndispatched), 1),
            "dispatch_refused" => (new LocalExecutionStepScript().On(Step.Implementation, LeaveUndispatched), 1),
            "already_free" => (new LocalExecutionStepScript().On(Step.Implementation, ProviderFailedWithExit), 120),
            "sequence_not_ended" => (new LocalExecutionStepScript().On(Step.Implementation, ProviderTimedOutTerminated), 1),
            "evidence_missing_no_exit" => (new LocalExecutionStepScript().On(Step.Implementation, ProviderFailedNoExit), 120),
            "evidence_missing_timeout_unobserved" => (new LocalExecutionStepScript().On(Step.Implementation, ProviderTimedOutUnobserved), 120),
            _ => (new LocalExecutionStepScript().Verify(1, 0, VerificationTimedOut), 120),
        };
        var ended = await Rig.DriveAsync(fixture, script, maximumTicks: ticks);
        if (name == "dispatch_refused")
        {
            // The sequence authority ends before dispatch: the dedicated refusal closes the claimed, never-dispatched operation.
            await ended.Scene.StopAsync(ended.Sequence.Id);
            ended.Scene.Clock.Advance(TimeSpan.FromHours(1));
            await ended.Scene.ObserveAsync(ended.Sequence.Id);
            Assert.True((await ended.Scene.RecordAgentDispatchRefusalAsync(ended.Target.OperationId!.Value)).IsSuccess);
        }

        var read = await ended.Scene.ReadReleaseAsync(ended.Target.Id);

        var expected = name switch
        {
            "operation_not_terminal" => (LocalExecutionOwnershipReleaseStatus.NotEligible, "operation_not_terminal"),
            "claimed_undispatched_is_still_running" => (LocalExecutionOwnershipReleaseStatus.NotEligible, "operation_not_terminal"),
            "dispatch_refused" => (LocalExecutionOwnershipReleaseStatus.NotEligible, "not_dispatched"),
            "already_free" => (LocalExecutionOwnershipReleaseStatus.NotEligible, "already_free"),
            "sequence_not_ended" => (LocalExecutionOwnershipReleaseStatus.NotEligible, "sequence_not_ended"),
            _ => (LocalExecutionOwnershipReleaseStatus.Unavailable, "evidence_missing"),
        };
        Assert.Equal(expected, (read.Status, read.ReasonCode));
        Assert.Null(read.Candidate);
        Assert.Null(read.Release);
        var refused = await ended.Scene.ReleaseAsync(new ReleaseLocalExecutionOwnershipCommand(
            ended.Scene.RunId, ended.Target.Id, Guid.NewGuid(), 1, new string('0', 64), ended.Scene.Clock.GetUtcNow().AddMinutes(1)));
        Assert.Equal(
            expected.Item1 == LocalExecutionOwnershipReleaseStatus.Unavailable
                ? LocalExecutionOwnershipReleaseErrors.UnavailableCode
                : LocalExecutionOwnershipReleaseErrors.NotEligibleCode,
            refused.Errors[0].Code);
        Assert.Empty(await ended.Scene.ReadReleasesAsync());
        Assert.Equal(0, await ended.Scene.CountEventsAsync(RunEventType.LocalExecutionOwnershipReleased));
    }

    [Fact]
    public async Task A_reservation_and_an_unresolved_preparation_own_no_operation_and_are_never_released_here()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, 1);
        var plan = await scene.AddAcceptedRootPlanAsync();
        await scene.QueueAsync(plan.ProposalId);
        var ticket = await scene.ReserveAsync();
        Assert.NotNull(ticket);

        var reserved = await scene.ReadReleaseAsync(ticket!.StepId);
        Assert.Equal((LocalExecutionOwnershipReleaseStatus.NotEligible, "step_not_linked"), (reserved.Status, reserved.ReasonCode));
        Assert.Equal(WorkspaceStatusProbe.Held, await WorkspaceStatusProbe.ReadAsync(scene));

        // A contradictory shape that no honest claim produces cannot be proved unused, so its reservation stays held as Unresolved.
        await scene.PlantReservedOperationAsync(ticket);
        await scene.ResolveAsync(ticket, LocalExecutionSequenceErrors.PersistenceUnresolvedCode);
        var unresolved = await scene.ReadReleaseAsync(ticket.StepId);
        Assert.Equal((LocalExecutionOwnershipReleaseStatus.NotEligible, "step_not_linked"), (unresolved.Status, unresolved.ReasonCode));
        Assert.Equal(WorkspaceStatusProbe.Held, await WorkspaceStatusProbe.ReadAsync(scene));
        Assert.Empty(await scene.ReadReleasesAsync());
    }

    [Fact]
    public async Task An_absent_or_foreign_run_or_step_is_one_fixed_not_found()
    {
        var ended = await Rig.EndAsync(fixture, Step.Implementation, queued: false);
        var scene = ended.Scene;
        var bare = await LocalExecutionSequenceTestScene.CreateAsync(fixture, 1);

        var results = new[]
        {
            await scene.TryReadReleaseAsync(Guid.NewGuid(), ended.Target.Id),
            await scene.TryReadReleaseAsync(scene.RunId, Guid.NewGuid()),
            await scene.TryReadReleaseAsync(bare.RunId, ended.Target.Id),
            await scene.TryReadReleaseAsync(bare.RunId, Guid.NewGuid()),
        };

        Assert.All(results, result =>
        {
            Assert.True(result.IsFailure);
            Assert.Equal(LocalExecutionOwnershipReleaseErrors.NotFoundCode, result.Errors[0].Code);
        });
        var command = scene.ConfirmedCommand(await scene.ReadReleaseAsync(ended.Target.Id));
        Assert.Equal(LocalExecutionOwnershipReleaseErrors.NotFoundCode, (await scene.ReleaseAsync(command with { RunId = bare.RunId })).Errors[0].Code);
        Assert.Equal(LocalExecutionOwnershipReleaseErrors.NotFoundCode, (await scene.ReleaseAsync(command with { StepId = Guid.NewGuid() })).Errors[0].Code);
        Assert.Empty(await scene.ReadReleasesAsync());
    }

    [Fact]
    public async Task A_pre_feature_operation_without_original_host_evidence_stays_held_and_unavailable()
    {
        var ended = await Rig.EndAsync(fixture, Step.Implementation, queued: false);
        var scene = ended.Scene;
        await LocalExecutionOwnershipReleaseTamper.ApplyAsync(
            fixture, "local_execution_termination_observations", "DELETE FROM local_execution_termination_observations");
        await LocalExecutionOwnershipReleaseTamper.ApplyAsync(
            fixture, "local_execution_dispatch_witnesses", "DELETE FROM local_execution_dispatch_witnesses");

        var read = await scene.ReadReleaseAsync(ended.Target.Id);

        Assert.Equal((LocalExecutionOwnershipReleaseStatus.Unavailable, "evidence_missing"), (read.Status, read.ReasonCode));
        Assert.NotEqual(
            Application.Features.Projects.Policies.WorkspaceExecutions.WorkspaceExecutionStatus.Idle,
            (await Rig.ReadOwnershipAsync(fixture, scene)).Workspace);
    }

    // ---- the exact confirmation -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_candidate_confirmation_expires_within_two_minutes_and_names_the_exact_versioned_digest()
    {
        var ended = await Rig.EndAsync(fixture, Step.Implementation, queued: false);
        var scene = ended.Scene;

        var read = await scene.ReadReleaseAsync(ended.Target.Id);

        var candidate = read.Candidate!;
        Assert.Equal(1, candidate.ConfirmationVersion);
        Assert.True(LocalExecutionSequencePolicy.IsLowercaseSha256(candidate.ConfirmationSha256));
        Assert.True(candidate.ConfirmationExpiresAtUtc > scene.Clock.GetUtcNow());
        Assert.True(candidate.ConfirmationExpiresAtUtc - scene.Clock.GetUtcNow() <= TimeSpan.FromMinutes(2));
        Assert.Equal(TimeSpan.Zero, candidate.ConfirmationExpiresAtUtc.Offset);
        Assert.Equal(
            candidate.ConfirmationSha256,
            LocalExecutionOwnershipReleaseConfirmation.Digest(candidate.Facts, candidate.ConfirmationExpiresAtUtc));
    }

    public static TheoryData<string> StaleConfirmations =>
        ["wrong-digest", "wrong-version", "wrong-observation", "digest-of-another-expiry", "expired", "expiry-beyond-two-minutes", "expiry-now"];

    [Theory]
    [MemberData(nameof(StaleConfirmations))]
    public async Task A_stale_mismatched_or_expired_confirmation_is_refused_without_retargeting(string name)
    {
        var ended = await Rig.EndAsync(fixture, Step.Implementation, queued: false);
        var scene = ended.Scene;
        var read = await scene.ReadReleaseAsync(ended.Target.Id);
        var command = scene.ConfirmedCommand(read);
        var now = scene.Clock.GetUtcNow();
        var facts = read.Candidate!.Facts;
        var farExpiry = now.AddMinutes(10);

        var mutated = name switch
        {
            "wrong-digest" => command with { ConfirmationSha256 = new string('a', 64) },
            "wrong-version" => command with { ConfirmationVersion = 2 },
            "wrong-observation" => command with { ObservationId = Guid.NewGuid() },
            "digest-of-another-expiry" => command with { ConfirmationExpiresAtUtc = command.ConfirmationExpiresAtUtc.AddSeconds(-30) },
            "expired" => command with { ConfirmationExpiresAtUtc = now.AddSeconds(-1) },
            "expiry-beyond-two-minutes" => command with
            {
                ConfirmationExpiresAtUtc = farExpiry,
                ConfirmationSha256 = LocalExecutionOwnershipReleaseConfirmation.Digest(facts, farExpiry),
            },
            _ => command with
            {
                ConfirmationExpiresAtUtc = now,
                ConfirmationSha256 = LocalExecutionOwnershipReleaseConfirmation.Digest(facts, now),
            },
        };

        var refused = await scene.ReleaseAsync(mutated);

        Assert.True(refused.IsFailure);
        Assert.Equal(LocalExecutionOwnershipReleaseErrors.ConfirmationStaleCode, refused.Errors[0].Code);
        Assert.Empty(await scene.ReadReleasesAsync());
        Assert.Equal(0, await scene.CountEventsAsync(RunEventType.LocalExecutionOwnershipReleased));
        Assert.Equal(LocalExecutionOwnershipReleaseStatus.Available, (await scene.ReadReleaseAsync(ended.Target.Id)).Status);
    }

    [Fact]
    public async Task A_confirmation_that_expired_is_refused_and_a_fresh_read_gives_a_fresh_one()
    {
        var ended = await Rig.EndAsync(fixture, Step.Implementation, queued: false);
        var scene = ended.Scene;
        var command = scene.ConfirmedCommand(await scene.ReadReleaseAsync(ended.Target.Id));

        scene.Clock.Advance(TimeSpan.FromMinutes(2));
        var refused = await scene.ReleaseAsync(command);
        Assert.Equal(LocalExecutionOwnershipReleaseErrors.ConfirmationStaleCode, refused.Errors[0].Code);

        var fresh = await scene.ReleaseAsync(scene.ConfirmedCommand(await scene.ReadReleaseAsync(ended.Target.Id)));
        Assert.True(fresh.IsSuccess, fresh.IsFailure ? fresh.Errors[0].Code : null);
        Assert.Single(await scene.ReadReleasesAsync());
    }

    [Fact]
    public async Task The_command_validator_refuses_malformed_values_before_any_work()
    {
        var validator = new ReleaseLocalExecutionOwnershipCommandValidator();
        var valid = new ReleaseLocalExecutionOwnershipCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, new string('a', 64), new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero));
        Assert.True((await validator.ValidateAsync(valid)).IsValid);

        var invalid = new[]
        {
            valid with { RunId = Guid.Empty },
            valid with { StepId = Guid.Empty },
            valid with { ObservationId = Guid.Empty },
            valid with { ConfirmationVersion = 0 },
            valid with { ConfirmationVersion = 2 },
            valid with { ConfirmationSha256 = new string('A', 64) },
            valid with { ConfirmationSha256 = new string('a', 63) },
            valid with { ConfirmationExpiresAtUtc = new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.FromHours(1)) },
        };
        foreach (var command in invalid)
        {
            Assert.False((await validator.ValidateAsync(command)).IsValid);
        }
    }

    // ---- commit certainty ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_save_that_applied_nothing_is_a_certain_failure_and_changes_nothing()
    {
        var ended = await Rig.EndAsync(fixture, Step.Implementation, queued: false);
        var scene = ended.Scene;
        var command = scene.ConfirmedCommand(await scene.ReadReleaseAsync(ended.Target.Id));
        await using var real = fixture.CreateContext();
        var faulty = new FaultInjectingDbContext(real)
        {
            SaveChangesFailure = FaultInjectingDbContext.SaveChangesFailureMode.UpdateExceptionBeforeSave,
        };

        var refused = await scene.ReleaseAsync(command, faulty);

        Assert.True(refused.IsFailure);
        Assert.Equal(LocalExecutionOwnershipReleaseErrors.PersistenceFailedCode, refused.Errors[0].Code);
        Assert.Empty(await scene.ReadReleasesAsync());
        Assert.Equal(0, await scene.CountEventsAsync(RunEventType.LocalExecutionOwnershipReleased));
        Assert.True((await scene.ReleaseAsync(command)).IsSuccess);
    }

    [Theory]
    [InlineData(FaultInjectingDbContext.CommitFailureMode.BeforeCommit, false)]
    [InlineData(FaultInjectingDbContext.CommitFailureMode.AfterCommit, true)]
    [InlineData(FaultInjectingDbContext.CommitFailureMode.CancellationBeforeCommit, false)]
    [InlineData(FaultInjectingDbContext.CommitFailureMode.CancellationAfterCommit, true)]
    public async Task An_uncertain_commit_is_reported_unresolved_and_a_fresh_read_decides_never_an_inference(
        FaultInjectingDbContext.CommitFailureMode mode, bool durable)
    {
        var ended = await Rig.EndAsync(fixture, Step.Implementation, queued: false);
        var scene = ended.Scene;
        var command = scene.ConfirmedCommand(await scene.ReadReleaseAsync(ended.Target.Id));
        await using var real = fixture.CreateContext();
        var faulty = new FaultInjectingDbContext(real) { CommitFailure = mode };

        var outcome = await scene.ReleaseAsync(command, faulty);

        Assert.True(outcome.IsFailure);
        Assert.Equal(LocalExecutionOwnershipReleaseErrors.PersistenceUnresolvedCode, outcome.Errors[0].Code);
        var read = await scene.ReadReleaseAsync(ended.Target.Id);
        Assert.Equal(durable ? LocalExecutionOwnershipReleaseStatus.Released : LocalExecutionOwnershipReleaseStatus.Available, read.Status);
        Assert.Equal(durable ? 1 : 0, (await scene.ReadReleasesAsync()).Count);
        Assert.Equal(durable ? 1 : 0, await scene.CountEventsAsync(RunEventType.LocalExecutionOwnershipReleased));

        // Only an explicit fresh request settles it: a durable release replays, a lost one is made now, never twice.
        var settled = await scene.ReleaseAsync(durable ? command : scene.ConfirmedCommand(read));
        Assert.True(settled.IsSuccess, settled.IsFailure ? settled.Errors[0].Code : null);
        Assert.Single(await scene.ReadReleasesAsync());
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.LocalExecutionOwnershipReleased));
    }

    /// <summary>The workspace reading reduced to held or idle, for the cases where no operation exists to own it.</summary>
    private static class WorkspaceStatusProbe
    {
        public const string Held = "held";

        public static async Task<string> ReadAsync(LocalExecutionSequenceTestScene scene) =>
            (await scene.AssessWorkspaceAsync()).Status == Application.Features.Projects.Policies.WorkspaceExecutions.WorkspaceExecutionStatus.Idle
                ? "idle"
                : Held;
    }
}
