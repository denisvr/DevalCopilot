using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DevalCopilot.Api.Features.Runs.RequestLocalCommit;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>ADR-0029 through the protected HTTP boundary: real Git, file-backed SQLite, the real supervisor and the production
/// handlers that wrote the whole review lineage.</summary>
public sealed class LocalCommitEndpointTests : LocalCommitTestBase
{
    [Fact]
    public async Task A_complete_approved_checkpoint_is_committed_locally_and_the_run_completes()
    {
        using var host = StartHost(runSupervisor: true);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        var mainBefore = Scene.MainRepositoryFingerprint();
        var eligible = await StatusAsync(host, ids.RunId);
        Assert.True(eligible.Eligible);
        Assert.Equal(ids.CheckpointId, eligible.CheckpointId);
        Assert.Equal(ids.ReviewAttemptId, eligible.CodeReviewAttemptId);
        Assert.Equal(ids.HumanReviewId, eligible.HumanCheckpointReviewId);

        var operationId = Guid.NewGuid();
        var response = await PostAsync(host, ids.RunId, ids, operationId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var admitted = (await response.Content.ReadFromJsonAsync<LocalCommitOperationResponse>())!;
        Assert.Equal(operationId, admitted.OperationId);
        Assert.Null(admitted.CommitSha);

        var done = await WaitForAsync(host, ids.RunId, status => status.Operation?.Status == "Completed");
        var operation = done.Operation!;
        Assert.NotNull(operation.CommitSha);
        Assert.Equal(Scene.BaselineCommit, operation.ParentCommitSha);
        Assert.Equal(Scene.RunMainGit("rev-parse", "refs/heads/" + Scene.BranchName).Trim(), operation.CommitSha);
        Assert.Equal(operation.TreeSha, Scene.RunMainGit("rev-parse", operation.CommitSha + "^{tree}").Trim());
        Assert.Equal(2, operation.ChangedPathCount);
        Assert.Equal("approved change\n", Scene.RunMainGit("cat-file", "-p", operation.CommitSha + ":a.txt").Replace("\r", string.Empty));

        var run = await RunAsync(host, ids.RunId);
        Assert.Equal(RunLifecycle.Completed, run.Lifecycle);
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceAsync(host, ids.WorkspaceId)).Status);
        var events = await EventTypesAsync(host, ids.RunId);
        var local = events.Where(type => type.StartsWith("local_commit.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(["local_commit.admitted", "local_commit.executing", "local_commit.completed"], local);
        Assert.Equal(mainBefore, Scene.MainRepositoryFingerprint());
        Assert.False(File.Exists(Scene.IndexPath + ".lock"));
        Assert.Empty(Scene.StorageLeaves());

        // A replay of the identical operation returns the recorded operation without any further Git execution.
        var replay = await PostAsync(host, ids.RunId, ids, operationId);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("Completed", (await replay.Content.ReadFromJsonAsync<LocalCommitOperationResponse>())!.Status);
        Assert.Equal(1, await OperationCountAsync(host));
        Assert.Equal(operation.CommitSha, Scene.RunMainGit("rev-parse", "refs/heads/" + Scene.BranchName).Trim());
        Assert.Equal(events.Length, (await EventTypesAsync(host, ids.RunId)).Length);
    }

    [Fact]
    public async Task The_operation_requires_authentication_and_unknown_ids_write_nothing()
    {
        using var host = StartHost(runSupervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);

        using var anonymous = host.CreateClient();
        var unauthenticated = await anonymous.PostAsJsonAsync(
            $"/api/runs/{ids.RunId}/local-commit",
            new RequestLocalCommitRequest
            {
                OperationId = Guid.NewGuid(),
                CheckpointId = ids.CheckpointId,
                CodeReviewAttemptId = ids.ReviewAttemptId,
                HumanCheckpointReviewId = ids.HumanReviewId,
                Message = "Deliver",
            });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/runs/{ids.RunId}/local-commit")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(host, Guid.NewGuid(), ids)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(host, ids.RunId, ids, checkpointId: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(host, ids.RunId, ids, reviewAttemptId: Guid.NewGuid())).StatusCode);
        var humanMissing = await PostAsync(host, ids.RunId, ids, humanReviewId: Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, humanMissing.StatusCode);
        Assert.Equal("local_commit.human_approval_missing", await ErrorCodeAsync(humanMissing));

        using var authenticated = LocalCommitLineage.AuthenticatedClient(host);
        Assert.Equal(HttpStatusCode.NotFound, (await authenticated.GetAsync($"/api/runs/{Guid.NewGuid()}/local-commit")).StatusCode);
        Assert.Equal(0, await OperationCountAsync(host));
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceAsync(host, ids.WorkspaceId)).Status);
    }

    [Fact]
    public async Task Malformed_requests_are_refused_before_admission()
    {
        using var host = StartHost(runSupervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);

        foreach (var message in new[] { "", "   ", "a\r\nb", "ctrl\u0001char", new string('x', 2049), "Subject\n\nDevalCopilot-Operation: forged" })
        {
            var response = await PostAsync(host, ids.RunId, ids, message: message);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using var client = LocalCommitLineage.AuthenticatedClient(host);
        var oversized = await client.PostAsync(
            $"/api/runs/{ids.RunId}/local-commit", new StringContent(new string('x', 9 * 1024), new MediaTypeHeaderValue("application/json")));
        Assert.True(oversized.StatusCode is HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.BadRequest);
        Assert.Equal(0, await OperationCountAsync(host));
    }

    [Fact]
    public async Task One_operation_is_admitted_per_run_and_competing_or_changed_requests_conflict()
    {
        using var host = StartHost(runSupervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        var operationId = Guid.NewGuid();

        var first = await PostAsync(host, ids.RunId, ids, operationId);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(WorkspaceStatus.Committing, (await WorkspaceAsync(host, ids.WorkspaceId)).Status);

        var identical = await PostAsync(host, ids.RunId, ids, operationId);
        Assert.Equal(HttpStatusCode.OK, identical.StatusCode);
        Assert.Equal(operationId, (await identical.Content.ReadFromJsonAsync<LocalCommitOperationResponse>())!.OperationId);

        var changedMessage = await PostAsync(host, ids.RunId, ids, operationId, message: "A different message");
        Assert.Equal(HttpStatusCode.Conflict, changedMessage.StatusCode);
        Assert.Equal("local_commit.operation_conflict", await ErrorCodeAsync(changedMessage));
        var competing = await PostAsync(host, ids.RunId, ids, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, competing.StatusCode);
        Assert.Equal("local_commit.operation_conflict", await ErrorCodeAsync(competing));

        Assert.Equal(1, await OperationCountAsync(host));
        Assert.Equal(1, (await EventTypesAsync(host, ids.RunId)).Count(type => type == "local_commit.admitted"));
    }

    [Fact]
    public async Task Concurrent_identical_and_competing_requests_admit_exactly_one_operation()
    {
        using var host = StartHost(runSupervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        var operationId = Guid.NewGuid();

        var responses = await Task.WhenAll(
            PostAsync(host, ids.RunId, ids, operationId),
            PostAsync(host, ids.RunId, ids, operationId),
            PostAsync(host, ids.RunId, ids, Guid.NewGuid()),
            PostAsync(host, ids.RunId, ids, Guid.NewGuid()));

        // An unexpected status carries the bounded, sanitized head of its body and the server errors the host logged, so a failure of
        // this race is diagnosable from its own report instead of from a later passing run.
        var unexpected = new List<string>();
        foreach (var response in responses.Where(response => response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Conflict)))
        {
            unexpected.Add(await host.ServerErrors.DescribeAsync(response));
        }

        Assert.True(unexpected.Count == 0, $"Unexpected response(s): {string.Join(" || ", unexpected)}");
        Assert.Equal(1, await OperationCountAsync(host));

        // The transaction arbitrates all four requests together. Either identical request may be admitted and replayed (two OKs),
        // or either competing UUID may reserve the workspace first (one OK); in both cases one durable operation and event exist.
        Assert.InRange(responses.Count(response => response.StatusCode == HttpStatusCode.OK), 1, 2);
        Assert.InRange(responses.Count(response => response.StatusCode == HttpStatusCode.Conflict), 2, 3);
        Assert.Equal(1, (await EventTypesAsync(host, ids.RunId)).Count(type => type == "local_commit.admitted"));
    }
}
