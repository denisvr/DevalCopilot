using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionOwnershipRelease;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionReleaseInspection;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionInspection;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionOwnershipReleases;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Data.Sqlite;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionReleaseInspection.LocalExecutionReleaseInspectionRig;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences.ScriptedResultKind;
using Rig = DevalCopilot.Application.Tests.Features.Runs.LocalExecutionOwnershipReleases.LocalExecutionOwnershipReleaseRig;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionReleaseInspection;

/// <summary>
/// ADR-0067 over real file-backed SQLite: the recorded release inspection proves a Human quarantine release with the same statement and
/// proof the candidate uses, for both operation families, all six step kinds and both admission modes, as exactly one read-only
/// statement that writes nothing and changes no ownership reading. Without a receipt it answers NotRecorded only for a supported proof
/// result (a candidate or a coherent ineligible history); missing producer evidence is Unavailable, and an absent or foreign run, step
/// or sequence is the one fixed inspection not-found. A host restart, a reopened database and a newer run change nothing.
/// </summary>
public sealed class LocalExecutionReleaseInspectionTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    public static TheoryData<Step, bool> Everything()
    {
        var data = new TheoryData<Step, bool>();
        foreach (var kind in Rig.AllKinds)
        {
            data.Add(kind, false);
            data.Add(kind, true);
        }

        return data;
    }

    // ---- Recorded ---------------------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Everything))]
    public async Task A_proved_release_is_recorded_with_its_complete_facts_in_one_read_only_statement(Step kind, bool queued)
    {
        var ended = await Rig.EndAsync(fixture, kind, queued);
        var scene = ended.Scene;
        var read = await scene.ReadReleaseAsync(ended.Target.Id);
        var facts = read.Candidate!.Facts;
        var released = await scene.ReleaseAsync(scene.ConfirmedCommand(read));
        Assert.True(released.IsSuccess, released.IsFailure ? released.Errors[0].Code : null);
        var ownership = await Rig.ReadOwnershipAsync(fixture, scene);
        var before = await FingerprintAsync(fixture, scene);
        var log = new StatementLog();

        var inspected = await InspectAsync(fixture, scene.RunId, ended.Target.Id, log);

        var because = $"kind={kind}, queued={queued}";
        Assert.Equal(LocalExecutionReleaseInspectionStatus.Recorded, inspected.Status);
        var receipt = inspected.Receipt!;
        Assert.Equal(facts, receipt.Facts);
        Assert.Equal(released.Value.ReleaseId, receipt.ReleaseId);
        Assert.Equal(released.Value.ReleasedAtUtc, receipt.ReleasedAtUtc);
        Assert.Equal(released.Value.EventSequence, receipt.EventSequence);
        Assert.Equal(kind, receipt.Facts.Kind);
        Assert.Equal(Rig.IsVerification(kind) ? LocalExecutionOperationFamily.Verification : LocalExecutionOperationFamily.Agent, receipt.Facts.Family);
        Assert.Equal(queued ? LocalExecutionAdmissionMode.WaitForAdmission : LocalExecutionAdmissionMode.Immediate, receipt.Facts.AdmissionMode);
        Assert.Equal(ended.Target.OperationId, receipt.Facts.OperationId);
        AssertOneReadOnlyStatement(log);
        Assert.Equal(before, await FingerprintAsync(fixture, scene));
        Assert.Equal(ownership, await Rig.ReadOwnershipAsync(fixture, scene));
        Assert.True(ownership.Workspace == Application.Features.Projects.Policies.WorkspaceExecutions.WorkspaceExecutionStatus.Idle, because);
    }

    [Fact]
    public async Task The_inspection_neither_mints_a_confirmation_nor_needs_a_clock_and_a_repeat_read_is_identical()
    {
        var ended = await Rig.EndAsync(fixture, Step.CodeReview, queued: false);
        var scene = ended.Scene;
        await scene.ReleaseStepAsync(ended.Target.Id);

        var first = await InspectAsync(fixture, scene.RunId, ended.Target.Id);
        scene.Clock.Advance(TimeSpan.FromHours(3));
        var second = await InspectAsync(fixture, scene.RunId, ended.Target.Id);

        Assert.Equal(first, second);
        Assert.Equal(typeof(GetLocalExecutionReleaseInspectionQueryResult).GetProperties().Select(property => property.Name).Order(),
            new[] { "Receipt", "RunId", "Status", "StepId" });
        Assert.DoesNotContain(
            typeof(LocalExecutionReleaseInspectionReceipt).GetProperties(),
            property => property.Name.Contains("Confirmation", StringComparison.Ordinal));
    }

    // ---- NotRecorded ------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("candidate")]
    [InlineData("already_free")]
    [InlineData("sequence_not_ended")]
    [InlineData("operation_not_terminal")]
    public async Task A_supported_history_without_any_receipt_is_not_recorded_and_says_nothing_more(string name)
    {
        var (script, ticks) = name switch
        {
            "candidate" => (Rig.Script(Step.Implementation), 120),
            "already_free" => (new LocalExecutionStepScript().On(Step.Implementation, ProviderFailedWithExit), 120),
            "sequence_not_ended" => (new LocalExecutionStepScript().On(Step.Implementation, ProviderTimedOutTerminated), 1),
            _ => (new LocalExecutionStepScript().On(Step.Implementation, LeaveRunning), 1),
        };
        var ended = await Rig.DriveAsync(fixture, script, maximumTicks: ticks);
        var scene = ended.Scene;
        var proved = await scene.ReadReleaseAsync(ended.Target.Id);
        Assert.Equal(
            name == "candidate" ? LocalExecutionOwnershipReleaseStatus.Available : LocalExecutionOwnershipReleaseStatus.NotEligible,
            proved.Status);
        var ownership = await Rig.ReadOwnershipAsync(fixture, scene);
        var before = await FingerprintAsync(fixture, scene);
        var log = new StatementLog();

        var inspected = await InspectAsync(fixture, scene.RunId, ended.Target.Id, log);

        Assert.Equal(LocalExecutionReleaseInspectionStatus.NotRecorded, inspected.Status);
        Assert.Null(inspected.Receipt);
        AssertOneReadOnlyStatement(log);
        Assert.Equal(before, await FingerprintAsync(fixture, scene));
        Assert.Equal(ownership, await Rig.ReadOwnershipAsync(fixture, scene));
    }

    [Fact]
    public async Task A_reservation_that_names_no_operation_is_not_recorded()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, 1);
        var plan = await scene.AddAcceptedRootPlanAsync();
        await scene.QueueAsync(plan.ProposalId);
        var ticket = await scene.ReserveAsync();
        Assert.NotNull(ticket);

        var inspected = await InspectAsync(fixture, scene.RunId, ticket!.StepId);

        Assert.Equal(LocalExecutionReleaseInspectionStatus.NotRecorded, inspected.Status);
        Assert.Null(inspected.Receipt);
    }

    // ---- Unavailable (never absence) ----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_producer_evidence_of_an_unreleased_operation_is_unavailable_not_absent(bool deleteObservation)
    {
        var ended = deleteObservation
            ? await Rig.EndAsync(fixture, Step.Implementation, queued: false)
            : await Rig.DriveAsync(
                fixture, new LocalExecutionStepScript().On(Step.Implementation, ProviderTimedOutUnobserved), maximumTicks: 120);
        var scene = ended.Scene;
        if (deleteObservation)
        {
            await LocalExecutionOwnershipReleaseTamper.ApplyAsync(
                fixture, "local_execution_termination_observations", "DELETE FROM local_execution_termination_observations");
        }

        var proved = await scene.ReadReleaseAsync(ended.Target.Id);
        Assert.Equal((LocalExecutionOwnershipReleaseStatus.Unavailable, "evidence_missing"), (proved.Status, proved.ReasonCode));

        var inspected = await InspectAsync(fixture, scene.RunId, ended.Target.Id);

        Assert.Equal(LocalExecutionReleaseInspectionStatus.Unavailable, inspected.Status);
        Assert.Null(inspected.Receipt);
    }

    // ---- NotFound ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_absent_or_foreign_run_step_or_sequence_is_one_fixed_inspection_not_found()
    {
        var ended = await Rig.EndAsync(fixture, Step.Implementation, queued: false);
        var scene = ended.Scene;
        await scene.ReleaseStepAsync(ended.Target.Id);
        var bare = await LocalExecutionSequenceTestScene.CreateAsync(fixture, 1);

        var results = new[]
        {
            await TryInspectAsync(fixture, Guid.NewGuid(), ended.Target.Id),
            await TryInspectAsync(fixture, scene.RunId, Guid.NewGuid()),
            await TryInspectAsync(fixture, bare.RunId, ended.Target.Id),
            await TryInspectAsync(fixture, bare.RunId, Guid.NewGuid()),
        };

        Assert.All(results, result =>
        {
            Assert.True(result.IsFailure);
            Assert.Equal(LocalExecutionInspectionErrors.NotFoundCode, Assert.Single(result.Errors).Code);
            Assert.Equal(LocalExecutionInspectionErrors.NotFound().Description, result.Errors[0].Description);
        });
    }

    // ---- Reopen and history -------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_host_restart_and_a_reopened_database_read_the_same_receipt()
    {
        var ended = await Rig.EndAsync(fixture, Step.Verification, queued: true);
        var scene = ended.Scene;
        await scene.ReleaseStepAsync(ended.Target.Id);
        var first = await InspectAsync(fixture, scene.RunId, ended.Target.Id);

        scene.RestartHost();
        SqliteConnection.ClearPool(new SqliteConnection(fixture.ConnectionString));
        var reopened = await InspectAsync(fixture, scene.RunId, ended.Target.Id);

        Assert.Equal(LocalExecutionReleaseInspectionStatus.Recorded, first.Status);
        Assert.Equal(first, reopened);
    }

    [Fact]
    public async Task An_older_selected_run_keeps_its_own_receipt_after_a_newer_run_is_released()
    {
        var older = await Rig.EndAsync(fixture, Step.Implementation, queued: false);
        await older.Scene.ReleaseStepAsync(older.Target.Id);
        var olderBefore = await InspectAsync(fixture, older.Scene.RunId, older.Target.Id);

        older.Scene.Clock.Advance(TimeSpan.FromHours(1));
        var newer = await Rig.EndAsync(fixture, Step.CodeReview, queued: true);
        await newer.Scene.ReleaseStepAsync(newer.Target.Id);

        var olderAfter = await InspectAsync(fixture, older.Scene.RunId, older.Target.Id);
        var newerRead = await InspectAsync(fixture, newer.Scene.RunId, newer.Target.Id);
        var crossed = await TryInspectAsync(fixture, older.Scene.RunId, newer.Target.Id);

        Assert.Equal(olderBefore, olderAfter);
        Assert.Equal(LocalExecutionReleaseInspectionStatus.Recorded, newerRead.Status);
        Assert.NotEqual(olderAfter.Receipt!.ReleaseId, newerRead.Receipt!.ReleaseId);
        Assert.Equal(older.Scene.RunId, olderAfter.Receipt.Facts.RunId);
        Assert.Equal(newer.Scene.RunId, newerRead.Receipt.Facts.RunId);
        Assert.True(crossed.IsFailure);
        Assert.Equal(LocalExecutionInspectionErrors.NotFoundCode, crossed.Errors[0].Code);
    }
}
