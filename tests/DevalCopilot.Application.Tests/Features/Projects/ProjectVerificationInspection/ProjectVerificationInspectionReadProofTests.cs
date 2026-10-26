using DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationInspection;
using DevalCopilot.Domain.Features.Projects;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects.ProjectVerificationInspection;

/// <summary>ADR-0069: the selected metadata is a membership predicate, one bounded relational statement for the ownership and result facts
/// and one bounded metadata read per stream. It writes nothing, reads no path, hash or byte (the handler has no artifact store), and no
/// current recipe, run, Agent or sequence row.</summary>
public sealed class ProjectVerificationInspectionReadProofTests : ProjectVerificationInspectionTestBase
{
    private async Task<(Guid ProjectId, SeededExecution Execution)> SeedWithStreamsAsync()
    {
        var projectId = await Seed.SeedProjectAsync();
        var (workspace, checkpoint) = await Seed.SeedWorkspaceAsync(projectId);
        var command = await Seed.SeedCommandAsync(projectId);
        var execution = await Seed.SeedExecutionAsync(projectId, workspace, checkpoint, command, 1, ExecutionShape.FailedExited);
        await Seed.AddOutputAsync(execution.Id, VerificationOutputPurpose.StandardOutput, "a/out.sealed", ProjectVerificationInspectionSeed.ContentHash(), 5);
        await Seed.AddOutputAsync(execution.Id, VerificationOutputPurpose.StandardError, "a/err.sealed", ProjectVerificationInspectionSeed.ContentHash('c'), 6);
        return (projectId, execution);
    }

    [Fact]
    public async Task The_metadata_read_is_four_bounded_selects_that_never_select_a_path_hash_argument_or_current_recipe()
    {
        var (projectId, execution) = await SeedWithStreamsAsync();
        var log = new ProjectVerificationStatementLog();
        var fingerprint = await ChangeFingerprintAsync();

        var result = await InspectAsync(projectId, execution.Id, log);

        Assert.Equal(ProjectVerificationInspectionStatus.Recorded, result.Status);
        log.AssertReadOnly();
        log.AssertNeverSelects(
            "RelativeStoragePath", "ContentHash", "WorkspacePath", "ExecutablePath", "\"Arguments\"", "TimeoutSeconds", "verification_commands",
            "attempts", "runs", "attempt_verification_evidence", "local_execution", "repository_mutation_leases");
        Assert.Equal(4, log.Commands.Count);
        var ownership = Assert.Single(log.Statements, statement => statement.Contains("FROM \"git_checkpoints\"", StringComparison.Ordinal)
            || statement.Contains("JOIN \"git_checkpoints\"", StringComparison.Ordinal));
        Assert.Contains("\"git_workspaces\"", ownership, StringComparison.Ordinal);
        Assert.Contains("\"verification_executions\"", ownership, StringComparison.Ordinal);
        Assert.Contains("LIMIT", ownership, StringComparison.Ordinal);
        var streams = log.Statements.Where(statement => statement.Contains("FROM \"verification_output_artifacts\"", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, streams.Length);
        Assert.All(streams, statement =>
        {
            Assert.Contains("\"Purpose\" =", statement, StringComparison.Ordinal);
            Assert.Contains("LIMIT", statement, StringComparison.Ordinal);
        });
        Assert.Equal(fingerprint, await ChangeFingerprintAsync());
    }

    [Fact]
    public async Task The_ownership_statement_and_each_stream_read_are_index_or_key_driven()
    {
        var (projectId, execution) = await SeedWithStreamsAsync();
        var log = new ProjectVerificationStatementLog();
        await InspectAsync(projectId, execution.Id, log);

        foreach (var command in log.Commands)
        {
            var plan = await ProjectVerificationPlanProbe.ExplainAsync(Fixture.ConnectionString, command);
            Assert.DoesNotContain("SCAN verification_executions", plan, StringComparison.Ordinal);
            Assert.DoesNotContain("SCAN git_checkpoints", plan, StringComparison.Ordinal);
            Assert.DoesNotContain("SCAN git_workspaces", plan, StringComparison.Ordinal);
            Assert.DoesNotContain("SCAN verification_output_artifacts", plan, StringComparison.Ordinal);
            Assert.DoesNotContain("TEMP B-TREE", plan, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_handlers_take_only_the_database_so_metadata_can_open_no_artifact_file()
    {
        var inspection = typeof(DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationInspection.GetProjectVerificationInspectionQueryHandler);
        var history = typeof(DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationHistory.GetProjectVerificationHistoryQueryHandler);

        Assert.Equal([typeof(DevalCopilot.Application.Data.IDevalCopilotDbContext)],
            Assert.Single(inspection.GetConstructors()).GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal([typeof(DevalCopilot.Application.Data.IDevalCopilotDbContext)],
            Assert.Single(history.GetConstructors()).GetParameters().Select(parameter => parameter.ParameterType));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_database_fault_propagates_and_is_never_an_availability_state()
    {
        var (projectId, execution) = await SeedWithStreamsAsync();

        foreach (var table in new[] { "verification_executions", "git_checkpoints", "verification_output_artifacts" })
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => TryInspectAsync(projectId, execution.Id, new FaultOnTable(table)));
        }
    }

    private sealed class FaultOnTable(string table) : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains($"\"{table}\"", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("injected database fault");
            }

            return ValueTask.FromResult(result);
        }
    }
}
