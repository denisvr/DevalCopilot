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
/// Exercises <c>GET /api/runs/{runId}/agent-attempts/claude-critical-review</c> against the real
/// Api host: authentication, the safe 404 for an unknown run, the explicit
/// <c>hasAttempt: false</c> 200 body for a run that has never requested a Claude critical review
/// (never an ambiguous null/204 — see <c>GetClaudeCriticalReviewAttemptStatusEndpoint</c>'s own
/// doc comment), the populated projection — including the reviewed Proposal's own message
/// identity — once an attempt exists, and that its artifact metadata never exposes a storage path
/// or content hash. Mirrors <c>GetAgentAttemptStatusEndpointTests</c> exactly. Shares one
/// <see cref="ClaudeCriticalReviewApiWebApplicationFactory"/> across the class: nothing here
/// touches the host-scoped <c>HostCapabilitySnapshot</c> row or seeds a <c>Running</c>,
/// dispatch-eligible attempt, so there is nothing for the removed background supervisors to race —
/// every attempt seeded below is already terminal before the request is ever made.
/// </summary>
public sealed class GetClaudeCriticalReviewAttemptStatusEndpointTests(ClaudeCriticalReviewApiWebApplicationFactory factory)
    : IClassFixture<ClaudeCriticalReviewApiWebApplicationFactory>
{
    private static readonly string Fingerprint = ClaudeCriticalReviewApiWebApplicationFactory.MatchingFingerprint;

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

        var response = await client.GetAsync($"/api/runs/{Guid.NewGuid()}/agent-attempts/claude-critical-review");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_ok_with_has_attempt_false_for_a_run_with_no_critical_review_attempt_yet()
    {
        var (runId, _, _) = await SeedRunAsync();
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync($"/api/runs/{runId}/agent-attempts/claude-critical-review");
        var body = await response.Content.ReadAsStringAsync();

        // A real, always-non-null 200 body: hasAttempt is the explicit discriminator, never an
        // ambiguous 204/null response — every other field is null/empty when it is false.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.False(root.GetProperty("hasAttempt").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("attemptId").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("attemptNumber").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("reviewedProposalMessageId").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("status").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("outcome").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("claimedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("dispatchedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("completedAtUtc").ValueKind);
        Assert.Empty(root.GetProperty("artifacts").EnumerateArray());
    }

    [Fact]
    public async Task Returns_a_safe_not_found_response_for_a_run_that_does_not_exist()
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync($"/api/runs/{Guid.NewGuid()}/agent-attempts/claude-critical-review");
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
        var reviewedProposalMessageId = Guid.NewGuid();
        Guid attemptId;

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var manifestArtifactId = Guid.NewGuid();
            var attempt = Attempt.ClaimAgentCriticalReview(
                Guid.NewGuid(), runId, 1, workspaceId, checkpointId, Fingerprint, manifestArtifactId,
                TimeSpan.FromMinutes(10), 262144, 524288, now);
            attempt.MarkAgentDispatched(now.AddSeconds(1));
            attempt.CompleteAgent(AgentOutcome.Accepted, Fingerprint, now.AddSeconds(5));
            attemptId = attempt.Id;
            dbContext.Attempts.Add(attempt);
            dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, reviewedProposalMessageId, sequence: 0));

            dbContext.Artifacts.Add(Artifact.Record(
                manifestArtifactId, runId, attempt.Id, ArtifactPurpose.AgentContextManifest, "application/json",
                $@"agent-attempts\{runId:N}\{attempt.Id:N}\context-manifest.sealed", "sha256:" + new string('c', 64),
                512, truncated: false, ArtifactCaptureOutcome.Captured, ArtifactSensitivity.HostConstructedContent,
                ArtifactRetentionPolicy.RetainUntilRunDeleted, now));
            dbContext.Artifacts.Add(Artifact.Record(
                Guid.NewGuid(), runId, attempt.Id, ArtifactPurpose.AgentStandardOutput, "application/jsonl",
                $@"agent-attempts\{runId:N}\{attempt.Id:N}\stdout.sealed", "sha256:" + new string('d', 64),
                4096, truncated: true, ArtifactCaptureOutcome.Captured, ArtifactSensitivity.RedactedBestEffort,
                ArtifactRetentionPolicy.RetainUntilRunDeleted, now));

            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/runs/{runId}/agent-attempts/claude-critical-review");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.True(root.GetProperty("hasAttempt").GetBoolean());
        Assert.Equal(attemptId, root.GetProperty("attemptId").GetGuid());
        Assert.Equal(1, root.GetProperty("attemptNumber").GetInt32());
        Assert.Equal(reviewedProposalMessageId, root.GetProperty("reviewedProposalMessageId").GetGuid());
        Assert.Equal("Completed", root.GetProperty("status").GetString());
        Assert.Equal("Accepted", root.GetProperty("outcome").GetString());
        Assert.True(root.TryGetProperty("claimedAtUtc", out _));
        Assert.False(string.IsNullOrEmpty(root.GetProperty("dispatchedAtUtc").GetString()));
        Assert.False(string.IsNullOrEmpty(root.GetProperty("completedAtUtc").GetString()));

        var expectedArtifactFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "purpose", "byteLength", "truncated", "captureOutcome",
        };

        var artifacts = root.GetProperty("artifacts");
        Assert.Equal(2, artifacts.GetArrayLength());
        foreach (var artifact in artifacts.EnumerateArray())
        {
            var propertyNames = artifact.EnumerateObject().Select(property => property.Name).ToArray();
            Assert.Equal(expectedArtifactFields.Count, propertyNames.Length);
            Assert.All(propertyNames, name => Assert.Contains(name, expectedArtifactFields));
            Assert.True(artifact.GetProperty("byteLength").GetInt64() > 0);
        }

        Assert.Contains(artifacts.EnumerateArray(), a => a.GetProperty("purpose").GetString() == "AgentContextManifest");
        Assert.Contains(artifacts.EnumerateArray(), a => a.GetProperty("purpose").GetString() == "AgentStandardOutput" && a.GetProperty("truncated").GetBoolean());

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

            var firstAttempt = Attempt.ClaimAgentCriticalReview(
                Guid.NewGuid(), runId, 1, workspaceId, checkpointId, Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, now);
            firstAttempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, now.AddSeconds(1));
            dbContext.Attempts.Add(firstAttempt);

            var secondAttempt = Attempt.ClaimAgentCriticalReview(
                Guid.NewGuid(), runId, 2, workspaceId, checkpointId, Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, now.AddSeconds(2));
            secondAttempt.MarkAgentDispatched(now.AddSeconds(3));
            secondAttempt.CompleteAgent(AgentOutcome.Challenged, Fingerprint, now.AddSeconds(4));
            dbContext.Attempts.Add(secondAttempt);

            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/runs/{runId}/agent-attempts/claude-critical-review");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(2, document.RootElement.GetProperty("attemptNumber").GetInt32());
        Assert.Equal("Challenged", document.RootElement.GetProperty("outcome").GetString());
    }

    /// <summary>
    /// This status projection is fully generic over <see cref="AgentOutcome"/> (it just calls
    /// <c>ToString()</c> on whatever the query returns) — no endpoint or query code change was
    /// needed to expose <see cref="AgentOutcome.InputAlreadyReviewed"/> once the Domain/Application
    /// layers gained it. This regression test proves that genuinely, end to end through the real
    /// Api host, rather than merely trusting the projection's genericness by inspection.
    /// </summary>
    [Fact]
    public async Task Returns_input_already_reviewed_as_a_terminal_outcome_distinct_from_workspace_no_longer_eligible()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync();
        var now = DateTimeOffset.UtcNow;
        Guid attemptId;

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();

            var attempt = Attempt.ClaimAgentCriticalReview(
                Guid.NewGuid(), runId, 1, workspaceId, checkpointId, Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, now);
            attempt.CompleteAgent(AgentOutcome.InputAlreadyReviewed, completionFingerprintSha256: null, now.AddSeconds(1));
            attemptId = attempt.Id;
            dbContext.Attempts.Add(attempt);

            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/runs/{runId}/agent-attempts/claude-critical-review");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.Equal(attemptId, root.GetProperty("attemptId").GetGuid());
        Assert.Equal("Failed", root.GetProperty("status").GetString());
        Assert.Equal("InputAlreadyReviewed", root.GetProperty("outcome").GetString());
        Assert.NotEqual("WorkspaceNoLongerEligible", root.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("dispatchedAtUtc").ValueKind);
    }

    [Fact]
    public async Task Returns_a_null_json_truncated_field_for_an_artifact_with_unknown_truncation()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync();
        var now = DateTimeOffset.UtcNow;

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var attempt = Attempt.ClaimAgentCriticalReview(
                Guid.NewGuid(), runId, 1, workspaceId, checkpointId, Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, now);
            attempt.MarkAgentDispatched(now.AddSeconds(1));
            attempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, now.AddSeconds(2));
            dbContext.Attempts.Add(attempt);

            // Truncated is null exactly when it is genuinely unknown — an artifact recovered
            // from a host interruption, never a stand-in for false.
            dbContext.Artifacts.Add(Artifact.Record(
                Guid.NewGuid(), runId, attempt.Id, ArtifactPurpose.AgentStandardOutput, "application/jsonl",
                $@"agent-attempts\{runId:N}\{attempt.Id:N}\stdout.sealed", "sha256:" + new string('f', 64),
                2048, truncated: null, ArtifactCaptureOutcome.PartialHostInterrupted, ArtifactSensitivity.RedactedBestEffort,
                ArtifactRetentionPolicy.RetainUntilRunDeleted, now));

            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/runs/{runId}/agent-attempts/claude-critical-review");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var artifact = Assert.Single(document.RootElement.GetProperty("artifacts").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, artifact.GetProperty("truncated").ValueKind);
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
        Assert.DoesNotContain("relativeStoragePath", body, StringComparison.Ordinal);
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

        var project = Project.Register(Guid.NewGuid(), "Claude review status project", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Review the ledger proposal", now);
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
