using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>Feature-owned seeding and reading shared by the two ADR-0052 endpoint test classes. It adds no shared fixture state: every
/// call writes to the database of the host it was given, with rows from the Domain factories; only deliberately damaged storage is
/// written as SQL.</summary>
public abstract class AgentAttemptVerificationApiTestBase(WebApplicationFactory<Program> factory)
{
    protected const string Secret = "SECRET-SENTINEL-52c3";

    protected static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    protected static readonly string Fingerprint = new('a', 64);

    protected static readonly string Sha = "0123456789abcdef0123456789abcdef01234567";

    private static int _projectCounter;

    protected WebApplicationFactory<Program> Host { get; } = factory;

    protected HttpClient AuthenticatedClient()
    {
        var client = Host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    protected async Task<T> WithDbAsync<T>(Func<DevalCopilotDbContext, Task<T>> action)
    {
        using var scope = Host.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>());
    }

    protected async Task ExecuteAsync(FormattableString sql) => await WithDbAsync(async db =>
    {
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
        return await db.Database.ExecuteSqlInterpolatedAsync(sql);
    });

    protected static string ContentHash(byte[] bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));

    protected Task<(Guid ProjectId, Guid RunId)> SeedRunAsync() => WithDbAsync(async db =>
    {
        var project = Project.Register(Guid.NewGuid(), "Verification", $@"C:\repos\{Guid.NewGuid():N}", Now);
        db.Projects.Add(project);
        var run = Run.RecordClassifiedIntent(
            Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Objective", Now);
        db.Runs.Add(run);
        await db.SaveChangesAsync();
        return (project.Id, run.Id);
    });

    /// <summary>One workspace, checkpoint, recipe and terminal execution per entry (true: Failed, exit code 1; false: Passed, exit
    /// code 0) and the CodeReviewer attempt that recorded them in order.</summary>
    protected async Task<SeededVerificationLineage> SeedAsync(bool diagnosis, params bool[] failed)
    {
        var (projectId, runId) = await SeedRunAsync();
        return await WithDbAsync(async db =>
        {
            var workspace = GitWorkspace.Prepare(Guid.NewGuid(), projectId, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", Sha, "main", Now);
            db.GitWorkspaces.Add(workspace);
            var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, Sha, Fingerprint, []);
            db.GitCheckpoints.Add(checkpoint);
            await db.SaveChangesAsync();

            var members = new List<(Guid, Guid)>();
            var executions = new List<(VerificationCommand Command, VerificationExecution Execution)>();
            for (var index = 0; index < failed.Length; index++)
            {
                var number = Interlocked.Increment(ref _projectCounter);
                var command = VerificationCommand.Configure(
                    Guid.NewGuid(), projectId, index + 1, $"Command {index + 1}", @"C:\tools\verify.exe", ["--secret-argument"], 60, true, Now);
                db.VerificationCommands.Add(command);
                await db.SaveChangesAsync();
                var execution = VerificationExecution.Claim(Guid.NewGuid(), projectId, index + 1, workspace, checkpoint, command, Now.AddSeconds(number));
                execution.MarkDispatched(Now.AddSeconds(number + 1));
                execution.Complete(VerificationExecutionOutcome.Exited, failed[index] ? 1 : 0, checkpoint.FingerprintSha256, Now.AddSeconds(number + 2));
                db.VerificationExecutions.Add(execution);
                await db.SaveChangesAsync();
                executions.Add((command, execution));
                members.Add((command.Id, execution.Id));
            }

            var timeout = TimeSpan.FromMinutes(10);
            var attempt = diagnosis
                ? Attempt.ClaimAgentVerificationDiagnosis(
                    Guid.NewGuid(), runId, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), timeout, 1024, 2048, Now, null, null, 1)
                : Attempt.ClaimAgentCodeReview(
                    Guid.NewGuid(), runId, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), timeout, 1024, 2048, Now, 1);
            db.Attempts.Add(attempt);
            for (var index = 0; index < executions.Count; index++)
            {
                var (command, execution) = executions[index];
                db.AttemptVerificationEvidence.Add(diagnosis
                    ? AttemptVerificationEvidence.RecordDiagnosisSnapshot(Guid.NewGuid(), attempt.Id, command.Id, execution.Id, index, new string('c', 64))
                    : AttemptVerificationEvidence.Record(Guid.NewGuid(), attempt.Id, command.Id, execution.Id, index));
            }

            await db.SaveChangesAsync();
            return new SeededVerificationLineage(projectId, runId, attempt.Id, workspace.Id, checkpoint.Id, members);
        });
    }

    protected Task<VerificationOutputArtifact> AddOutputAsync(
        Guid executionId,
        VerificationOutputPurpose purpose,
        string relativePath,
        string contentHash,
        long byteLength,
        bool? truncated = false,
        VerificationOutputCaptureOutcome capture = VerificationOutputCaptureOutcome.CapturedWithKnownTruncation) => WithDbAsync(async db =>
    {
        var output = VerificationOutputArtifact.Record(
            Guid.NewGuid(), executionId, purpose, relativePath, contentHash, byteLength, truncated, capture, Now);
        db.VerificationOutputArtifacts.Add(output);
        await db.SaveChangesAsync();
        return output;
    });

    protected async Task<(HttpStatusCode Status, JsonElement Body, string Text, string? MediaType)> GetAsync(string route)
    {
        using var client = AuthenticatedClient();
        var response = await client.GetAsync(route);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone(), text,
            response.Content.Headers.ContentType?.MediaType);
    }

    protected static string[] Names(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    protected static string Normalize(string text)
    {
        var body = JsonNode.Parse(text)!.AsObject();
        body.Remove("traceId");
        body.Remove("instance");
        return body.ToJsonString();
    }

    protected static JsonObject PropertiesOf(JsonNode document, string name)
    {
        var schema = document["components"]!["schemas"]![name]!;
        return (schema["properties"] ?? schema["allOf"]!.AsArray().First(node => node!["properties"] is not null)!["properties"])!.AsObject();
    }
}
