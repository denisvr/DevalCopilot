using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionOwnershipRelease;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionReleaseInspection;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionInspection;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionOwnershipReleases;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionReleaseInspection.LocalExecutionReleaseInspectionRig;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences.ScriptedResultKind;
using Rig = DevalCopilot.Application.Tests.Features.Runs.LocalExecutionOwnershipReleases.LocalExecutionOwnershipReleaseRig;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;
using Tamper = DevalCopilot.Application.Tests.Features.Runs.LocalExecutionOwnershipReleases.LocalExecutionOwnershipReleaseTamper;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionReleaseInspection;

/// <summary>
/// LRI-R1 residual (review 2): a NULLABLE numeric fact whose stored value is unsupported NONNULL storage (plain text, a fraction or an
/// integer beyond Int32) used to be projected as the legal null, so the in-memory proof compared a legal null with a legal null and the
/// inspection answered Recorded (the candidate reader Released) while the relational ownership expression and the SQL fence kept the
/// operation held. The projection now computes a fresh raw-storage validity in the same statement, keeping three cases apart (actual NULL,
/// a supported exact integer, unsupported nonnull storage), and the proof refuses unsupported storage before first-release eligibility or
/// receipt replay can classify it. Each damaged case, with an existing receipt (inspection, candidate and replay) and without one
/// (inspection, candidate and an attempted first confirmation), is refused without appending a release or event, repairing the value or
/// changing the ownership readings and the SQL fence. Guards are suspended only in this disposable database.
/// </summary>
public sealed class LocalExecutionReleaseInspectionNullableIntegerTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    /// <summary>The identities the damage targets, read without requiring a release (the first-confirmation cases have none).</summary>
    private sealed record Targets(Guid WitnessId, Guid ObservationId, Guid OperationId, Guid? AdmissionId);

    private sealed record Site(string Table, string Column, Step Kind, bool Queued, Func<Targets, Guid> Key);

    private static readonly Dictionary<string, Site> Sites = new()
    {
        ["observation-semantic-exit-code-agent"] = new("local_execution_termination_observations", "SemanticExitCode", Step.Implementation, false, t => t.ObservationId),
        ["observation-semantic-exit-code-verification"] = new("local_execution_termination_observations", "SemanticExitCode", Step.Verification, false, t => t.ObservationId),
        ["attempt-process-exit-code"] = new("attempts", "AgentProcessExitCode", Step.Implementation, false, t => t.OperationId),
        ["execution-exit-code"] = new("verification_executions", "ExitCode", Step.Verification, false, t => t.OperationId),
        ["witness-recipe-ordinal"] = new("local_execution_dispatch_witnesses", "RecipeOrdinal", Step.Implementation, false, t => t.WitnessId),
        // The audit of the same projection found the queued admission's nullable recipe ordinal collapsing the same way.
        ["admission-recipe-ordinal"] = new("local_execution_admissions", "RecipeOrdinal", Step.Implementation, true, t => t.AdmissionId!.Value),
        // Representative queued cases of the two Agent sites that exist in both admission modes.
        ["observation-semantic-exit-code-agent-queued"] = new("local_execution_termination_observations", "SemanticExitCode", Step.Implementation, true, t => t.ObservationId),
        ["attempt-process-exit-code-queued"] = new("attempts", "AgentProcessExitCode", Step.CodeReview, true, t => t.OperationId),
    };

    private static readonly Dictionary<string, string> Forms = new()
    {
        ["plain-text"] = "'not-a-number'",
        ["fraction"] = "0.5",
        ["above-int32"] = "2147483648",
    };

    public static TheoryData<string, string, bool> Cases
    {
        get
        {
            var data = new TheoryData<string, string, bool>();
            foreach (var site in Sites.Keys)
            {
                foreach (var form in Forms.Keys)
                {
                    data.Add(site, form, true);
                    data.Add(site, form, false);
                }
            }

            return data;
        }
    }

    private async Task<Targets> ReadTargetsAsync(LocalExecutionSequenceStep target)
    {
        await using var context = fixture.CreateContext();
        var witness = await context.LocalExecutionDispatchWitnesses.AsNoTracking().SingleAsync(candidate => candidate.StepId == target.Id);
        var observation = await context.LocalExecutionTerminationObservations.AsNoTracking().SingleAsync(candidate => candidate.WitnessId == witness.Id);
        return new Targets(witness.Id, observation.Id, witness.OperationId, witness.AdmissionId);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task An_unsupported_nonnull_value_of_a_nullable_numeric_fact_is_never_read_as_a_legal_null(string name, string form, bool released)
    {
        var site = Sites[name];
        var ended = await Rig.EndAsync(fixture, site.Kind, site.Queued);
        var scene = ended.Scene;
        var confirmed = scene.ConfirmedCommand(await scene.ReadReleaseAsync(ended.Target.Id));
        if (released)
        {
            await scene.ReleaseStepAsync(ended.Target.Id);
        }

        var key = site.Key(await ReadTargetsAsync(ended.Target));
        await Tamper.ApplyWithoutCheckConstraintsAsync(
            fixture, site.Table, $"UPDATE {site.Table} SET \"{site.Column}\" = {Forms[form]} WHERE \"Id\" = {{0}}", key);
        var because = $"{name}/{form}/{(released ? "receipt" : "no receipt")}";
        var ownership = await Rig.ReadOwnershipAsync(fixture, scene);
        var fenceBlocked = await Tamper.ProbeIsBlockedAsync(fixture, scene.ProjectId);
        var abandonmentBlocked = await Tamper.ProbeRunAbandonmentIsBlockedAsync(fixture, scene.RunId);
        var before = await FingerprintAsync(fixture, scene);
        var releasesBefore = (await scene.ReadReleasesAsync()).Count;
        var eventsBefore = await scene.CountEventsAsync(RunEventType.LocalExecutionOwnershipReleased);

        // The relational ownership expression and the SQL fence keep the operation held: the readings must not contradict them.
        Assert.True(ownership.Workspace != WorkspaceExecutionStatus.Idle, $"workspace must be held ({because}): {ownership}");
        Assert.True(fenceBlocked, $"the SQL live fence must hold ({because})");
        Assert.True(abandonmentBlocked, $"abandonment must stay blocked ({because})");

        var log = new StatementLog();
        var inspected = await InspectAsync(fixture, scene.RunId, ended.Target.Id, log);
        var candidate = await scene.ReadReleaseAsync(ended.Target.Id);
        var attempt = await scene.ReleaseAsync(confirmed);

        // Never NotRecorded either: unsupported evidence is unavailable, not an absence.
        Assert.True(inspected.Status == LocalExecutionReleaseInspectionStatus.Unavailable, $"{because}: inspected {inspected.Status}");
        Assert.Null(inspected.Receipt);
        AssertOneReadOnlyStatement(log);
        Assert.True(candidate.Status == LocalExecutionOwnershipReleaseStatus.Unavailable, $"{because}: candidate {candidate.Status}");
        Assert.Null(candidate.Candidate);
        Assert.Null(candidate.Release);
        Assert.True(attempt.IsFailure, $"{because}: the {(released ? "replay" : "first confirmation")} must be refused");
        Assert.Equal(LocalExecutionOwnershipReleaseErrors.UnavailableCode, attempt.Errors[0].Code);
        // No receipt, event or repair, and nothing else changed.
        Assert.Equal(releasesBefore, (await scene.ReadReleasesAsync()).Count);
        Assert.Equal(eventsBefore, await scene.CountEventsAsync(RunEventType.LocalExecutionOwnershipReleased));
        Assert.Equal(before, await FingerprintAsync(fixture, scene));
        Assert.Equal(ownership, await Rig.ReadOwnershipAsync(fixture, scene));
        Assert.Equal(fenceBlocked, await Tamper.ProbeIsBlockedAsync(fixture, scene.ProjectId));
        Assert.Equal(abandonmentBlocked, await Tamper.ProbeRunAbandonmentIsBlockedAsync(fixture, scene.RunId));
    }

    // ---- controls -------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_nulls_stay_supported_a_candidate_before_and_a_recorded_receipt_after_the_release(bool released)
    {
        var ended = await Rig.EndAsync(fixture, Step.Implementation, queued: false);
        var scene = ended.Scene;
        if (released)
        {
            await scene.ReleaseStepAsync(ended.Target.Id);
        }

        var inspected = await InspectAsync(fixture, scene.RunId, ended.Target.Id);
        var candidate = await scene.ReadReleaseAsync(ended.Target.Id);

        // Every nullable numeric fact of this history (the semantic and exit codes of a timed-out Agent, its recipe ordinals) is a real NULL.
        Assert.Equal(released ? LocalExecutionReleaseInspectionStatus.Recorded : LocalExecutionReleaseInspectionStatus.NotRecorded, inspected.Status);
        Assert.Equal(released ? LocalExecutionOwnershipReleaseStatus.Released : LocalExecutionOwnershipReleaseStatus.Available, candidate.Status);
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-int.MaxValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public async Task Every_valid_int32_exit_code_stays_supported_on_an_already_free_operation(int exitCode)
    {
        var ended = await Rig.DriveAsync(fixture, new LocalExecutionStepScript().On(Step.Implementation, ProviderFailedWithExit), maximumTicks: 120);
        var scene = ended.Scene;
        await Tamper.ApplyWithoutCheckConstraintsAsync(
            fixture, "attempts", "UPDATE attempts SET \"AgentProcessExitCode\" = {1} WHERE \"Id\" = {0}", ended.Target.OperationId!.Value, exitCode);

        var inspected = await InspectAsync(fixture, scene.RunId, ended.Target.Id);
        var candidate = await scene.ReadReleaseAsync(ended.Target.Id);

        // An operation with ordinary Exited proof is already free: a valid code, whatever its value, never makes it unavailable.
        Assert.Equal(LocalExecutionReleaseInspectionStatus.NotRecorded, inspected.Status);
        Assert.Equal(LocalExecutionOwnershipReleaseStatus.NotEligible, candidate.Status);
        Assert.Equal("already_free", candidate.ReasonCode);
    }
}
