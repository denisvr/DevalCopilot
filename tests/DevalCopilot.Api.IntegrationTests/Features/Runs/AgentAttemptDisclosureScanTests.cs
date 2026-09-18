using System.Net;
using System.Net.Http.Headers;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// A dedicated, full-pipeline no-disclosure scan across both Codex-planning endpoints together —
/// distinct from the narrower per-response assertions already embedded in
/// <see cref="RequestCodexPlanningAttemptEndpointTests"/> and
/// <see cref="GetAgentAttemptStatusEndpointTests"/>. It seeds one realistic scenario carrying
/// every category of sensitive-shaped value this slice's own domain and doc comments call out —
/// a local filesystem path, a resolved provider executable path, a checkpoint fingerprint, a
/// content hash, a sealed artifact storage path, and a credential-shaped string — then asserts
/// none of it is reachable in either endpoint's response body, in either direction (create
/// conflict, create success, and get status).
/// </summary>
public sealed class AgentAttemptDisclosureScanTests : IDisposable
{
    private const string CredentialShapedSecret = "ghp_abcdefghijklmnopqrstuvwxyz0123456789AB";
    private static readonly string Fingerprint = CodexPlanningApiWebApplicationFactory.MatchingFingerprint;

    private readonly CodexPlanningApiWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    [Fact]
    public async Task Neither_endpoint_ever_discloses_a_path_hash_fingerprint_or_credential_shaped_value()
    {
        var workspacePath = $@"C:\workspaces\{Guid.NewGuid():N}";
        var resolvedExecutablePath = $@"C:\Users\{CredentialShapedSecret}\AppData\Local\Codex\codex.exe";
        var sealedStoragePath = $@"agent-attempts\secret-workspace\{Guid.NewGuid():N}\stdout.sealed";
        var contentHash = "sha256:" + new string('e', 64);

        var (runId, workspaceId, checkpointId) = await SeedFullScenarioAsync(workspacePath, resolvedExecutablePath);

        using var client = CreateAuthenticatedClient();

        // 1. The create endpoint's own conflict path (an attempt is already running for this run).
        var conflictResponse = await client.PostAsync($"/api/runs/{runId}/agent-attempts/codex-plan", content: null);
        var conflictBody = await conflictResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
        AssertNoDisclosure(conflictBody, workspacePath, resolvedExecutablePath, contentHash, sealedStoragePath);

        // 2. The status endpoint over a terminal attempt carrying every sensitive-shaped field.
        await SeedTerminalAttemptWithArtifactAsync(runId, workspaceId, checkpointId, sealedStoragePath, contentHash);

        var statusResponse = await client.GetAsync($"/api/runs/{runId}/agent-attempts/codex-plan");
        var statusBody = await statusResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        AssertNoDisclosure(statusBody, workspacePath, resolvedExecutablePath, contentHash, sealedStoragePath);

        // 3. The create endpoint's success path for a second, freshly eligible run in the same
        // scenario (proving the golden-path body is equally silent about all of the above).
        var (secondRunId, _, _) = await SeedEligibleSecondRunAsync(workspacePath, resolvedExecutablePath);
        var successResponse = await client.PostAsync($"/api/runs/{secondRunId}/agent-attempts/codex-plan", content: null);
        var successBody = await successResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, successResponse.StatusCode);
        AssertNoDisclosure(successBody, workspacePath, resolvedExecutablePath, contentHash, sealedStoragePath);
    }

    private static void AssertNoDisclosure(
        string body, string workspacePath, string resolvedExecutablePath, string contentHash, string sealedStoragePath)
    {
        Assert.DoesNotContain(workspacePath, body, StringComparison.Ordinal);
        Assert.DoesNotContain(resolvedExecutablePath, body, StringComparison.Ordinal);
        Assert.DoesNotContain(contentHash, body, StringComparison.Ordinal);
        Assert.DoesNotContain(sealedStoragePath, body, StringComparison.Ordinal);
        Assert.DoesNotContain(Fingerprint, body, StringComparison.Ordinal);
        Assert.DoesNotContain(CredentialShapedSecret, body, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sha256:", body, StringComparison.Ordinal);
        Assert.DoesNotContain("credential", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bearer ", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("environment", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("resolvedExecutablePath", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("relativeStoragePath", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("contentHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", body, StringComparison.Ordinal);
    }

    private async Task<(Guid RunId, Guid WorkspaceId, Guid CheckpointId)> SeedFullScenarioAsync(
        string workspacePath, string resolvedExecutablePath)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;

        var project = Project.Register(Guid.NewGuid(), "Disclosure scan project", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Prove nothing sensitive leaks", now);
        run.Claim(now);

        var workspace = GitWorkspace.Prepare(Guid.NewGuid(), project.Id, 1, workspacePath, "branch", new string('a', 40), "main", now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, now, new string('a', 40), Fingerprint, []);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.RepositoryMutationLeases.Add(
            RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), now));

        var codex = await dbContext.HostCapabilitySnapshots.SingleAsync(s => s.Capability == Capability.CodexCli);
        codex.MarkDispatched(now);
        codex.RecordSuccess(CapabilityLaunchKind.DirectExecutable, resolvedExecutablePath, null, "1.2.3", now, now.AddMinutes(5));

        // Already running: exercises the create endpoint's conflict path in step 1.
        dbContext.Attempts.Add(Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, now));

        await dbContext.SaveChangesAsync();

        return (run.Id, workspace.Id, checkpoint.Id);
    }

    private async Task SeedTerminalAttemptWithArtifactAsync(
        Guid runId, Guid workspaceId, Guid checkpointId, string sealedStoragePath, string contentHash)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;

        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), runId, 2, workspaceId, checkpointId, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, now);
        attempt.MarkAgentDispatched(now.AddSeconds(1));
        attempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, now.AddSeconds(2));
        dbContext.Attempts.Add(attempt);

        dbContext.Artifacts.Add(Artifact.Record(
            Guid.NewGuid(), runId, attempt.Id, ArtifactPurpose.AgentStandardOutput, "application/jsonl",
            sealedStoragePath, contentHash, 2048, truncated: true, ArtifactCaptureOutcome.Captured,
            ArtifactSensitivity.RedactedBestEffort, ArtifactRetentionPolicy.RetainUntilRunDeleted, now));

        await dbContext.SaveChangesAsync();
    }

    private async Task<(Guid RunId, Guid WorkspaceId, Guid CheckpointId)> SeedEligibleSecondRunAsync(
        string workspacePath, string resolvedExecutablePath)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;

        var project = Project.Register(Guid.NewGuid(), "Disclosure scan project 2", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Prove the golden path stays silent too", now);

        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, workspacePath + "-2", "branch", new string('a', 40), "main", now);
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
