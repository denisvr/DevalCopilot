using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionAdmissionHistory;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionQueueInspection;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionAdmissionHistory;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionQueueInspection.LocalExecutionQueueInspectionHarness;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionQueueInspection;

/// <summary>
/// The exact bounds of the outstanding-admission answer (ADR-0064): at most 64 live queued cycles and four held reservations (Reserved or
/// Unresolved, ended or not), so at most 68 coherent entries, read as 69 candidates so an overflow is seen and never truncated into an
/// apparently complete list; strictly increasing admission order; one candidate per sequence and run; the per-sequence ledger bounds.
/// One cycle of each representative state is created by the production commands and cloned by a bounded TEST-ONLY storage fixture
/// (<see cref="LocalExecutionQueueInspectionClones"/>), so the counts never repeat the provider workflow per row.
/// </summary>
public sealed class LocalExecutionQueueInspectionBoundTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    /// <summary>
    /// The production-created templates of a bounded count come first (the reserved one, then the ended unresolved one, then the waiting
    /// one, so each reservation is the one the host offers and every number comes from the production counters), and only then the
    /// clones: a clone is a recorded owner, so it must not take the capacity a later production reservation needs.
    /// </summary>
    private async Task<(LocalExecutionQueueInspectionHarness Harness, List<ClonedCycle> Clones)> WorldAsync(
        int live, int reserved, int ended)
    {
        var harness = new LocalExecutionQueueInspectionHarness(fixture);
        var clones = new List<ClonedCycle>();
        LocalExecutionSequenceTestScene? reservedTemplate = null;
        LocalExecutionSequenceTestScene? endedTemplate = null;
        LocalExecutionSequenceTestScene? liveTemplate = null;
        if (reserved > 0)
        {
            var (cycle, _) = await harness.AddCycleAsync(projectName: "Reserved template");
            Assert.NotNull(await cycle.Scene.ReserveAsync());
            reservedTemplate = cycle.Scene;
        }

        if (ended > 0)
        {
            var (cycle, _) = await harness.AddCycleAsync(projectName: "Ended template");
            var ticket = (await cycle.Scene.ReserveAsync())!;
            var resolved = await cycle.Scene.ResolveAsync(
                ticket, "attempts.persistence_failed", new ConfigurableLocalExecutionStepProbe(LocalExecutionStepProbeResult.Unresolved));
            Assert.True(resolved.IsSuccess);
            endedTemplate = cycle.Scene;
        }

        if (live > 0)
        {
            liveTemplate = (await harness.AddCycleAsync(projectName: "Waiting template")).Cycle.Scene;
        }

        if (reservedTemplate is not null)
        {
            clones.AddRange(await LocalExecutionQueueInspectionClones.CloneAsync(fixture, reservedTemplate, reserved - 1, "Reserved"));
        }

        if (endedTemplate is not null)
        {
            clones.AddRange(await LocalExecutionQueueInspectionClones.CloneAsync(fixture, endedTemplate, ended - 1, "Ended"));
        }

        if (liveTemplate is not null)
        {
            clones.AddRange(await LocalExecutionQueueInspectionClones.CloneAsync(fixture, liveTemplate, live - 1, "Waiting"));
        }

        return (harness, clones);
    }

    public static TheoryData<int, int, int, LocalExecutionQueueInspectionStatus, int> Counts => new()
    {
        // live waiting, live reserved, ended unresolved, status, entries
        { 1, 0, 0, LocalExecutionQueueInspectionStatus.Recorded, 1 },
        { 64, 0, 0, LocalExecutionQueueInspectionStatus.Recorded, 64 },
        { 64, 0, 4, LocalExecutionQueueInspectionStatus.Recorded, 68 },
        { 60, 4, 0, LocalExecutionQueueInspectionStatus.Recorded, 64 },
        { 62, 2, 2, LocalExecutionQueueInspectionStatus.Recorded, 66 },
        { 65, 0, 0, LocalExecutionQueueInspectionStatus.Unavailable, 0 },
        { 64, 0, 5, LocalExecutionQueueInspectionStatus.Unavailable, 0 },
        { 60, 0, 5, LocalExecutionQueueInspectionStatus.Unavailable, 0 },
        { 59, 5, 0, LocalExecutionQueueInspectionStatus.Unavailable, 0 },
        { 61, 3, 2, LocalExecutionQueueInspectionStatus.Unavailable, 0 },
        { 60, 5, 0, LocalExecutionQueueInspectionStatus.Unavailable, 0 },
    };

    [Theory]
    [MemberData(nameof(Counts))]
    public async Task The_exact_bounds_are_recorded_in_full_and_every_overflow_is_unavailable_never_a_truncated_list(
        int live, int reserved, int ended, LocalExecutionQueueInspectionStatus status, int entries)
    {
        var (harness, _) = await WorldAsync(live, reserved, ended);
        var before = await harness.ChangeFingerprintAsync();

        var result = await harness.InspectAsync();

        Assert.Equal(status, result.Status);
        Assert.Equal(entries, result.Entries.Count);
        Assert.Equal(before, await harness.ChangeFingerprintAsync());
        if (status != LocalExecutionQueueInspectionStatus.Recorded)
        {
            return;
        }

        var numbers = result.Entries.Select(entry => entry.Admission.AdmissionNumber).ToArray();
        Assert.Equal(numbers.Order(), numbers);
        Assert.Equal(numbers.Length, numbers.Distinct().Count());
        Assert.Equal(entries, result.Entries.Select(entry => entry.RunId).Distinct().Count());
        Assert.Equal(entries, result.Entries.Select(entry => entry.Sequence.SequenceId).Distinct().Count());
        Assert.Equal(entries, result.Entries.Select(entry => entry.ProjectId).Distinct().Count());
        Assert.Equal(live + reserved, result.Entries.Count(entry => !LocalExecutionSequencePolicy.IsEnded(entry.Sequence.RecordedState)));
        Assert.Equal(ended, result.Entries.Count(entry => entry.Sequence.RecordedState == LocalExecutionSequenceState.NeedsAttention));
        Assert.Equal(reserved, result.Entries.Count(entry => entry.Admission.RecordedState == LocalExecutionAdmissionState.Reserved));
        Assert.Equal(ended, result.Entries.Count(entry => entry.Admission.RecordedState == LocalExecutionAdmissionState.Unresolved));
    }

    [Fact]
    public async Task The_largest_coherent_answer_is_the_per_run_ledger_tail_of_every_entry_and_names_the_registered_project()
    {
        var (harness, clones) = await WorldAsync(live: 64, reserved: 0, ended: 4);

        var result = await harness.InspectAsync();

        Assert.Equal(68, result.Entries.Count);
        var byRun = clones.ToDictionary(clone => clone.RunId);
        foreach (var entry in result.Entries.Where(entry => byRun.ContainsKey(entry.RunId)))
        {
            var clone = byRun[entry.RunId];
            Assert.Equal(clone.ProjectId, entry.ProjectId);
            Assert.Equal(clone.ProjectName, entry.ProjectName);
            Assert.Equal(clone.SequenceId, entry.Sequence.SequenceId);
            Assert.Equal(clone.AdmissionId, entry.Admission.AdmissionId);
        }

        // Every clone's whole answer also equals what the per-run ADR-0063 reading proves for its run.
        foreach (var entry in new[] { result.Entries[0], result.Entries[3], result.Entries[4], result.Entries[^1] })
        {
            await using var context = fixture.CreateContext();
            var history = (await new GetLocalExecutionAdmissionHistoryQueryHandler(context).HandleAsync(
                new GetLocalExecutionAdmissionHistoryQuery(entry.RunId), CancellationToken.None)).Value;
            Assert.Equal(LocalExecutionAdmissionHistoryStatus.Recorded, history.Status);
            Assert.Equal(history.Sequence, entry.Sequence);
            Assert.Equal(history.Admissions[^1], entry.Admission);
        }
    }

    [Fact]
    public async Task Overflow_is_detected_even_when_every_one_of_the_sixty_nine_candidates_is_individually_coherent()
    {
        var (harness, _) = await WorldAsync(live: 64, reserved: 0, ended: 5);
        var recorder = new CommandRecorder();

        var result = await harness.InspectAsync(recorder);

        AssertShowsNothing(result, LocalExecutionQueueInspectionStatus.Unavailable);
        await using var context = fixture.CreateContext();
        Assert.Equal(69, await context.LocalExecutionAdmissions.CountAsync(admission => admission.State != LocalExecutionAdmissionState.Consumed && admission.State != LocalExecutionAdmissionState.Retired));
        Assert.Contains("LIMIT", Assert.Single(recorder.Commands), StringComparison.Ordinal);
    }

    // ---- the per-sequence ledger bounds ---------------------------------------------------------------------------------------------

    private static string Insert(string columns, string values, string where) =>
        $"INSERT INTO local_execution_admissions ({columns}) SELECT {values} FROM local_execution_admissions WHERE {where}";

    private const string AdmissionColumns =
        "Id, SequenceId, AdmissionNumber, Ordinal, Kind, PassNumber, RecipeOrdinal, PredecessorStepId, SourceCheckpointId, "
        + "SourceFingerprintSha256, ReportMessageId, SubjectAttemptId, State, StepId, WaitingReason, WaitingReasonRecordedAtUtc, "
        + "EnqueuedAtUtc, ReasonCode, Revision, UpdatedAtUtc";

    private const string ForeignGuid =
        "(SELECT upper(hex(randomblob(4))) || '-' || hex(randomblob(2)) || '-4000-8000-' || hex(randomblob(6)))";

    [Fact]
    public async Task A_ledger_beyond_the_recipe_dependent_maximum_makes_the_whole_answer_unavailable_beside_a_coherent_cycle()
    {
        var harness = new LocalExecutionQueueInspectionHarness(fixture);
        // The long cycle is the oldest, so the host's global reservation of each step is always its own.
        var (longer, sequenceId) = await harness.AddCycleAsync(recipeCount: 2);
        await longer.DriveStepsThenWaitAsync(sequenceId, 11, LocalExecutionAdmissionHistoryHarness.MixedOriginsScript());
        await harness.AddCycleAsync();
        Assert.Equal(2, (await harness.InspectAsync()).Entries.Count);

        // TEST-ONLY storage write: a thirteenth admission after the twelfth, the Domain's own maximum for two recipes.
        await harness.CorruptAsync(
            "DROP INDEX IF EXISTS ix_local_execution_admissions_outstanding; " + Insert(
                AdmissionColumns,
                $"{ForeignGuid}, SequenceId, (SELECT MAX(AdmissionNumber) FROM local_execution_admissions) + 1, 12, Kind, PassNumber, RecipeOrdinal, "
                + "PredecessorStepId, SourceCheckpointId, SourceFingerprintSha256, ReportMessageId, SubjectAttemptId, 'Waiting', NULL, WaitingReason, "
                + "WaitingReasonRecordedAtUtc, EnqueuedAtUtc, NULL, 1, UpdatedAtUtc",
                "Ordinal = 11"),
            null,
            "trg_local_execution_admission_insert_shape", "trg_local_execution_admission_insert_bound", LocalExecutionAdmissionHistoryHarness.AdmissionImmutable);

        AssertShowsNothing(await harness.InspectAsync(), LocalExecutionQueueInspectionStatus.Unavailable);
    }

    [Fact]
    public async Task A_ledger_beyond_the_absolute_maximum_is_unavailable_even_though_the_aggregate_would_fit()
    {
        var harness = new LocalExecutionQueueInspectionHarness(fixture);
        var (cycle, _) = await harness.AddCycleAsync();
        await harness.AddCycleAsync();
        Assert.NotNull(cycle);

        // TEST-ONLY storage write: 110 further rows on one sequence (the unique ordinal and outstanding indexes dropped), far above the
        // 103-row read guard yet a tiny share of the 7,177-row ceiling.
        await harness.CorruptAsync(
            "DROP INDEX IF EXISTS ix_local_execution_admissions_ordinal; DROP INDEX IF EXISTS ix_local_execution_admissions_outstanding; "
            + $"WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 110) INSERT INTO local_execution_admissions ({AdmissionColumns}) "
            + "SELECT printf('%08x-0000-4000-8000-%012d', n.i, n.i), SequenceId, (SELECT MAX(AdmissionNumber) FROM local_execution_admissions) + n.i, 0, Kind, PassNumber, "
            + "RecipeOrdinal, PredecessorStepId, SourceCheckpointId, SourceFingerprintSha256, ReportMessageId, SubjectAttemptId, 'Consumed', "
            + "NULL, WaitingReason, WaitingReasonRecordedAtUtc, EnqueuedAtUtc, NULL, 1, UpdatedAtUtc FROM local_execution_admissions, n "
            + "WHERE AdmissionNumber = (SELECT MIN(AdmissionNumber) FROM local_execution_admissions)",
            null,
            "trg_local_execution_admission_insert_shape", "trg_local_execution_admission_insert_bound", LocalExecutionAdmissionHistoryHarness.AdmissionImmutable);

        AssertShowsNothing(await harness.InspectAsync(), LocalExecutionQueueInspectionStatus.Unavailable);
    }
}
