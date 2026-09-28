using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

file static class NoBomUtf8
{
    // File.WriteAllTextAsync(path, text, Encoding.UTF8) writes a 3-byte BOM preamble, which would
    // shift every byte offset this test suite deliberately computes by hand — sealed capture
    // itself never writes one either.
    public static readonly Encoding Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}

/// <summary>
/// Exercises
/// <c>GET /api/runs/{runId}/collaboration-messages/{messageId}/evidence/artifact-window/{purpose}</c>
/// against the real Api host and a real <see cref="FilesystemArtifactStoreTests"/>-equivalent
/// sealed-file boundary (the same <see cref="IArtifactStore"/> singleton
/// <see cref="ApiWebApplicationFactory"/> already wires for every other artifact-reading
/// endpoint): authentication, all four allowlisted purposes, a genuine multi-window UTF-8 split
/// boundary with a monotonic cursor, an unrecorded artifact, a recorded artifact whose sealed file
/// is absent or tampered, a path-escaping stored path, incoherent/foreign/simulated links, and that
/// no response ever discloses a storage path or content hash.
/// </summary>
public sealed class GetSealedAgentArtifactWindowEndpointTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly string Fingerprint = new('a', 64);

    private const string ProposalContent =
        "{\"scope\":\"Ledger\",\"implementationSteps\":\"Add the table then the query\",\"risks\":\"Unbounded content\",\"verificationPlan\":\"Tests\",\"escalationPoints\":\"None expected\"}";

    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private static string RouteFor(string purpose) => purpose switch
    {
        nameof(ArtifactPurpose.AgentContextManifest) => "context-manifest",
        nameof(ArtifactPurpose.AgentStandardOutput) => "stdout",
        nameof(ArtifactPurpose.AgentStandardError) => "stderr",
        nameof(ArtifactPurpose.AgentFinalResponse) => "final-response",
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null),
    };

    [Fact]
    public async Task Requires_authentication()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/runs/{Guid.NewGuid()}/collaboration-messages/{Guid.NewGuid()}/evidence/artifact-window/final-response");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_a_safe_not_found_for_an_unrecognized_purpose_segment()
    {
        var (runId, attemptId, messageId) = await SeedLinkedProposalAsync();
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync(
            $"/api/runs/{runId}/collaboration-messages/{messageId}/evidence/artifact-window/not-a-real-purpose");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Rejects_a_negative_offset()
    {
        var (runId, attemptId, messageId) = await SeedLinkedProposalAsync();
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync(
            $"/api/runs/{runId}/collaboration-messages/{messageId}/evidence/artifact-window/final-response?fromOffset=-1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Returns_a_safe_not_found_for_an_unknown_message()
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync(
            $"/api/runs/{Guid.NewGuid()}/collaboration-messages/{Guid.NewGuid()}/evidence/artifact-window/final-response");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("collaboration_messages.not_found", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Returns_a_safe_not_found_when_the_message_belongs_to_a_different_run()
    {
        var (_, _, messageId) = await SeedLinkedProposalAsync();
        var otherRunId = await SeedRunAsync();
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync(
            $"/api/runs/{otherRunId}/collaboration-messages/{messageId}/evidence/artifact-window/final-response");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_no_agent_evidence_for_a_simulated_message()
    {
        var runId = await SeedRunAsync();
        Guid messageId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var message = CollaborationMessage.Record(
                Guid.NewGuid(), runId, null, CollaborationMessage.ProtocolVersionOne,
                ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
                ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
                CollaborationMessageType.Proposal, null, "A simulated proposal", ProposalContent,
                CollaborationMessageProvenance.Simulated, DateTimeOffset.UtcNow);
            messageId = message.Id;
            dbContext.CollaborationMessages.Add(message);
            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync(
            $"/api/runs/{runId}/collaboration-messages/{messageId}/evidence/artifact-window/final-response");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("NoAgentEvidence", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(string.Empty, document.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Returns_attempt_link_broken_when_the_linked_attempts_role_is_incoherent()
    {
        var (runId, attemptId, messageId) = await SeedLinkedProposalAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE attempts SET AgentRole = {nameof(AgentRole.Implementer)} WHERE Id = {attemptId}");
        }

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync(
            $"/api/runs/{runId}/collaboration-messages/{messageId}/evidence/artifact-window/final-response");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("AttemptLinkBroken", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Returns_artifact_not_found_when_no_row_is_recorded_for_the_requested_purpose()
    {
        var (runId, _, messageId) = await SeedLinkedProposalAsync();
        using var client = CreateAuthenticatedClient();

        // The proposal-linked attempt only ever records an AgentFinalResponse artifact — stdout
        // was never captured/recorded for it.
        var response = await client.GetAsync(
            $"/api/runs/{runId}/collaboration-messages/{messageId}/evidence/artifact-window/stdout");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("ArtifactNotFound", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Returns_missing_when_the_recorded_artifacts_sealed_file_is_absent()
    {
        var (runId, attemptId, messageId) = await SeedLinkedProposalAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            // Records a plausible-looking, never-sealed artifact row for a distinct purpose —
            // its deterministic path is never actually written to disk.
            dbContext.Artifacts.Add(Artifact.Record(
                Guid.NewGuid(), runId, attemptId, ArtifactPurpose.AgentStandardError, "text/plain",
                @"runs\r\attempts\a\stderr.sealed", "sha256:never-written", 42, false,
                ArtifactCaptureOutcome.Captured, ArtifactSensitivity.RedactedBestEffort, ArtifactRetentionPolicy.RetainUntilRunDeleted, DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync(
            $"/api/runs/{runId}/collaboration-messages/{messageId}/evidence/artifact-window/stderr");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("Missing", document.RootElement.GetProperty("status").GetString());
        AssertNoStorageDisclosure(body);
    }

    [Fact]
    public async Task Returns_missing_for_a_path_escaping_stored_relative_path_rather_than_disclosing_it()
    {
        var (runId, attemptId, messageId) = await SeedLinkedProposalAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            dbContext.Artifacts.Add(Artifact.Record(
                Guid.NewGuid(), runId, attemptId, ArtifactPurpose.AgentContextManifest, "text/plain",
                @"..\..\escape.sealed", "sha256:escape", 4, false,
                ArtifactCaptureOutcome.Captured, ArtifactSensitivity.RedactedBestEffort, ArtifactRetentionPolicy.RetainUntilRunDeleted, DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync(
            $"/api/runs/{runId}/collaboration-messages/{messageId}/evidence/artifact-window/context-manifest");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("Missing", document.RootElement.GetProperty("status").GetString());
        AssertNoStorageDisclosure(body);
    }

    [Fact]
    public async Task Returns_an_integrity_mismatch_for_a_tampered_recorded_length_rather_than_serving_bytes()
    {
        var (runId, attemptId, messageId) = await SeedLinkedProposalAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var artifactStore = scope.ServiceProvider.GetRequiredService<IArtifactStore>();
            var partialPath = artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentStandardOutput);
            Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
            await File.WriteAllTextAsync(partialPath, "genuine sealed content", NoBomUtf8.Encoding);
            var sealedFile = await artifactStore.SealAsync(runId, attemptId, ArtifactPurpose.AgentStandardOutput, CancellationToken.None);
            Assert.NotNull(sealedFile);

            // Records a byte length that does not match the real sealed file's own actual length —
            // simulates a tampered or corrupted durable record without touching the real bytes.
            dbContext.Artifacts.Add(Artifact.Record(
                Guid.NewGuid(), runId, attemptId, ArtifactPurpose.AgentStandardOutput, "text/plain",
                sealedFile!.RelativeStoragePath, sealedFile.ContentHash, sealedFile.ByteLength + 1, false,
                ArtifactCaptureOutcome.Captured, ArtifactSensitivity.RedactedBestEffort, ArtifactRetentionPolicy.RetainUntilRunDeleted, DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync(
            $"/api/runs/{runId}/collaboration-messages/{messageId}/evidence/artifact-window/stdout");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("IntegrityMismatch", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(string.Empty, document.RootElement.GetProperty("text").GetString());
        AssertNoStorageDisclosure(body);
    }

    [Theory]
    [InlineData(nameof(ArtifactPurpose.AgentContextManifest))]
    [InlineData(nameof(ArtifactPurpose.AgentStandardOutput))]
    [InlineData(nameof(ArtifactPurpose.AgentStandardError))]
    [InlineData(nameof(ArtifactPurpose.AgentFinalResponse))]
    public async Task Returns_the_verified_content_for_every_allowlisted_purpose(string purposeName)
    {
        var purpose = Enum.Parse<ArtifactPurpose>(purposeName);
        var runId = await SeedRunAsync();
        var (attemptId, messageId) = await SeedDispatchedAttemptWithMessageAsync(runId);
        await SealArtifactAsync(runId, attemptId, purpose, "verified artifact content");

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync(
            $"/api/runs/{runId}/collaboration-messages/{messageId}/evidence/artifact-window/{RouteFor(purposeName)}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal("Ok", root.GetProperty("status").GetString());
        Assert.Equal("verified artifact content", root.GetProperty("text").GetString());
        Assert.False(root.GetProperty("truncated").GetBoolean());
        AssertNoStorageDisclosure(body);
    }

    [Fact]
    public async Task Splits_a_utf8_codepoint_across_windows_with_a_monotonic_cursor_and_reconstructs_the_full_text()
    {
        var runId = await SeedRunAsync();
        var (attemptId, messageId) = await SeedDispatchedAttemptWithMessageAsync(runId);

        // 61 ASCII bytes followed by one 4-byte UTF-8 codepoint (an emoji) landing exactly across
        // the minimum allowed maxBytes (64) window boundary — bytes [61,64) of the emoji fall
        // inside the first window's byte budget, but its 4th byte does not, so the whole codepoint
        // must be deferred to the next window rather than ever being split.
        var content = new string('a', 61) + "\U0001F600" + new string('b', 20);
        await SealArtifactAsync(runId, attemptId, ArtifactPurpose.AgentFinalResponse, content);

        using var client = CreateAuthenticatedClient();
        var firstResponse = await client.GetAsync(
            $"/api/runs/{runId}/collaboration-messages/{messageId}/evidence/artifact-window/final-response?fromOffset=0&maxBytes=64");
        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        using var firstDocument = JsonDocument.Parse(firstBody);
        var firstRoot = firstDocument.RootElement;

        Assert.Equal("Ok", firstRoot.GetProperty("status").GetString());
        var firstText = firstRoot.GetProperty("text").GetString()!;
        var firstNextOffset = firstRoot.GetProperty("nextOffset").GetInt64();
        // The incomplete trailing codepoint is held back entirely — the cursor stops before it,
        // never mid-codepoint.
        Assert.Equal(61, firstNextOffset);
        Assert.Equal(new string('a', 61), firstText);

        var secondResponse = await client.GetAsync(
            $"/api/runs/{runId}/collaboration-messages/{messageId}/evidence/artifact-window/final-response?fromOffset={firstNextOffset}&maxBytes=64");
        var secondBody = await secondResponse.Content.ReadAsStringAsync();
        using var secondDocument = JsonDocument.Parse(secondBody);
        var secondRoot = secondDocument.RootElement;

        Assert.Equal("Ok", secondRoot.GetProperty("status").GetString());
        var secondText = secondRoot.GetProperty("text").GetString()!;
        var secondNextOffset = secondRoot.GetProperty("nextOffset").GetInt64();
        Assert.True(secondNextOffset > firstNextOffset);

        Assert.Equal(content, firstText + secondText);
    }

    private static void AssertNoStorageDisclosure(string body)
    {
        Assert.DoesNotContain("RelativeStoragePath", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ContentHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sha256:", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".sealed", body, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
    }

    private async Task<Guid> SeedRunAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "Sealed artifact window", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Sealed artifact window run", now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync();
        return run.Id;
    }

    private async Task<(Guid AttemptId, Guid MessageId)> SeedDispatchedAttemptWithMessageAsync(Guid runId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), runId, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, now, 1);
        attempt.MarkAgentDispatched(now.AddSeconds(1));
        attempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, now.AddSeconds(2), TestProcessEvidence.CleanExit);
        var message = CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
            CollaborationMessageType.Proposal, null, "A proposal", ProposalContent, now.AddSeconds(3));
        dbContext.Attempts.Add(attempt);
        dbContext.CollaborationMessages.Add(message);
        await dbContext.SaveChangesAsync();
        return (attempt.Id, message.Id);
    }

    private async Task SealArtifactAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, string content)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var artifactStore = scope.ServiceProvider.GetRequiredService<IArtifactStore>();
        var partialPath = artifactStore.GetPartialPath(runId, attemptId, purpose);
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        await File.WriteAllTextAsync(partialPath, content, NoBomUtf8.Encoding);
        var sealedFile = await artifactStore.SealAsync(runId, attemptId, purpose, CancellationToken.None);
        Assert.NotNull(sealedFile);

        dbContext.Artifacts.Add(Artifact.Record(
            Guid.NewGuid(), runId, attemptId, purpose, "text/plain",
            sealedFile!.RelativeStoragePath, sealedFile.ContentHash, sealedFile.ByteLength, false,
            ArtifactCaptureOutcome.Captured, ArtifactSensitivity.RedactedBestEffort, ArtifactRetentionPolicy.RetainUntilRunDeleted, DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync();
    }

    private async Task<(Guid RunId, Guid AttemptId, Guid MessageId)> SeedLinkedProposalAsync()
    {
        var runId = await SeedRunAsync();
        var (attemptId, messageId) = await SeedDispatchedAttemptWithMessageAsync(runId);
        await SealArtifactAsync(runId, attemptId, ArtifactPurpose.AgentFinalResponse, "final response body");
        return (runId, attemptId, messageId);
    }
}
