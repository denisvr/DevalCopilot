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

/// <summary>
/// Exercises the run-scoped Agent-attempt history
/// (<c>GET /api/runs/{runId}/agent-attempts</c>), the selected-attempt evidence metadata
/// (<c>.../agent-attempts/{attemptId}/evidence</c>), and the sealed artifact window
/// (<c>.../evidence/artifact-window/{purpose}</c>) against the real Api host and artifact store:
/// authentication, descending stable paging including a run with more than 16 Agent attempts,
/// every supported role/provider pair, a failed attempt that emitted no collaboration message,
/// unknown/foreign/non-Agent/incoherent attempts, cross-run and disallowed artifacts, missing and
/// tampered files, multi-window UTF-8 reconstruction, and that metadata and window-envelope fields never
/// disclose a storage path, hash, provider session identifier, or prompt (verified artifact text is captured
/// content returned exactly and may itself contain such content).
/// </summary>
public sealed class AgentAttemptHistoryEndpointTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly string Fingerprint = new('a', 64);
    private const string SessionSecret = "session-secret-do-not-leak";
    private static readonly UTF8Encoding NoBomUtf8 = new(encoderShouldEmitUTF8Identifier: false);

    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private static string RouteFor(ArtifactPurpose purpose) => purpose switch
    {
        ArtifactPurpose.AgentContextManifest => "context-manifest",
        ArtifactPurpose.AgentStandardOutput => "stdout",
        ArtifactPurpose.AgentStandardError => "stderr",
        ArtifactPurpose.AgentFinalResponse => "final-response",
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null),
    };

    /// <summary>Parses a response and asserts non-disclosure over its metadata only: every property
    /// except a sealed window's <c>text</c>, which is verified captured content that must be returned
    /// exactly, whatever it contains. Property names are checked too, so an added path, hash,
    /// session, or prompt field is caught.</summary>
    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var root = JsonDocument.Parse(body).RootElement.Clone();
        if (root.ValueKind == JsonValueKind.Object)
        {
            AssertNoSensitiveDisclosure(string.Concat(
                root.EnumerateObject().Where(property => property.Name != "text").Select(property => $"\"{property.Name}\":{property.Value.GetRawText()},")));
        }
        else
        {
            AssertNoSensitiveDisclosure(body);
        }

        return root;
    }

    private static void AssertNoSensitiveDisclosure(string body)
    {
        Assert.DoesNotContain("RelativeStoragePath", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ContentHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sha256:", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".sealed", body, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain(SessionSecret, body, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prompt", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Every_route_requires_authentication()
    {
        using var client = factory.CreateClient();
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/runs/{runId}/agent-attempts")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/runs/{runId}/agent-attempts/{attemptId}/evidence")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync($"/api/runs/{runId}/agent-attempts/{attemptId}/evidence/artifact-window/stdout")).StatusCode);
    }

    [Fact]
    public async Task History_of_an_unknown_run_is_a_safe_not_found_and_malformed_paging_is_a_bad_request()
    {
        using var client = CreateAuthenticatedClient();
        var runId = await SeedRunAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/runs/{Guid.NewGuid()}/agent-attempts")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/runs/{runId}/agent-attempts?limit=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/runs/{runId}/agent-attempts?limit=-3")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/runs/{runId}/agent-attempts?beforeAttemptNumber=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/runs/{runId}/agent-attempts?beforeAttemptNumber=-1")).StatusCode);
    }

    [Fact]
    public async Task History_of_a_run_without_agent_attempts_is_an_empty_page_without_continuation()
    {
        using var client = CreateAuthenticatedClient();
        var runId = await SeedRunAsync();

        var page = await ReadAsync(await client.GetAsync($"/api/runs/{runId}/agent-attempts"));

        Assert.Empty(page.GetProperty("items").EnumerateArray());
        Assert.False(page.GetProperty("hasMore").GetBoolean());
        Assert.Equal(JsonValueKind.Null, page.GetProperty("nextBeforeAttemptNumber").ValueKind);
    }

    [Fact]
    public async Task History_lists_every_supported_role_and_provider_pair_newest_first_and_only_agent_attempts()
    {
        using var client = CreateAuthenticatedClient();
        var runId = await SeedRunAsync();
        var otherRunId = await SeedRunAsync();
        var kinds = new[]
        {
            (AgentRole.Planner, AgentProvider.Codex),
            (AgentRole.CriticalReviewer, AgentProvider.ClaudeCode),
            (AgentRole.Resolver, AgentProvider.Codex),
            (AgentRole.Implementer, AgentProvider.ClaudeCode),
            (AgentRole.CodeReviewer, AgentProvider.Codex),
            (AgentRole.Implementer, AgentProvider.ClaudeCode),
        };
        var ids = new List<Guid>();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            for (var index = 0; index < kinds.Length; index++)
            {
                var attempt = ClaimSupported(runId, index + 1, index);
                attempt.Fail(DateTimeOffset.UtcNow);
                dbContext.Attempts.Add(attempt);
                ids.Add(attempt.Id);
            }

            // A non-Agent attempt in the same run and an Agent attempt in another run never appear.
            var process = Attempt.Claim(Guid.NewGuid(), runId, 7, DateTimeOffset.UtcNow);
            process.Fail(DateTimeOffset.UtcNow);
            dbContext.Attempts.Add(process);
            var foreign = ClaimSupported(otherRunId, 1, 0);
            foreign.Fail(DateTimeOffset.UtcNow);
            dbContext.Attempts.Add(foreign);
            await dbContext.SaveChangesAsync();
        }

        var page = await ReadAsync(await client.GetAsync($"/api/runs/{runId}/agent-attempts"));

        var items = page.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(kinds.Length, items.Length);
        Assert.Equal(new[] { 6, 5, 4, 3, 2, 1 }, items.Select(item => item.GetProperty("attemptNumber").GetInt32()));
        Assert.All(items, item => Assert.True(item.GetProperty("identityValid").GetBoolean()));
        Assert.All(items, item => Assert.Equal("Failed", item.GetProperty("status").GetString()));
        for (var index = 0; index < kinds.Length; index++)
        {
            var item = items[kinds.Length - 1 - index];
            Assert.Equal(kinds[index].Item1.ToString(), item.GetProperty("role").GetString());
            Assert.Equal(kinds[index].Item2.ToString(), item.GetProperty("provider").GetString());
            Assert.Equal(ids[index], item.GetProperty("attemptId").GetGuid());
        }

        Assert.False(page.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task History_pages_a_run_with_more_than_sixteen_agent_attempts_without_gaps_duplicates_or_overshoot()
    {
        using var client = CreateAuthenticatedClient();
        var runId = await SeedRunAsync();
        const int total = 45;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            for (var number = 1; number <= total; number++)
            {
                var attempt = ClaimSupported(runId, number, number % 6);
                attempt.Fail(DateTimeOffset.UtcNow);
                dbContext.Attempts.Add(attempt);
            }

            await dbContext.SaveChangesAsync();
        }

        var seen = new List<int>();
        int? cursor = null;
        var pages = 0;
        JsonElement page;
        do
        {
            var url = $"/api/runs/{runId}/agent-attempts?limit=1000" + (cursor is { } c ? $"&beforeAttemptNumber={c}" : string.Empty);
            page = await ReadAsync(await client.GetAsync(url));
            var numbers = page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("attemptNumber").GetInt32()).ToArray();
            Assert.InRange(numbers.Length, 1, 20);
            seen.AddRange(numbers);
            pages++;
            var hasMore = page.GetProperty("hasMore").GetBoolean();
            if (hasMore)
            {
                cursor = page.GetProperty("nextBeforeAttemptNumber").GetInt32();
                Assert.Equal(numbers[^1], cursor);
            }
            else
            {
                Assert.Equal(JsonValueKind.Null, page.GetProperty("nextBeforeAttemptNumber").ValueKind);
                cursor = null;
            }
        }
        while (cursor is not null);

        Assert.Equal(3, pages);
        Assert.Equal(Enumerable.Range(1, total).Reverse().ToArray(), seen);
    }

    [Fact]
    public async Task History_default_page_is_bounded_and_the_before_cursor_boundary_is_exclusive()
    {
        using var client = CreateAuthenticatedClient();
        var runId = await SeedRunAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            for (var number = 1; number <= 12; number++)
            {
                var attempt = ClaimSupported(runId, number, 0);
                attempt.Fail(DateTimeOffset.UtcNow);
                dbContext.Attempts.Add(attempt);
            }

            await dbContext.SaveChangesAsync();
        }

        var first = await ReadAsync(await client.GetAsync($"/api/runs/{runId}/agent-attempts"));
        Assert.Equal(10, first.GetProperty("items").GetArrayLength());
        Assert.True(first.GetProperty("hasMore").GetBoolean());
        Assert.Equal(3, first.GetProperty("nextBeforeAttemptNumber").GetInt32());

        var second = await ReadAsync(await client.GetAsync($"/api/runs/{runId}/agent-attempts?beforeAttemptNumber=3"));
        Assert.Equal(new[] { 2, 1 }, second.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("attemptNumber").GetInt32()));
        Assert.False(second.GetProperty("hasMore").GetBoolean());

        var beyond = await ReadAsync(await client.GetAsync($"/api/runs/{runId}/agent-attempts?beforeAttemptNumber=1"));
        Assert.Empty(beyond.GetProperty("items").EnumerateArray());
        Assert.False(beyond.GetProperty("hasMore").GetBoolean());

        var exact = await ReadAsync(await client.GetAsync($"/api/runs/{runId}/agent-attempts?limit=2&beforeAttemptNumber=3"));
        Assert.Equal(2, exact.GetProperty("items").GetArrayLength());
        Assert.False(exact.GetProperty("hasMore").GetBoolean());
        Assert.Equal(JsonValueKind.Null, exact.GetProperty("nextBeforeAttemptNumber").ValueKind);
    }

    [Fact]
    public async Task A_failed_attempt_with_no_collaboration_message_is_inspectable_through_its_sealed_output()
    {
        using var client = CreateAuthenticatedClient();
        var runId = await SeedRunAsync();
        var attemptId = await SeedFailedAttemptAsync(runId, 1);
        await SealArtifactAsync(runId, attemptId, ArtifactPurpose.AgentContextManifest, "manifest body");
        await SealArtifactAsync(runId, attemptId, ArtifactPurpose.AgentStandardOutput, "stdout body");
        await SealArtifactAsync(runId, attemptId, ArtifactPurpose.AgentStandardError, "stderr body <b>markup</b>");
        await SealArtifactAsync(runId, attemptId, ArtifactPurpose.AgentFinalResponse, "final body");
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            Assert.Empty(dbContext.CollaborationMessages.Where(message => message.AttemptId == attemptId));
        }

        var evidence = await ReadAsync(await client.GetAsync($"/api/runs/{runId}/agent-attempts/{attemptId}/evidence"));

        Assert.True(evidence.GetProperty("identityValid").GetBoolean());
        Assert.Equal("Failed", evidence.GetProperty("attemptStatus").GetString());
        Assert.Equal("Planner", evidence.GetProperty("role").GetString());
        Assert.Equal("Codex", evidence.GetProperty("provider").GetString());
        var purposes = evidence.GetProperty("artifacts").EnumerateArray().Select(a => a.GetProperty("purpose").GetString()).ToArray();
        Assert.Equal(
            new[] { "AgentContextManifest", "AgentStandardOutput", "AgentStandardError", "AgentFinalResponse" }.OrderBy(p => p),
            purposes.OrderBy(p => p));

        foreach (var (purpose, expected) in new[]
                 {
                     (ArtifactPurpose.AgentContextManifest, "manifest body"),
                     (ArtifactPurpose.AgentStandardOutput, "stdout body"),
                     (ArtifactPurpose.AgentStandardError, "stderr body <b>markup</b>"),
                     (ArtifactPurpose.AgentFinalResponse, "final body"),
                 })
        {
            var window = await ReadAsync(await client.GetAsync(
                $"/api/runs/{runId}/agent-attempts/{attemptId}/evidence/artifact-window/{RouteFor(purpose)}"));
            Assert.Equal("Ok", window.GetProperty("status").GetString());
            Assert.Equal(expected, window.GetProperty("text").GetString());
            Assert.False(window.GetProperty("truncated").GetBoolean());
        }
    }

    [Fact]
    public async Task Evidence_and_window_of_unknown_foreign_and_non_agent_attempts_are_safe_not_found()
    {
        using var client = CreateAuthenticatedClient();
        var runId = await SeedRunAsync();
        var otherRunId = await SeedRunAsync();
        var foreignAttemptId = await SeedFailedAttemptAsync(otherRunId, 1);
        await SealArtifactAsync(otherRunId, foreignAttemptId, ArtifactPurpose.AgentStandardOutput, "foreign output");
        Guid processAttemptId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var process = Attempt.Claim(Guid.NewGuid(), runId, 1, DateTimeOffset.UtcNow);
            process.Fail(DateTimeOffset.UtcNow);
            dbContext.Attempts.Add(process);
            await dbContext.SaveChangesAsync();
            processAttemptId = process.Id;
        }

        foreach (var attemptId in new[] { Guid.NewGuid(), foreignAttemptId, processAttemptId })
        {
            var evidence = await client.GetAsync($"/api/runs/{runId}/agent-attempts/{attemptId}/evidence");
            var evidenceBody = await evidence.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.NotFound, evidence.StatusCode);
            Assert.Contains("agent_attempts.not_found", evidenceBody, StringComparison.Ordinal);
            AssertNoSensitiveDisclosure(evidenceBody);

            var window = await client.GetAsync($"/api/runs/{runId}/agent-attempts/{attemptId}/evidence/artifact-window/stdout");
            Assert.Equal(HttpStatusCode.NotFound, window.StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
            $"/api/runs/{Guid.NewGuid()}/agent-attempts/{foreignAttemptId}/evidence")).StatusCode);
    }

    [Fact]
    public async Task An_incoherent_attempt_is_listed_without_identity_and_discloses_no_evidence_or_content()
    {
        using var client = CreateAuthenticatedClient();
        var runId = await SeedRunAsync();
        var attemptId = await SeedFailedAttemptAsync(runId, 1);
        await SealArtifactAsync(runId, attemptId, ArtifactPurpose.AgentFinalResponse, "must not be served");
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            // A Codex planner cannot legitimately be a Claude Code planner.
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE attempts SET AgentProvider = {nameof(AgentProvider.ClaudeCode)} WHERE Id = {attemptId}");
        }

        var page = await ReadAsync(await client.GetAsync($"/api/runs/{runId}/agent-attempts"));
        var item = page.GetProperty("items")[0];
        Assert.False(item.GetProperty("identityValid").GetBoolean());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("role").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("provider").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("outcome").ValueKind);

        var evidence = await ReadAsync(await client.GetAsync($"/api/runs/{runId}/agent-attempts/{attemptId}/evidence"));
        Assert.False(evidence.GetProperty("identityValid").GetBoolean());
        Assert.Empty(evidence.GetProperty("artifacts").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("role").ValueKind);

        var window = await ReadAsync(await client.GetAsync(
            $"/api/runs/{runId}/agent-attempts/{attemptId}/evidence/artifact-window/final-response"));
        Assert.Equal("AttemptIdentityInvalid", window.GetProperty("status").GetString());
        Assert.Equal(string.Empty, window.GetProperty("text").GetString());
    }

    [Fact]
    public async Task A_response_contract_that_does_not_belong_to_the_role_is_incoherent()
    {
        using var client = CreateAuthenticatedClient();
        var runId = await SeedRunAsync();
        var attemptId = await SeedFailedAttemptAsync(runId, 1);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE attempts SET AgentResponseContract = {nameof(AgentResponseContract.ReviewCorrection)} WHERE Id = {attemptId}");
        }

        var evidence = await ReadAsync(await client.GetAsync($"/api/runs/{runId}/agent-attempts/{attemptId}/evidence"));

        Assert.False(evidence.GetProperty("identityValid").GetBoolean());
    }

    [Fact]
    public async Task Evidence_metadata_excludes_cross_run_and_process_purpose_artifact_rows()
    {
        using var client = CreateAuthenticatedClient();
        var runId = await SeedRunAsync();
        var otherRunId = await SeedRunAsync();
        var attemptId = await SeedFailedAttemptAsync(runId, 1);
        await SealArtifactAsync(runId, attemptId, ArtifactPurpose.AgentStandardOutput, "genuine output");
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            dbContext.Artifacts.Add(Artifact.Record(
                Guid.NewGuid(), otherRunId, attemptId, ArtifactPurpose.AgentStandardError, "text/plain",
                @"runs\x\attempts\y\stderr.sealed", "sha256:cross", 5, false, ArtifactCaptureOutcome.Captured,
                ArtifactSensitivity.RedactedBestEffort, ArtifactRetentionPolicy.RetainUntilRunDeleted, DateTimeOffset.UtcNow));
            dbContext.Artifacts.Add(Artifact.Record(
                Guid.NewGuid(), runId, attemptId, ArtifactPurpose.ProcessStandardOutput, "text/plain",
                @"runs\x\attempts\y\process.sealed", "sha256:process", 5, false, ArtifactCaptureOutcome.Captured,
                ArtifactSensitivity.RedactedBestEffort, ArtifactRetentionPolicy.RetainUntilRunDeleted, DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync();
        }

        var evidence = await ReadAsync(await client.GetAsync($"/api/runs/{runId}/agent-attempts/{attemptId}/evidence"));
        var purposes = evidence.GetProperty("artifacts").EnumerateArray().Select(a => a.GetProperty("purpose").GetString()).ToArray();
        Assert.Equal(new[] { "AgentStandardOutput" }, purposes);

        // The cross-run stderr row is never served, and a Process purpose has no route at all.
        var stderr = await ReadAsync(await client.GetAsync(
            $"/api/runs/{runId}/agent-attempts/{attemptId}/evidence/artifact-window/stderr"));
        Assert.Equal("ArtifactNotFound", stderr.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
            $"/api/runs/{runId}/agent-attempts/{attemptId}/evidence/artifact-window/process-stdout")).StatusCode);
    }

    [Fact]
    public async Task Window_reports_an_absent_row_missing_file_escaping_path_and_tampered_length_without_content()
    {
        using var client = CreateAuthenticatedClient();
        var runId = await SeedRunAsync();
        var attemptId = await SeedFailedAttemptAsync(runId, 1);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            dbContext.Artifacts.Add(Artifact.Record(
                Guid.NewGuid(), runId, attemptId, ArtifactPurpose.AgentStandardError, "text/plain",
                @"runs\r\attempts\a\stderr.sealed", "sha256:never-written", 42, false, ArtifactCaptureOutcome.Captured,
                ArtifactSensitivity.RedactedBestEffort, ArtifactRetentionPolicy.RetainUntilRunDeleted, DateTimeOffset.UtcNow));
            dbContext.Artifacts.Add(Artifact.Record(
                Guid.NewGuid(), runId, attemptId, ArtifactPurpose.AgentContextManifest, "text/plain",
                @"..\..\escape.sealed", "sha256:escape", 4, false, ArtifactCaptureOutcome.Captured,
                ArtifactSensitivity.RedactedBestEffort, ArtifactRetentionPolicy.RetainUntilRunDeleted, DateTimeOffset.UtcNow));
            var artifactStore = scope.ServiceProvider.GetRequiredService<IArtifactStore>();
            var partialPath = artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentStandardOutput);
            Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
            await File.WriteAllTextAsync(partialPath, "genuine sealed content", NoBomUtf8);
            var sealedFile = await artifactStore.SealAsync(runId, attemptId, ArtifactPurpose.AgentStandardOutput, CancellationToken.None);
            Assert.NotNull(sealedFile);
            dbContext.Artifacts.Add(Artifact.Record(
                Guid.NewGuid(), runId, attemptId, ArtifactPurpose.AgentStandardOutput, "text/plain",
                sealedFile!.RelativeStoragePath, sealedFile.ContentHash, sealedFile.ByteLength + 1, false,
                ArtifactCaptureOutcome.Captured, ArtifactSensitivity.RedactedBestEffort,
                ArtifactRetentionPolicy.RetainUntilRunDeleted, DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync();
        }

        async Task<JsonElement> WindowAsync(string route) => await ReadAsync(await client.GetAsync(
            $"/api/runs/{runId}/agent-attempts/{attemptId}/evidence/artifact-window/{route}"));

        Assert.Equal("ArtifactNotFound", (await WindowAsync("final-response")).GetProperty("status").GetString());
        Assert.Equal("Missing", (await WindowAsync("stderr")).GetProperty("status").GetString());
        Assert.Equal("Missing", (await WindowAsync("context-manifest")).GetProperty("status").GetString());
        var tampered = await WindowAsync("stdout");
        Assert.Equal("IntegrityMismatch", tampered.GetProperty("status").GetString());
        Assert.Equal(string.Empty, tampered.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Window_rejects_a_negative_offset_and_an_unrecognized_purpose()
    {
        using var client = CreateAuthenticatedClient();
        var runId = await SeedRunAsync();
        var attemptId = await SeedFailedAttemptAsync(runId, 1);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(
            $"/api/runs/{runId}/agent-attempts/{attemptId}/evidence/artifact-window/stdout?fromOffset=-1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
            $"/api/runs/{runId}/agent-attempts/{attemptId}/evidence/artifact-window/not-a-purpose")).StatusCode);
    }

    [Fact]
    public async Task Window_splits_a_utf8_codepoint_across_windows_and_reconstructs_the_full_text()
    {
        using var client = CreateAuthenticatedClient();
        var runId = await SeedRunAsync();
        var attemptId = await SeedFailedAttemptAsync(runId, 1);
        var content = new string('a', 61) + "\U0001F600" + new string('b', 20);
        await SealArtifactAsync(runId, attemptId, ArtifactPurpose.AgentStandardOutput, content);
        var reconstructed = new StringBuilder();
        long offset = 0;
        var windows = 0;

        while (windows < 10)
        {
            var window = await ReadAsync(await client.GetAsync(
                $"/api/runs/{runId}/agent-attempts/{attemptId}/evidence/artifact-window/stdout?fromOffset={offset}&maxBytes=64"));
            Assert.Equal("Ok", window.GetProperty("status").GetString());
            var next = window.GetProperty("nextOffset").GetInt64();
            var text = window.GetProperty("text").GetString()!;
            windows++;
            if (text.Length == 0)
            {
                break;
            }

            Assert.True(next > offset);
            reconstructed.Append(text);
            offset = next;
        }

        Assert.Equal(content, reconstructed.ToString());
        Assert.True(windows >= 2);
    }

    private static Attempt ClaimSupported(Guid runId, int number, int kind)
    {
        var id = Guid.NewGuid();
        var workspace = Guid.NewGuid();
        var checkpoint = Guid.NewGuid();
        var manifest = Guid.NewGuid();
        var timeout = TimeSpan.FromMinutes(10);
        var now = DateTimeOffset.UtcNow;
        return kind switch
        {
            0 => Attempt.ClaimAgent(id, runId, number, workspace, checkpoint, Fingerprint, manifest, timeout, 262144, 524288, now, number),
            1 => Attempt.ClaimAgentCriticalReview(id, runId, number, workspace, checkpoint, Fingerprint, manifest, timeout, 262144, 524288, now, number),
            2 => Attempt.ClaimAgentChallengeResolution(id, runId, number, workspace, checkpoint, Fingerprint, manifest, timeout, 262144, 524288, now, number),
            3 => Attempt.ClaimAgentImplementation(id, runId, number, workspace, checkpoint, Fingerprint, manifest, timeout, 262144, 524288, now, number),
            4 => Attempt.ClaimAgentCodeReview(id, runId, number, workspace, checkpoint, Fingerprint, manifest, timeout, 262144, 524288, now, number),
            _ => Attempt.ClaimAgentReviewCorrection(id, runId, number, workspace, checkpoint, Fingerprint, manifest, timeout, 262144, 524288, now, number),
        };
    }

    private async Task<Guid> SeedRunAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "Attempt history", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Attempt history run", now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync();
        return run.Id;
    }

    private async Task<Guid> SeedFailedAttemptAsync(Guid runId, int number)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var attempt = ClaimSupported(runId, number, 0);
        attempt.MarkAgentDispatched(DateTimeOffset.UtcNow);
        attempt.RecordAgentProviderSessionId(SessionSecret);
        attempt.Fail(DateTimeOffset.UtcNow);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync();
        return attempt.Id;
    }

    private async Task SealArtifactAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, string content)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var artifactStore = scope.ServiceProvider.GetRequiredService<IArtifactStore>();
        var partialPath = artifactStore.GetPartialPath(runId, attemptId, purpose);
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        await File.WriteAllTextAsync(partialPath, content, NoBomUtf8);
        var sealedFile = await artifactStore.SealAsync(runId, attemptId, purpose, CancellationToken.None);
        Assert.NotNull(sealedFile);
        dbContext.Artifacts.Add(Artifact.Record(
            Guid.NewGuid(), runId, attemptId, purpose, "text/plain",
            sealedFile!.RelativeStoragePath, sealedFile.ContentHash, sealedFile.ByteLength, false,
            ArtifactCaptureOutcome.Captured, ArtifactSensitivity.RedactedBestEffort,
            ArtifactRetentionPolicy.RetainUntilRunDeleted, DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync();
    }

    [Theory]
    [InlineData("Status")]
    [InlineData("AgentRole")]
    [InlineData("AgentProvider")]
    [InlineData("AgentResponseContract")]
    public async Task An_unrecognized_persisted_enum_string_fails_safely_on_all_three_routes(string column)
    {
        using var client = CreateAuthenticatedClient();
        var runId = await SeedRunAsync();
        var attemptId = await SeedFailedAttemptAsync(runId, 1);
        await SealArtifactAsync(runId, attemptId, ArtifactPurpose.AgentFinalResponse, "artifact content must not be served");
        var healthyId = await SeedFailedAttemptAsync(runId, 2);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var corrupt = "NotARealValue";
            _ = column switch
            {
                "Status" => await dbContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET Status = {corrupt} WHERE Id = {attemptId}"),
                "AgentRole" => await dbContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentRole = {corrupt} WHERE Id = {attemptId}"),
                "AgentProvider" => await dbContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentProvider = {corrupt} WHERE Id = {attemptId}"),
                _ => await dbContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentResponseContract = {corrupt} WHERE Id = {attemptId}"),
            };
        }

        var history = await client.GetAsync($"/api/runs/{runId}/agent-attempts");
        var evidence = await client.GetAsync($"/api/runs/{runId}/agent-attempts/{attemptId}/evidence");
        var window = await client.GetAsync($"/api/runs/{runId}/agent-attempts/{attemptId}/evidence/artifact-window/final-response");

        foreach (var response in new[] { history, evidence, window })
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("NotARealValue", body, StringComparison.Ordinal);
            Assert.DoesNotContain("artifact content must not be served", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
            AssertNoSensitiveDisclosure(body);
        }

        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        Assert.Equal(HttpStatusCode.OK, evidence.StatusCode);
        Assert.Equal(HttpStatusCode.OK, window.StatusCode);

        var items = JsonDocument.Parse(await history.Content.ReadAsStringAsync()).RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(new[] { 2, 1 }, items.Select(item => item.GetProperty("attemptNumber").GetInt32()));
        Assert.True(items[0].GetProperty("identityValid").GetBoolean());
        Assert.Equal(healthyId, items[0].GetProperty("attemptId").GetGuid());
        Assert.False(items[1].GetProperty("identityValid").GetBoolean());
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("role").ValueKind);
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("provider").ValueKind);
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("outcome").ValueKind);

        var evidenceRoot = JsonDocument.Parse(await evidence.Content.ReadAsStringAsync()).RootElement;
        Assert.False(evidenceRoot.GetProperty("identityValid").GetBoolean());
        Assert.Equal(1, evidenceRoot.GetProperty("attemptNumber").GetInt32());
        Assert.Empty(evidenceRoot.GetProperty("artifacts").EnumerateArray());

        var windowRoot = JsonDocument.Parse(await window.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("AttemptIdentityInvalid", windowRoot.GetProperty("status").GetString());
        Assert.Equal(string.Empty, windowRoot.GetProperty("text").GetString());
    }

    [Fact]
    public async Task A_verified_window_returns_captured_text_exactly_while_its_envelope_adds_no_sensitive_field()
    {
        using var client = CreateAuthenticatedClient();
        var runId = await SeedRunAsync();
        var attemptId = await SeedFailedAttemptAsync(runId, 1);
        // Clearly fictitious content that looks like the very things the envelope must never add.
        var captured = $"prompt: summarise the repo\nsessionId={SessionSecret}\nC:\repos\fictitious\file.cs\nsha256:00ff.sealed\nException: fake";
        await SealArtifactAsync(runId, attemptId, ArtifactPurpose.AgentStandardOutput, captured);

        var response = await client.GetAsync(
            $"/api/runs/{runId}/agent-attempts/{attemptId}/evidence/artifact-window/stdout?maxBytes=65536");
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Ok", root.GetProperty("status").GetString());
        Assert.Equal(captured, root.GetProperty("text").GetString());
        Assert.Equal(
            new[] { "nextOffset", "status", "text", "totalLengthSoFar", "truncated" },
            root.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
        AssertNoSensitiveDisclosure(string.Concat(
            root.EnumerateObject().Where(property => property.Name != "text").Select(property => $"\"{property.Name}\":{property.Value.GetRawText()},")));

        // The metadata routes for the same attempt still disclose none of it.
        var evidenceBody = await (await client.GetAsync($"/api/runs/{runId}/agent-attempts/{attemptId}/evidence")).Content.ReadAsStringAsync();
        AssertNoSensitiveDisclosure(evidenceBody);
        var historyBody = await (await client.GetAsync($"/api/runs/{runId}/agent-attempts")).Content.ReadAsStringAsync();
        AssertNoSensitiveDisclosure(historyBody);
    }
}
