using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionOwnershipRelease;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionReleaseInspection;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionInspection;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionOwnershipReleases;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionReleaseInspection.LocalExecutionReleaseInspectionRig;
using Rig = DevalCopilot.Application.Tests.Features.Runs.LocalExecutionOwnershipReleases.LocalExecutionOwnershipReleaseRig;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;
using Tamper = DevalCopilot.Application.Tests.Features.Runs.LocalExecutionOwnershipReleases.LocalExecutionOwnershipReleaseTamper;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionReleaseInspection;

/// <summary>
/// LRI-R1: every numeric member of the typed intent snapshot is admitted by the shared release projection BEFORE the provider can convert
/// it. A stored value that is not exactly an integer of the supported range (a numeric prefix followed by text, plain text such as the
/// original zero-pass case, a fraction, an out-of-range integer or a REAL beyond Int64) used to be normalized to a plausible number, so
/// the in-memory proof read the old value and the recorded inspection answered Recorded while the relational ownership expression and
/// the SQL fence kept the operation held. Each damaged case now leaves the inspection, the candidate reading and a replay refusing the
/// receipt, adds no receipt or event, repairs nothing, and changes neither the ownership readings nor the live SQL fence. Guards are
/// suspended only in this disposable database; no normal-host corruption or ownership bypass is claimed. Valid boundary values, a
/// well-formed receipt and ADR-0066's checksum-only mismatch stay controls.
/// </summary>
public sealed class LocalExecutionReleaseInspectionNumericIntentTests : IAsyncLifetime
{
    private const string Table = "local_execution_ownership_releases";

    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    /// <summary>One numeric intent member: its column, the kind of scene that stores a value for it, whether it is stored as ticks, the
    /// smallest out-of-domain value above its table range and the smallest one below it (SQL literals).</summary>
    private sealed record Field(
        string Column, Step Kind, string Above, string Below, bool Ticks = false, string Target = Table, Func<Tamper.Ids, Guid>? Key = null);

    private static readonly Dictionary<string, Field> Fields = new()
    {
        ["version"] = new("IntentVersion", Step.Implementation, "2", "0"),
        ["confirmation-version"] = new("IntentConfirmationVersion", Step.Implementation, "2", "0"),
        ["ordinal"] = new("IntentOrdinal", Step.CodeReview, "102", "-1"),
        ["pass"] = new("IntentPassNumber", Step.Implementation, "4", "-1"),
        ["recipe-ordinal"] = new("IntentRecipeOrdinal", Step.Verification, "32", "-1"),
        ["physical-exit-code"] = new("IntentPhysicalExitCode", Step.Implementation, "2147483648", "-2147483649"),
        ["process-duration"] = new("IntentProcessDuration", Step.Implementation, "9300000000000000000.0", "-1", Ticks: true),
        ["observed-elapsed"] = new("IntentObservedElapsed", Step.Implementation, "9300000000000000000.0", "-1", Ticks: true),
        ["semantic-exit-code"] = new("IntentSemanticExitCode", Step.Implementation, "2147483648", "-2147483649"),
        // The same stored-number defect at the sibling tick members the one release proof reads: the observation and the Agent attempt.
        ["observation-process-duration"] = new("ProcessDuration", Step.Implementation, "9300000000000000000.0", "-1", true, "local_execution_termination_observations", ids => ids.ObservationId),
        ["observation-observed-elapsed"] = new("ObservedElapsed", Step.Implementation, "9300000000000000000.0", "-1", true, "local_execution_termination_observations", ids => ids.ObservationId),
        ["attempt-process-duration"] = new("AgentProcessDuration", Step.Implementation, "9300000000000000000.0", "-1", true, "attempts", ids => ids.OperationId),
    };

    private static readonly string[] Forms = ["numeric-prefix-text", "plain-text", "fraction", "above-range", "below-range"];

    public static TheoryData<string, string> Cases
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var member in Fields.Keys)
            {
                foreach (var form in Forms)
                {
                    data.Add(member, form);
                }
            }

            return data;
        }
    }

    private static string Value(Field field, string form) => form switch
    {
        // The original decimal text followed by junk (an absent original is treated as zero), exactly the reviewer's six probes.
        "numeric-prefix-text" => $"COALESCE(CAST(\"{field.Column}\" AS TEXT), '0') || 'junk'",
        "plain-text" => "'not-a-number'",
        "fraction" => $"COALESCE(CAST(\"{field.Column}\" AS REAL), 0) + 0.5",
        "above-range" => field.Above,
        _ => field.Below,
    };

    private async Task<(string Type, string Text)> StoredAsync(string table, Guid id, string column)
    {
        await using var context = fixture.CreateContext();
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
#pragma warning disable CA2100 // The column name is a constant of this test and the database is disposable.
        command.CommandText = $"SELECT typeof(\"{column}\"), CAST(\"{column}\" AS TEXT) FROM {table} WHERE \"Id\" = $id;";
#pragma warning restore CA2100
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$id";
        parameter.Value = id.ToString().ToUpperInvariant();
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.IsDBNull(1) ? "<null>" : reader.GetString(1));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_numeric_intent_member_that_is_not_exactly_a_supported_integer_is_refused_by_the_inspection_the_candidate_and_a_replay(
        string name, string form)
    {
        var field = Fields[name];
        var ended = await Rig.EndAsync(fixture, field.Kind, queued: false);
        var scene = ended.Scene;
        var confirmed = scene.ConfirmedCommand(await scene.ReadReleaseAsync(ended.Target.Id));
        await scene.ReleaseStepAsync(ended.Target.Id);
        var ids = await Tamper.ReadIdsAsync(fixture, scene, ended.Target);
        var target = field.Key?.Invoke(ids) ?? ids.ReleaseId;
        await Tamper.ApplyWithoutCheckConstraintsAsync(
            fixture, field.Target, $"UPDATE {field.Target} SET \"{field.Column}\" = {Value(field, form)} WHERE \"Id\" = {{0}}", target);
        var because = $"{name}/{form}";
        var stored = await StoredAsync(field.Target, target, field.Column);
        var ownership = await Rig.ReadOwnershipAsync(fixture, scene);
        var fenceBlocked = await Tamper.ProbeIsBlockedAsync(fixture, scene.ProjectId);
        var abandonmentBlocked = await Tamper.ProbeRunAbandonmentIsBlockedAsync(fixture, scene.RunId);
        var before = await FingerprintAsync(fixture, scene);

        // The relational ownership expression and the SQL fence already keep the operation held: the reading must not contradict them.
        Assert.True(ownership.Workspace != WorkspaceExecutionStatus.Idle, $"workspace must be held ({because}): {ownership}");
        Assert.True(fenceBlocked, $"the SQL live fence must hold ({because})");
        Assert.True(abandonmentBlocked, $"abandonment must stay blocked ({because})");

        var log = new StatementLog();
        var inspected = await InspectAsync(fixture, scene.RunId, ended.Target.Id, log);
        var candidate = await scene.ReadReleaseAsync(ended.Target.Id);
        var replay = await scene.ReleaseAsync(confirmed);

        Assert.True(inspected.Status == LocalExecutionReleaseInspectionStatus.Unavailable, $"{because}: inspected {inspected.Status}");
        Assert.Null(inspected.Receipt);
        AssertOneReadOnlyStatement(log);
        Assert.True(candidate.Status == LocalExecutionOwnershipReleaseStatus.Unavailable, $"{because}: candidate {candidate.Status}");
        Assert.Null(candidate.Candidate);
        Assert.Null(candidate.Release);
        Assert.True(replay.IsFailure, $"{because}: the replay must be refused");
        Assert.Equal(LocalExecutionOwnershipReleaseErrors.UnavailableCode, replay.Errors[0].Code);
        // No receipt, event or repair: the damaged value is exactly as stored and nothing else changed.
        Assert.Equal(stored, await StoredAsync(field.Target, target, field.Column));
        Assert.Equal(before, await FingerprintAsync(fixture, scene));
        Assert.Equal(ownership, await Rig.ReadOwnershipAsync(fixture, scene));
        Assert.Equal(fenceBlocked, await Tamper.ProbeIsBlockedAsync(fixture, scene.ProjectId));
        Assert.Equal(abandonmentBlocked, await Tamper.ProbeRunAbandonmentIsBlockedAsync(fixture, scene.RunId));
    }

    // ---- controls -------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("valid")]
    [InlineData("checksum-only")]
    public async Task A_well_formed_receipt_stays_recorded_and_a_checksum_only_mismatch_stays_unavailable_without_reacquiring_ownership(string name)
    {
        var ended = await Rig.EndAsync(fixture, Step.Implementation, queued: false);
        var scene = ended.Scene;
        var confirmed = scene.ConfirmedCommand(await scene.ReadReleaseAsync(ended.Target.Id));
        await scene.ReleaseStepAsync(ended.Target.Id);
        var ids = await Tamper.ReadIdsAsync(fixture, scene, ended.Target);
        if (name == "checksum-only")
        {
            await Tamper.ApplyAsync(fixture, Table, $"UPDATE {Table} SET \"ConfirmationSha256\" = {{0}} WHERE \"Id\" = {{1}}", new string('a', 64), ids.ReleaseId);
        }

        var ownership = await Rig.ReadOwnershipAsync(fixture, scene);
        var inspected = await InspectAsync(fixture, scene.RunId, ended.Target.Id);
        var candidate = await scene.ReadReleaseAsync(ended.Target.Id);
        var replay = await scene.ReleaseAsync(confirmed);

        // The relational grant releases ownership in both cases: the checksum is an exact-intent check, never authority.
        Assert.Equal(WorkspaceExecutionStatus.Idle, ownership.Workspace);
        Assert.False(await Tamper.ProbeIsBlockedAsync(fixture, scene.ProjectId));
        if (name == "valid")
        {
            Assert.Equal(LocalExecutionReleaseInspectionStatus.Recorded, inspected.Status);
            Assert.Equal(LocalExecutionOwnershipReleaseStatus.Released, candidate.Status);
            Assert.True(replay.IsSuccess);
        }
        else
        {
            Assert.Equal(LocalExecutionReleaseInspectionStatus.Unavailable, inspected.Status);
            Assert.Equal(LocalExecutionOwnershipReleaseStatus.Unavailable, candidate.Status);
            Assert.True(replay.IsFailure);
        }

        Assert.Single(await scene.ReadReleasesAsync());
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.LocalExecutionOwnershipReleased));
        Assert.Equal(ownership, await Rig.ReadOwnershipAsync(fixture, scene));
    }

    [Theory]
    [InlineData(int.MaxValue, 0L, 0L)]
    [InlineData(int.MinValue, 0L, 0L)]
    [InlineData(-int.MaxValue, 50_000_000L, 40_000L)]
    [InlineData(0, long.MaxValue, long.MaxValue)]
    [InlineData(137, long.MaxValue, 0L)]
    [InlineData(137, 0L, 0L)]
    public async Task Valid_integer_boundaries_of_the_physical_exit_code_and_the_tick_counts_are_admitted_exactly(
        int physicalExitCode, long processDurationTicks, long observedElapsedTicks)
    {
        var ended = await Rig.EndAsync(fixture, Step.Implementation, queued: false);
        var scene = ended.Scene;
        await scene.ReleaseStepAsync(ended.Target.Id);
        var ids = await Tamper.ReadIdsAsync(fixture, scene, ended.Target);
        var recorded = await InspectAsync(fixture, scene.RunId, ended.Target.Id);
        var expires = (await scene.ReadReleasesAsync()).Single().ConfirmationExpiresAtUtc;
        var facts = recorded.Receipt!.Facts with
        {
            PhysicalExitCode = physicalExitCode,
            ProcessDurationTicks = processDurationTicks,
            ObservedElapsedTicks = observedElapsedTicks,
        };
        var digest = LocalExecutionOwnershipReleaseConfirmation.Digest(facts, expires);
        // The one fact, the operation row, the observation and the Human's own snapshot all carry the same boundary values.
        await Tamper.ApplyWithoutCheckConstraintsAsync(
            fixture, "attempts", "UPDATE attempts SET \"AgentProcessDuration\" = {1} WHERE \"Id\" = {0}", ids.OperationId, processDurationTicks);
        await Tamper.ApplyWithoutCheckConstraintsAsync(
            fixture, "local_execution_termination_observations",
            "UPDATE local_execution_termination_observations SET \"PhysicalExitCode\" = {1}, \"ProcessDuration\" = {2}, \"ObservedElapsed\" = {3} "
            + "WHERE \"Id\" = {0}",
            ids.ObservationId, physicalExitCode, processDurationTicks, observedElapsedTicks);
        await Tamper.ApplyWithoutCheckConstraintsAsync(
            fixture, Table,
            $"UPDATE {Table} SET \"IntentPhysicalExitCode\" = {{1}}, \"IntentProcessDuration\" = {{2}}, \"IntentObservedElapsed\" = {{3}}, "
            + "\"ConfirmationSha256\" = {4} WHERE \"Id\" = {0}",
            ids.ReleaseId, physicalExitCode, processDurationTicks, observedElapsedTicks, digest);

        var inspected = await InspectAsync(fixture, scene.RunId, ended.Target.Id);
        var candidate = await scene.ReadReleaseAsync(ended.Target.Id);

        var because = $"{physicalExitCode}/{processDurationTicks}/{observedElapsedTicks}";
        Assert.True(inspected.Status == LocalExecutionReleaseInspectionStatus.Recorded, $"{because}: {inspected.Status}");
        Assert.Equal(physicalExitCode, inspected.Receipt!.Facts.PhysicalExitCode);
        Assert.Equal(processDurationTicks, inspected.Receipt.Facts.ProcessDurationTicks);
        Assert.Equal(observedElapsedTicks, inspected.Receipt.Facts.ObservedElapsedTicks);
        Assert.Equal(LocalExecutionOwnershipReleaseStatus.Released, candidate.Status);
        Assert.Equal(WorkspaceExecutionStatus.Idle, (await Rig.ReadOwnershipAsync(fixture, scene)).Workspace);
    }
}
