using System.Data.Common;
using DevalCopilot.Application.Features.Runs.Queries.GetPlanningSequenceInspection;
using DevalCopilot.Application.Features.Runs.Queries.GetPlanningStepInspection;
using DevalCopilot.Application.Tests.Features.Runs.PlanningSequences;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.PlanningSequenceInspection;

/// <summary>
/// Review 1 corrections of the selected association (ADR-0068). PLI-R1: the linked attempt's number is admitted as an exact stored
/// INTEGER from 1 to int.MaxValue in the same statement that materializes the shared scalar identity, so REAL, TEXT, BLOB, nonpositive and
/// out-of-range storage is Unavailable with null payloads (never a plausible number or a conversion fault) while every legitimate positive
/// Int32, including int.MaxValue and SQLite INTEGER-affinity conversions of numeric text, stays Recorded. PLI-R2: a concurrent revision
/// change during the later membership read withholds even a coherent no-link answer.
/// </summary>
public sealed class PlanningStepInspectionAdmissionTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    [Theory]
    [InlineData("1.75")]
    [InlineData("0.5")]
    [InlineData("1e300")]
    [InlineData("2147483648")]
    [InlineData("4294967297")]
    [InlineData("9223372036854775807")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("-2147483648")]
    [InlineData("'7tail'")]
    [InlineData("'seven'")]
    [InlineData("''")]
    [InlineData("X'31'")]
    [InlineData("X''")]
    public async Task Unsupported_attempt_number_storage_is_unavailable_with_null_payloads_while_the_index_stays_recorded(string stored)
    {
        var harness = await PlanningSequenceInspectionHarness.ArmedAsync(fixture);
        var attempts = await harness.DriveStepsAsync(2);
        var rows = await harness.RowsAsync();
        Assert.Equal(PlanningStepInspectionStatus.Recorded, (await harness.StepAsync(rows[1].Id)).Status);

        await harness.CorruptAsync($"UPDATE attempts SET AttemptNumber = {stored} WHERE Id = $p0", [attempts[1].ToString().ToUpperInvariant()]);

        var answer = await harness.StepAsync(rows[1].Id);
        Assert.True(
            answer.Status == PlanningStepInspectionStatus.Unavailable,
            $"A stored attempt number of {stored} became {answer.Status} with number {answer.Agent?.AttemptNumber}.");
        Assert.Equal(harness.SequenceId, answer.SequenceId);
        Assert.Null(answer.Step);
        Assert.Null(answer.Agent);
        var index = await harness.IndexAsync();
        Assert.Equal(PlanningSequenceInspectionStatus.Recorded, index.Status);
        Assert.Equal(2, index.Steps.Count);

        // The damage is local to the selected association: the sibling step is still proved.
        Assert.Equal(PlanningStepInspectionStatus.Recorded, (await harness.StepAsync(rows[0].Id)).Status);
    }

    [Theory]
    [InlineData("2147483647", 2147483647)]
    [InlineData("2147483646", 2147483646)]
    [InlineData("1", 1)]
    [InlineData("65536", 65536)]
    [InlineData("'7'", 7)]
    [InlineData("7.0", 7)]
    public async Task Every_legitimate_positive_int32_attempt_number_stays_recorded_including_int_max_and_integer_affinity_conversions(
        string stored, int expected)
    {
        var harness = await PlanningSequenceInspectionHarness.ArmedAsync(fixture);
        var (_, claim) = await harness.Scene.ReserveAndClaimAsync(harness.SequenceId);
        var stepId = (await harness.RowsAsync()).Single().Id;

        await harness.CorruptAsync($"UPDATE attempts SET AttemptNumber = {stored} WHERE Id = $p0", [claim.AttemptId.ToString().ToUpperInvariant()]);

        var answer = await harness.StepAsync(stepId);
        Assert.Equal(PlanningStepInspectionStatus.Recorded, answer.Status);
        Assert.Equal(claim.AttemptId, answer.Agent!.Id);
        Assert.Equal(expected, answer.Agent.AttemptNumber);
        Assert.NotNull(answer.Step);
    }

    [Fact]
    public async Task The_attempt_number_admission_is_part_of_the_one_statement_that_materializes_the_identity()
    {
        var harness = await PlanningSequenceInspectionHarness.ArmedAsync(fixture);
        await harness.Scene.ReserveAndClaimAsync(harness.SequenceId);
        var stepId = (await harness.RowsAsync()).Single().Id;
        var log = new StatementLog();

        await harness.StepAsync(stepId, null, log);

        var identity = Assert.Single(log.Statements, statement => statement.Contains("\"AgentObservedModel\"", StringComparison.Ordinal));
        Assert.Contains("\"AttemptNumber\" >= 1", identity, StringComparison.Ordinal);
        Assert.Contains("\"AttemptNumber\" <= 2147483647", identity, StringComparison.Ordinal);
        Assert.Contains("\"AttemptNumber\" = 2147483647", identity, StringComparison.Ordinal);
        Assert.Contains("\"AttemptNumber\" % 2147483647", identity, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_revision_change_during_the_no_link_membership_read_withholds_the_answer_with_null_payloads_without_a_retry()
    {
        var harness = await PlanningSequenceInspectionHarness.ArmedAsync(fixture);
        await harness.Scene.ObserveReservingAsync(harness.SequenceId);
        var stepId = (await harness.RowsAsync()).Single().Id;
        var bump = new MembershipRevisionBumper(fixture.ConnectionString);

        var answer = await harness.StepAsync(stepId, null, bump);

        Assert.True(bump.Fired, "The independent writer never ran at the membership seam.");
        Assert.Equal(PlanningStepInspectionStatus.Unavailable, answer.Status);
        Assert.Equal(harness.SequenceId, answer.SequenceId);
        Assert.Null(answer.Step);
        Assert.Null(answer.Agent);

        // An explicit second read sees the later revision and the coherent no-link shape.
        var reload = await harness.StepAsync(stepId);
        Assert.Equal(PlanningStepInspectionStatus.NoLinkedAttempt, reload.Status);
        Assert.Null(reload.Step);
        Assert.Null(reload.Agent);
    }

    [Fact]
    public async Task Without_a_concurrent_change_the_coherent_reserved_no_link_answer_is_unchanged_and_the_membership_seam_is_observed()
    {
        var harness = await PlanningSequenceInspectionHarness.ArmedAsync(fixture);
        await harness.Scene.ObserveReservingAsync(harness.SequenceId);
        var stepId = (await harness.RowsAsync()).Single().Id;
        var observer = new MembershipObserver();

        var answer = await harness.StepAsync(stepId, null, observer);

        Assert.True(observer.Seen, "The membership statement was not observed, so the seam is not the one the race test writes at.");
        Assert.Equal(PlanningStepInspectionStatus.NoLinkedAttempt, answer.Status);
        Assert.Null(answer.Step);
        Assert.Null(answer.Agent);
    }

    private static bool IsMembershipStatement(DbCommand command) =>
        command.CommandText.Contains("SELECT EXISTS", StringComparison.Ordinal)
        && command.CommandText.Contains("\"planning_sequence_steps\"", StringComparison.Ordinal);

    private sealed class MembershipObserver : DbCommandInterceptor
    {
        public bool Seen { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Seen |= IsMembershipStatement(command);
            return ValueTask.FromResult(result);
        }
    }

    /// <summary>A deterministic independent SQLite writer: at the membership statement it advances the sequence revision through a connection
    /// of its own, exactly as a concurrent transition would commit between the structural recheck and the answer.</summary>
    private sealed class MembershipRevisionBumper(string connectionString) : DbCommandInterceptor
    {
        public bool Fired { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!Fired && IsMembershipStatement(command))
            {
                Fired = true;
                using var connection = new SqliteConnection(connectionString + ";Pooling=False");
                connection.Open();
                using var update = connection.CreateCommand();
                update.CommandText = "UPDATE planning_sequences SET Revision = Revision + 1";
                update.ExecuteNonQuery();
            }

            return ValueTask.FromResult(result);
        }
    }
}
