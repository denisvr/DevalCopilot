using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// Exercises <c>GET /api/runs/{runId}/agent-attempts/challenge-resolution</c> against the real Api
/// host: authentication, the safe 404 for an unknown run, the explicit <c>hasAttempt: false</c>
/// 200 body for a run that has never requested a resolution, the populated projection — including
/// the original Proposal and ordered Challenge message identities — once an attempt exists, and
/// that its artifact metadata never exposes a storage path or content hash. Mirrors
/// <c>GetClaudeCriticalReviewAttemptStatusEndpointTests</c> exactly, scoped down to the scenarios
/// genuinely specific to this projection's extra fields.
/// </summary>
public sealed class GetChallengeResolutionAttemptStatusEndpointTests(ChallengeResolutionApiWebApplicationFactory factory)
    : IClassFixture<ChallengeResolutionApiWebApplicationFactory>
{
    private static readonly string Fingerprint = ChallengeResolutionApiWebApplicationFactory.MatchingFingerprint;

    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    [Fact]
    public async Task Requires_authentication()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/runs/{Guid.NewGuid()}/agent-attempts/challenge-resolution");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_ok_with_has_attempt_false_for_a_run_with_no_resolution_attempt_yet()
    {
        var (runId, _, _) = await SeedRunAsync();
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync($"/api/runs/{runId}/agent-attempts/challenge-resolution");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.False(root.GetProperty("hasAttempt").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("attemptId").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("originalProposalMessageId").ValueKind);
        Assert.Empty(root.GetProperty("challengeMessageIds").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("status").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("outcome").ValueKind);
        Assert.Empty(root.GetProperty("artifacts").EnumerateArray());
    }

    [Fact]
    public async Task Returns_a_safe_not_found_response_for_a_run_that_does_not_exist()
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync($"/api/runs/{Guid.NewGuid()}/agent-attempts/challenge-resolution");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("runs.not_found", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_the_populated_projection_once_a_terminal_attempt_exists()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync();
        var now = DateTimeOffset.UtcNow;
        var originalProposalId = Guid.NewGuid();
        var challengeIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        Guid attemptId;

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var manifestArtifactId = Guid.NewGuid();
            var attempt = Attempt.ClaimAgentChallengeResolution(
                Guid.NewGuid(), runId, 1, workspaceId, checkpointId, Fingerprint, manifestArtifactId,
                TimeSpan.FromMinutes(10), 262144, 524288, now, 1);
            attempt.MarkAgentDispatched(now.AddSeconds(1));
            attempt.CompleteAgent(AgentOutcome.Resolved, Fingerprint, now.AddSeconds(5), processEvidence: TestProcessEvidence.CleanExit);
            attemptId = attempt.Id;
            dbContext.Attempts.Add(attempt);
            dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, originalProposalId, sequence: 0));
            for (var index = 0; index < challengeIds.Length; index++)
            {
                dbContext.AttemptInputMessages.Add(
                    AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, challengeIds[index], sequence: index + 1));
            }

            dbContext.Artifacts.Add(Artifact.Record(
                manifestArtifactId, runId, attempt.Id, ArtifactPurpose.AgentContextManifest, "application/json",
                $@"agent-attempts\{runId:N}\{attempt.Id:N}\context-manifest.sealed", "sha256:" + new string('c', 64),
                512, truncated: false, ArtifactCaptureOutcome.Captured, ArtifactSensitivity.HostConstructedContent,
                ArtifactRetentionPolicy.RetainUntilRunDeleted, now));

            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/runs/{runId}/agent-attempts/challenge-resolution");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.True(root.GetProperty("hasAttempt").GetBoolean());
        Assert.Equal(attemptId, root.GetProperty("attemptId").GetGuid());
        Assert.Equal(1, root.GetProperty("attemptNumber").GetInt32());
        Assert.Equal(originalProposalId, root.GetProperty("originalProposalMessageId").GetGuid());
        var returnedChallengeIds = root.GetProperty("challengeMessageIds").EnumerateArray().Select(e => e.GetGuid()).ToArray();
        Assert.Equal(challengeIds, returnedChallengeIds);
        Assert.Equal("Completed", root.GetProperty("status").GetString());
        Assert.Equal("Resolved", root.GetProperty("outcome").GetString());

        var artifacts = root.GetProperty("artifacts");
        Assert.Single(artifacts.EnumerateArray());
        AssertArtifactsNeverExposeStorageDetails(body);
    }

    [Fact]
    public async Task Returns_the_most_recently_numbered_attempt_when_more_than_one_exists()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync();
        var now = DateTimeOffset.UtcNow;

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();

            var firstAttempt = Attempt.ClaimAgentChallengeResolution(
                Guid.NewGuid(), runId, 1, workspaceId, checkpointId, Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, now, 1);
            firstAttempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, now.AddSeconds(1));
            dbContext.Attempts.Add(firstAttempt);
            dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), firstAttempt.Id, Guid.NewGuid(), sequence: 0));

            var secondAttempt = Attempt.ClaimAgentChallengeResolution(
                Guid.NewGuid(), runId, 2, workspaceId, checkpointId, Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, now.AddSeconds(2), 2);
            secondAttempt.MarkAgentDispatched(now.AddSeconds(3));
            secondAttempt.CompleteAgent(AgentOutcome.Resolved, Fingerprint, now.AddSeconds(4), processEvidence: TestProcessEvidence.CleanExit);
            dbContext.Attempts.Add(secondAttempt);
            dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), secondAttempt.Id, Guid.NewGuid(), sequence: 0));

            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/runs/{runId}/agent-attempts/challenge-resolution");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(2, document.RootElement.GetProperty("attemptNumber").GetInt32());
        Assert.Equal("Resolved", document.RootElement.GetProperty("outcome").GetString());
    }

    private static void AssertNoDisclosure(string body)
    {
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\", body, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertArtifactsNeverExposeStorageDetails(string body)
    {
        Assert.DoesNotContain("RelativeStoragePath", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ContentHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sha256:", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".sealed", body, StringComparison.Ordinal);
        Assert.DoesNotContain("agent-attempts\\", body, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\", body, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(Guid RunId, Guid WorkspaceId, Guid CheckpointId)> SeedRunAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;

        var project = Project.Register(Guid.NewGuid(), "Challenge resolution status project", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Resolve the challenged ledger proposal", now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, now, new string('a', 40), Fingerprint, []);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.RepositoryMutationLeases.Add(
            RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), now));

        await dbContext.SaveChangesAsync();

        return (run.Id, workspace.Id, checkpoint.Id);
    }
}
