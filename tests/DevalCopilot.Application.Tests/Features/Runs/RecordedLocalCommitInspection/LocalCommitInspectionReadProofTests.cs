using System.Text.RegularExpressions;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalCommitInspection;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.RecordedLocalCommitInspection;

/// <summary>Proves how the recorded local-commit read reads (ADR-0070): exactly one bounded, index-driven, read-only statement over the
/// five named tables, no forbidden column, no write, no artifact, repository or external dependency, and a database fault that propagates
/// to the normal failure boundary instead of becoming Unavailable or absent.</summary>
public sealed class LocalCommitInspectionReadProofTests : LocalCommitInspectionTestBase
{
    private static readonly string[] ForbiddenColumns =
    [
        "RepositoryMutationLeaseId", "CodeReviewAttemptId", "CodeReviewApprovalMessageId", "ExecutionReportMessageId",
        "AgentCheckpointReviewId", "HumanCheckpointReviewId", "RequestSha256", "AuthoritySha256", "NormalizedMessage", "AuthorName",
        "AuthorEmail", "CommitTimeUnixSeconds", "IndexPreimageSha256", "PreparedIndexSha256", "PreparedIndexRelativePath",
        "IndexAdministrativeDirectoryIdentity", "IndexPreimageIdentity", "PreparedIndexArtifactIdentity", "PreparedIndexArtifactLength",
        "IndexPreimageLength", "IndexLockIdentity", "IndexLockLength", "IndexAcquiredAtUtc", "IndexQuarantineName",
        "IndexReplacementPlannedAtUtc", "local_commit_authority_members", "WorkspacePath", "SourceCommitSha", "SourceBranchName",
        "LastFailureReasonCode", "WorkspaceNumber", "ChangedFiles", "repository_mutation_leases", "checkpoint_reviews", "attempts",
        "collaboration_messages", "events", "Objective", "Lifecycle", "Stage",
    ];

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task One_bounded_read_only_statement_over_the_five_named_tables_answers_every_shape(LocalCommitInspectionShape shape)
    {
        var rows = await SeedAsync(shape);
        var log = new LocalCommitInspectionStatementLog();

        Assert.Equal(LocalCommitInspectionStatus.Recorded, (await InspectAsync(rows.Run.Id, log)).Status);

        var recorded = Assert.Single(log.Commands);
        log.AssertReadOnly();
        log.AssertNeverSelects(ForbiddenColumns);
        Assert.Contains(recorded.Parameters, parameter => parameter.Name == "@p" && Equals(parameter.Value, 2));
        Assert.Equal(
            ["git_checkpoints", "git_workspaces", "local_commit_operations", "projects", "runs"],
            Regex.Matches(recorded.Text, "(?:FROM|JOIN) \"(?<table>[a-z_]+)\"").Select(match => match.Groups["table"].Value).Order().ToArray());
        Assert.Single(Regex.Matches(recorded.Text, @"\bLIMIT\b"));
    }

    public static TheoryData<LocalCommitInspectionShape> Shapes => new(Enum.GetValues<LocalCommitInspectionShape>());

    [Fact]
    public async Task The_statement_is_driven_by_the_unique_run_index_with_no_scan_or_sort()
    {
        var rows = await SeedAsync(LocalCommitInspectionShape.Completed);
        var log = new LocalCommitInspectionStatementLog();
        await InspectAsync(rows.Run.Id, log);

        var plan = await LocalCommitInspectionPlanProbe.ExplainAsync(Fixture.ConnectionString, Assert.Single(log.Commands));

        Assert.Contains("USING INDEX ix_local_commit_operations_run (RunId=?)", plan);
        Assert.DoesNotContain("SCAN", plan);
        Assert.DoesNotContain("TEMP B-TREE", plan);
    }

    [Fact]
    public async Task A_run_without_an_operation_and_an_unknown_run_also_cost_one_read_only_statement()
    {
        var rows = await SeedAsync(LocalCommitInspectionShape.Prepared);
        await ExecuteAsync($"DELETE FROM local_commit_authority_members WHERE OperationId = '{Id(rows.Operation.Id)}'");
        await ExecuteAsync($"DELETE FROM local_commit_operations WHERE RunId = '{Id(rows.Run.Id)}'");
        var absent = new LocalCommitInspectionStatementLog();
        var unknown = new LocalCommitInspectionStatementLog();

        Assert.Equal(LocalCommitInspectionStatus.NotRecorded, (await InspectAsync(rows.Run.Id, absent)).Status);
        Assert.True((await TryInspectAsync(Guid.NewGuid(), unknown)).IsFailure);

        Assert.Single(absent.Commands);
        Assert.Single(unknown.Commands);
        absent.AssertReadOnly();
        unknown.AssertReadOnly();
    }

    [Fact]
    public async Task A_non_completed_operation_never_discloses_its_prepared_proposed_commit_even_when_that_is_damaged()
    {
        var rows = await SeedAsync(LocalCommitInspectionShape.FailedStarted);
        Assert.Equal(1, await ExecuteAsync($"UPDATE local_commit_operations SET CommitSha = 'NOT-A-COMMIT' WHERE Id = '{Id(rows.Operation.Id)}'"));

        var operation = Recorded(await InspectAsync(rows.Run.Id));

        Assert.Null(operation.CommitSha);
    }

    [Fact]
    public void The_handler_has_no_dependency_beyond_the_database_context()
    {
        var constructor = Assert.Single(typeof(GetLocalCommitInspectionQueryHandler).GetConstructors());

        Assert.Equal([typeof(IDevalCopilotDbContext)], constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
    }

    [Theory]
    [InlineData("local_commit_operations")]
    [InlineData("git_workspaces")]
    [InlineData("git_checkpoints")]
    [InlineData("projects")]
    [InlineData("runs")]
    public async Task A_database_fault_propagates_and_is_never_relabelled_unavailable_or_absent(string table)
    {
        var rows = await SeedAsync(LocalCommitInspectionShape.Completed);

        await Assert.ThrowsAsync<InvalidOperationException>(() => TryInspectAsync(rows.Run.Id, new FaultOnTable(table)));
    }

    private sealed class FaultOnTable(string table) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            CommandEventData eventData,
            InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default) =>
            command.CommandText.Contains($"\"{table}\"", StringComparison.Ordinal)
                ? throw new InvalidOperationException("Injected database fault.")
                : ValueTask.FromResult(result);
    }
}
