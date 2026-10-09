using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevalCopilot.Api.Features.Projects.UpdateVerificationCommand;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// The recorded local-delivery receipt through the protected HTTP boundary (ADR-0032): a real delivery written by the production
/// handlers and supervisor against real Git and file-backed SQLite, read back as the four closed states. Every fact asserted here
/// is compared with the durable rows the delivery itself recorded, never with the current eligibility of the run.
/// </summary>
public sealed class LocalDeliveryReceiptEndpointTests : LocalDeliveryReceiptTestBase
{
    private static readonly string[] ReceiptKeys =
    [
        "version", "runId", "operationId", "objective", "commitSha", "parentCommitSha", "treeSha", "branchName", "completedAtUtc",
        "checkpoint", "executionReportMessageId", "codeReview", "humanReview", "verification",
    ];

    private static readonly string[] VerificationKeys =
        ["order", "commandId", "executionId", "executionNumber", "commandName", "status", "exitCode", "completedAtUtc"];

    private async Task AssertReceiptMatchesRecordedRowsAsync(ReceiptReading reading, LocalCommitLineageIds ids, Guid operationId)
    {
        Assert.Equal(HttpStatusCode.OK, reading.Status);
        Assert.Equal("Available", reading.State);
        var receipt = reading.Receipt;
        Assert.Equal(ReceiptKeys.Order(StringComparer.Ordinal), receipt.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));

        await using var db = OpenDb();
        var run = await db.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == ids.RunId);
        var operation = await db.LocalCommitOperations.AsNoTracking().SingleAsync(candidate => candidate.Id == operationId);
        var checkpoint = await db.GitCheckpoints.AsNoTracking().SingleAsync(candidate => candidate.Id == ids.CheckpointId);
        var review = await db.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == ids.ReviewAttemptId);
        Assert.Equal(LocalCommitStatus.Completed, operation.Status);

        Assert.Equal(1, receipt.GetProperty("version").GetInt32());
        Assert.Equal(run.Id, receipt.GetProperty("runId").GetGuid());
        Assert.Equal(operationId, receipt.GetProperty("operationId").GetGuid());
        Assert.Equal(run.Objective, receipt.GetProperty("objective").GetString());
        Assert.Equal(Scene.RunMainGit("rev-parse", "refs/heads/" + Scene.BranchName).Trim(), receipt.GetProperty("commitSha").GetString());
        Assert.Equal(operation.CommitSha, receipt.GetProperty("commitSha").GetString());
        Assert.Equal(Scene.BaselineCommit, receipt.GetProperty("parentCommitSha").GetString());
        Assert.Equal(operation.TreeSha, receipt.GetProperty("treeSha").GetString());
        Assert.Equal(Scene.BranchName, receipt.GetProperty("branchName").GetString());
        Assert.Equal(operation.CompletedAtUtc, receipt.GetProperty("completedAtUtc").GetDateTimeOffset());

        var checkpointFacts = receipt.GetProperty("checkpoint");
        Assert.Equal(ids.CheckpointId, checkpointFacts.GetProperty("id").GetGuid());
        Assert.Equal(checkpoint.CheckpointNumber, checkpointFacts.GetProperty("number").GetInt32());
        Assert.Equal(checkpoint.FingerprintSha256, checkpointFacts.GetProperty("fingerprintSha256").GetString());
        Assert.Equal(2, checkpointFacts.GetProperty("changedPathCount").GetInt32());

        Assert.Equal(ids.ExecutionReportId, receipt.GetProperty("executionReportMessageId").GetGuid());
        var codeReview = receipt.GetProperty("codeReview");
        Assert.Equal(ids.ReviewAttemptId, codeReview.GetProperty("attemptId").GetGuid());
        Assert.Equal(review.AttemptNumber, codeReview.GetProperty("attemptNumber").GetInt32());
        Assert.Equal(operation.CodeReviewApprovalMessageId, codeReview.GetProperty("approvalMessageId").GetGuid());
        var human = receipt.GetProperty("humanReview");
        Assert.Equal(ids.HumanReviewId, human.GetProperty("reviewId").GetGuid());
        Assert.Equal("Approved", human.GetProperty("decision").GetString());

        var executions = await db.VerificationExecutions.AsNoTracking()
            .Where(candidate => ids.AllExecutionIds!.Contains(candidate.Id)).ToListAsync();
        var members = receipt.GetProperty("verification").EnumerateArray().ToList();
        Assert.Equal(ids.AllExecutionIds!.Count, members.Count);
        for (var order = 0; order < members.Count; order++)
        {
            var expected = executions.Single(candidate => candidate.Id == ids.AllExecutionIds[order]);
            var member = members[order];
            Assert.Equal(VerificationKeys.Order(StringComparer.Ordinal), member.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
            Assert.Equal(order, member.GetProperty("order").GetInt32());
            Assert.Equal(expected.VerificationCommandId, member.GetProperty("commandId").GetGuid());
            Assert.Equal(expected.Id, member.GetProperty("executionId").GetGuid());
            Assert.Equal(expected.ExecutionNumber, member.GetProperty("executionNumber").GetInt32());
            Assert.Equal($"Backend tests {order + 1}", member.GetProperty("commandName").GetString());
            Assert.Equal(expected.CommandName, member.GetProperty("commandName").GetString());
            Assert.Equal("Passed", member.GetProperty("status").GetString());
            Assert.Equal(0, member.GetProperty("exitCode").GetInt32());
            Assert.Equal(expected.CompletedAtUtc, member.GetProperty("completedAtUtc").GetDateTimeOffset());
        }

        Assert.DoesNotContain(Scene.MainPath, reading.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Scene.WorkspacePath, reading.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(operation.AuthorEmail, reading.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\\dotnet.exe", reading.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(operation.PreparedIndexRelativePath.Replace("\\", "\\\\", StringComparison.Ordinal), reading.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_run_is_a_safe_not_found_and_the_route_requires_authentication()
    {
        using var host = StartHost(runSupervisor: false);

        using (var client = LocalCommitLineage.AuthenticatedClient(host))
        {
            var missing = await client.GetAsync($"/api/runs/{Guid.NewGuid()}/{Route}");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Equal("runs.not_found", await ErrorCodeAsync(missing));
        }

        using var anonymous = host.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/runs/{Guid.NewGuid()}/{Route}")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await LocalCommitLineage.AuthenticatedClient(host)
            .PostAsync($"/api/runs/{Guid.NewGuid()}/{Route}", null)).StatusCode);
    }

    [Fact]
    public async Task A_run_without_an_operation_is_not_recorded_with_a_null_receipt()
    {
        using var host = StartHost(runSupervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);

        var reading = await ReceiptAsync(host, ids.RunId);

        Assert.Equal(HttpStatusCode.OK, reading.Status);
        Assert.Equal("NotRecorded", reading.State);
        Assert.Equal(JsonValueKind.Null, reading.Root.GetProperty("receipt").ValueKind);
        Assert.Equal(["receipt", "state"], reading.Root.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_admitted_operation_that_has_not_completed_is_not_completed_with_a_null_receipt()
    {
        using var host = StartHost(runSupervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        var operationId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, ids.RunId, ids, operationId)).StatusCode);
        Assert.Equal("Prepared", (await StatusAsync(host, ids.RunId)).Operation!.Status);

        var prepared = await ReceiptAsync(host, ids.RunId);
        Assert.Equal("NotCompleted", prepared.State);
        Assert.Equal(JsonValueKind.Null, prepared.Root.GetProperty("receipt").ValueKind);

        foreach (var status in new[] { "Executing", "Failed", "Interrupted", "NeedsAttention" })
        {
            await CorruptAsync(
                ["local_commit_operations"],
                [$"UPDATE local_commit_operations SET \"Status\" = '{status}' WHERE \"Id\" = '{Upper(operationId)}'"],
                async () =>
                {
                    var reading = await ReceiptAsync(host, ids.RunId);
                    Assert.Equal(HttpStatusCode.OK, reading.Status);
                    Assert.Equal("NotCompleted", reading.State);
                    Assert.Equal(JsonValueKind.Null, reading.Root.GetProperty("receipt").ValueKind);
                });
        }
    }

    [Fact]
    public async Task A_completed_single_recipe_delivery_with_the_legacy_human_approval_reports_the_exact_recorded_receipt()
    {
        using var host = StartHost(runSupervisor: true);
        var (ids, operationId) = await DeliverAsync(host);

        var reading = await ReceiptAsync(host, ids.RunId);

        await AssertReceiptMatchesRecordedRowsAsync(reading, ids, operationId);
        Assert.Single(reading.Receipt.GetProperty("verification").EnumerateArray());
    }

    [Fact]
    public async Task A_completed_two_recipe_delivery_with_the_complete_human_approval_reports_both_members_in_recorded_order()
    {
        using var host = StartHost(runSupervisor: true);
        var (ids, operationId) = await DeliverAsync(host, recipes: 2, completeSet: true);

        var reading = await ReceiptAsync(host, ids.RunId);

        await AssertReceiptMatchesRecordedRowsAsync(reading, ids, operationId);
        var members = reading.Receipt.GetProperty("verification").EnumerateArray().ToList();
        Assert.Equal(2, members.Count);
        Assert.Equal(ids.AllExecutionIds!, members.Select(member => member.GetProperty("executionId").GetGuid()));
        Assert.Equal(["Backend tests 1", "Backend tests 2"], members.Select(member => member.GetProperty("commandName").GetString()));
    }

    [Fact]
    public async Task The_receipt_survives_a_restart_byte_for_byte_and_reading_it_changes_nothing()
    {
        string first;
        LocalCommitLineageIds ids;
        string before;
        using (var host = StartHost(runSupervisor: true))
        {
            (ids, _) = await DeliverAsync(host, recipes: 2, completeSet: true);
            first = await AvailableBodyAsync(host, ids.RunId);
            before = await DatabaseFingerprintAsync();
            var mainBefore = Scene.MainRepositoryFingerprint();
            for (var read = 0; read < 3; read++)
            {
                Assert.Equal(first, (await ReceiptAsync(host, ids.RunId)).Body);
            }

            Assert.Equal(before, await DatabaseFingerprintAsync());
            Assert.Equal(mainBefore, Scene.MainRepositoryFingerprint());
        }

        using var restarted = StartHost(runSupervisor: true);
        Assert.Equal(first, (await ReceiptAsync(restarted, ids.RunId)).Body);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task Later_recipe_changes_reruns_and_newer_checkpoints_never_replace_an_intact_historical_receipt()
    {
        using var host = StartHost(runSupervisor: true);
        var (ids, operationId) = await DeliverAsync(host, recipes: 2, completeSet: true);
        var historical = await AvailableBodyAsync(host, ids.RunId);

        await using (var db = OpenDb())
        {
            var commands = await db.VerificationCommands.AsNoTracking().Where(command => command.ProjectId == ids.ProjectId)
                .OrderBy(command => command.CommandNumber).ToListAsync();
            using var client = LocalCommitLineage.AuthenticatedClient(host);
            var edit = await client.PutAsJsonAsync(
                $"/api/projects/{ids.ProjectId}/verification-commands/{commands[0].Id}",
                new UpdateVerificationCommandRequest("Renamed later", @"C:\other.exe", ["different"], 60, true));
            Assert.True(edit.IsSuccessStatusCode, await edit.Content.ReadAsStringAsync());
            var disable = await client.PutAsJsonAsync(
                $"/api/projects/{ids.ProjectId}/verification-commands/{commands[1].Id}",
                new UpdateVerificationCommandRequest(commands[1].Name, commands[1].ExecutablePath, commands[1].Arguments, commands[1].TimeoutSeconds, false));
            Assert.True(disable.IsSuccessStatusCode, await disable.Content.ReadAsStringAsync());
        }

        Assert.Equal(historical, (await ReceiptAsync(host, ids.RunId)).Body);

        await using (var db = OpenDb())
        {
            var workspace = await db.GitWorkspaces.AsNoTracking().SingleAsync(candidate => candidate.Id == ids.WorkspaceId);
            Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
        }

        // A later run in the same workspace, with its own checkpoint, executions and approvals, delivers after the first.
        var second = await LocalCommitLineage.SeedAsync(host, Scene, continueFrom: ids);
        Assert.NotEqual(ids.CheckpointId, second.CheckpointId);
        Assert.Equal(historical, (await ReceiptAsync(host, ids.RunId)).Body);
        Assert.Equal("NotRecorded", (await ReceiptAsync(host, second.RunId)).State);

        var secondOperationId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, second.RunId, second, secondOperationId)).StatusCode);
        await WaitForAsync(host, second.RunId, status => status.Operation?.Status == "Completed");

        Assert.Equal(historical, (await ReceiptAsync(host, ids.RunId)).Body);
        var secondReading = await ReceiptAsync(host, second.RunId);
        Assert.Equal("Available", secondReading.State);
        Assert.Equal(second.RunId, secondReading.Receipt.GetProperty("runId").GetGuid());
        Assert.Equal(secondOperationId, secondReading.Receipt.GetProperty("operationId").GetGuid());
        Assert.Equal(second.CheckpointId, secondReading.Receipt.GetProperty("checkpoint").GetProperty("id").GetGuid());
        Assert.NotEqual(historical, secondReading.Body);
        Assert.Equal(operationId, JsonDocument.Parse(historical).RootElement.GetProperty("receipt").GetProperty("operationId").GetGuid());
    }

    [Fact]
    public async Task A_later_human_decision_never_replaces_the_pinned_one()
    {
        using var host = StartHost(runSupervisor: true);
        var (ids, _) = await DeliverAsync(host, recipes: 2, completeSet: true);
        var historical = await AvailableBodyAsync(host, ids.RunId);

        // The host refuses new reviews once the run is delivered, so the later decision is written as a durable row directly.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var checkpoint = await db.GitCheckpoints.AsNoTracking().SingleAsync(candidate => candidate.Id == ids.CheckpointId);
            var pinned = await db.CheckpointReviewEvidence.AsNoTracking()
                .Where(row => row.CheckpointReviewId == ids.HumanReviewId).ToListAsync();
            var laterId = Guid.NewGuid();
            var evidence = pinned.Select(row => CheckpointReviewEvidence.Observe(
                Guid.NewGuid(), laterId, row.VerificationCommandId, row.VerificationExecutionId, row.VerificationExecutionNumber,
                row.VerificationExecutionCheckpointFingerprintSha256, row.VerificationExecutionStatus, row.VerificationExecutionOutcome,
                row.VerificationExecutionExitCode)).ToArray();
            db.CheckpointReviews.Add(CheckpointReview.Record(
                laterId, ids.ProjectId, ids.WorkspaceId, ids.CheckpointId, checkpoint.CheckpointNumber, checkpoint.FingerprintSha256,
                ReviewActorKind.Human, ReviewDecision.ChangesRequested, DateTimeOffset.UtcNow.AddMinutes(5), evidence));
            await db.SaveChangesAsync();
        }

        var reading = await ReceiptAsync(host, ids.RunId);
        Assert.Equal(historical, reading.Body);
        Assert.Equal(ids.HumanReviewId, reading.Receipt.GetProperty("humanReview").GetProperty("reviewId").GetGuid());
        Assert.Equal("Approved", reading.Receipt.GetProperty("humanReview").GetProperty("decision").GetString());
    }

    [Fact]
    public async Task The_maximum_of_thirty_two_recorded_members_is_reported_whole_and_in_order()
    {
        using var host = StartHost(runSupervisor: true);
        var (ids, operationId) = await DeliverAsync(host, recipes: 32, completeSet: true);

        var reading = await ReceiptAsync(host, ids.RunId);

        await AssertReceiptMatchesRecordedRowsAsync(reading, ids, operationId);
        Assert.Equal(32, reading.Receipt.GetProperty("verification").GetArrayLength());
    }

    [Fact]
    public async Task A_newer_verification_execution_of_the_same_recipe_does_not_replace_the_pinned_member()
    {
        using var host = StartHost(runSupervisor: true);
        var (ids, _) = await DeliverAsync(host, recipes: 2, completeSet: true);
        var historical = await AvailableBodyAsync(host, ids.RunId);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var project = await db.Projects.SingleAsync(candidate => candidate.Id == ids.ProjectId);
            var workspace = await db.GitWorkspaces.SingleAsync(candidate => candidate.Id == ids.WorkspaceId);
            var checkpoint = await db.GitCheckpoints.SingleAsync(candidate => candidate.Id == ids.CheckpointId);
            var command = await db.VerificationCommands.SingleAsync(candidate => candidate.Id == ids.CommandId);
            var rerun = VerificationExecution.Claim(
                Guid.NewGuid(), project.Id, project.ReserveVerificationExecutionNumber(), workspace, checkpoint, command, DateTimeOffset.UtcNow);
            rerun.MarkDispatched(DateTimeOffset.UtcNow);
            rerun.Complete(VerificationExecutionOutcome.Exited, 1, checkpoint.FingerprintSha256, DateTimeOffset.UtcNow);
            db.VerificationExecutions.Add(rerun);
            await db.SaveChangesAsync();
        }

        Assert.Equal(historical, (await ReceiptAsync(host, ids.RunId)).Body);
    }
}
