using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevalCopilot.Api.IntegrationTests.Features.Projects.ProjectVerificationInspection;

/// <summary>Feature-owned seeding and reading shared by the ADR-0069 endpoint test classes. Every call writes to the database of the
/// host it was given, with rows from the Domain factories; only deliberately damaged storage is written as SQL. The artifact store of
/// the derived host is the real one over a class-owned disposable root, observed by a counting wrapper, so a refused request provably
/// reads no sealed file.</summary>
public abstract class ProjectVerificationInspectionApiTestBase : IDisposable
{
    protected const string Secret = "SECRET-SENTINEL-69c4";

    protected static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero).AddTicks(1234567);

    protected static readonly string Fingerprint = new('a', 64);

    protected static readonly string Sha = "0123456789abcdef0123456789abcdef01234567";

    private int _workspaceNumber;
    private readonly string _root;
    private readonly CountingStore _store;
    private readonly WebApplicationFactory<Program> _derived;

    protected ProjectVerificationInspectionApiTestBase(ApiWebApplicationFactory factory)
    {
        _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-project-verification-api-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _store = new CountingStore(new FilesystemArtifactStore(_root));
        _derived = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IArtifactStore>();
            services.AddSingleton<IArtifactStore>(_store);
        }));
    }

    protected WebApplicationFactory<Program> Host => _derived;

    protected string Root => _root;

    protected int StoreCalls
    {
        get
        {
            lock (_store.Paths)
            {
                return _store.Paths.Count;
            }
        }
    }

    protected sealed class CountingStore(IArtifactStore inner) : IArtifactStore
    {
        public List<string> Paths { get; } = [];

        public Task<SealedReadWindow> VerifyAndReadSealedAsync(
            string relativeStoragePath, long expectedByteLength, string expectedContentHash, long fromOffset, int maxBytes,
            CancellationToken cancellationToken)
        {
            lock (Paths)
            {
                Paths.Add(relativeStoragePath);
            }

            return inner.VerifyAndReadSealedAsync(
                relativeStoragePath, expectedByteLength, expectedContentHash, fromOffset, maxBytes, cancellationToken);
        }

        public string GetPartialPath(Guid runId, Guid attemptId, ArtifactPurpose purpose) => inner.GetPartialPath(runId, attemptId, purpose);

        public string GetSealedRelativePath(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
            inner.GetSealedRelativePath(runId, attemptId, purpose);

        public Task<SealedOutputFile?> SealAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken) =>
            inner.SealAsync(runId, attemptId, purpose, cancellationToken);

        public bool HasSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => inner.HasSealedFile(runId, attemptId, purpose);

        public bool HasPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => inner.HasPartialFile(runId, attemptId, purpose);

        public Task<SealedOutputFile?> DescribeSealedFileAsync(
            Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken) =>
            inner.DescribeSealedFileAsync(runId, attemptId, purpose, cancellationToken);

        public void DeleteOrphanedPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
            inner.DeleteOrphanedPartialFile(runId, attemptId, purpose);

        public void DeleteOrphanedSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
            inner.DeleteOrphanedSealedFile(runId, attemptId, purpose);

        public Task<PartialReadWindow> ReadPartialAsync(
            Guid runId, Guid attemptId, ArtifactPurpose purpose, long fromOffset, int maxBytes, CancellationToken cancellationToken) =>
            inner.ReadPartialAsync(runId, attemptId, purpose, fromOffset, maxBytes, cancellationToken);
    }

    public void Dispose()
    {
        _derived.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

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

    protected Task<Guid> SeedProjectAsync() => WithDbAsync(async db =>
    {
        var project = Project.Register(Guid.NewGuid(), "Verification", $@"C:\repos\{Guid.NewGuid():N}", Now);
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project.Id;
    });

    /// <summary>One project, workspace, checkpoint and recipe with executions numbered 1 to <paramref name="shapes"/>'s length: a failed
    /// execution (exit code 3) for <c>true</c> and a passed one for <c>false</c>. No run, Agent attempt or local sequence is created.</summary>
    protected async Task<(Guid ProjectId, IReadOnlyList<ApiSeededExecution> Executions)> SeedAsync(params bool[] shapes)
    {
        var projectId = await SeedProjectAsync();
        var executions = new List<ApiSeededExecution>();
        await WithDbAsync(async db =>
        {
            var workspace = GitWorkspace.Prepare(
                Guid.NewGuid(), projectId, Interlocked.Increment(ref _workspaceNumber), $@"C:\workspaces\{Guid.NewGuid():N}", "branch", Sha, "main", Now);
            db.GitWorkspaces.Add(workspace);
            var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, Sha, Fingerprint, []);
            db.GitCheckpoints.Add(checkpoint);
            var command = VerificationCommand.Configure(
                Guid.NewGuid(), projectId, 1, "Unit tests", @"C:\tools\verify.exe", ["--secret-argument"], 60, true, Now);
            db.VerificationCommands.Add(command);
            await db.SaveChangesAsync();
            for (var index = 0; index < shapes.Length; index++)
            {
                var number = index + 1;
                var execution = VerificationExecution.Claim(Guid.NewGuid(), projectId, number, workspace, checkpoint, command, Now.AddSeconds(number));
                execution.MarkDispatched(Now.AddSeconds(number).AddTicks(10_000_001));
                execution.Complete(
                    VerificationExecutionOutcome.Exited, shapes[index] ? 3 : 0, checkpoint.FingerprintSha256, Now.AddSeconds(number).AddTicks(30_000_004));
                db.VerificationExecutions.Add(execution);
                executions.Add(new ApiSeededExecution(execution.Id, number, command.Id, workspace.Id, checkpoint.Id));
            }

            await db.SaveChangesAsync();
            return 0;
        });
        return (projectId, executions);
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

    protected async Task<(string Path, VerificationOutputArtifact Row)> RecordAsync(
        Guid executionId, VerificationOutputPurpose purpose, byte[] bytes, bool? truncated = false,
        VerificationOutputCaptureOutcome capture = VerificationOutputCaptureOutcome.CapturedWithKnownTruncation)
    {
        var relative = Path.Combine(
            "verification", executionId.ToString("N"), (purpose == VerificationOutputPurpose.StandardOutput ? "stdout" : "stderr") + ".sealed");
        var full = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, bytes);
        return (relative, await AddOutputAsync(executionId, purpose, relative, ContentHash(bytes), bytes.Length, truncated, capture));
    }

    protected async Task<(HttpStatusCode Status, JsonElement Body, string Text, string? MediaType)> GetAsync(string route)
    {
        using var client = AuthenticatedClient();
        var response = await client.GetAsync(route);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone(), text,
            response.Content.Headers.ContentType?.MediaType);
    }

    protected static string History(Guid projectId, string? query = null) =>
        $"/api/projects/{projectId}/verification-history" + (query is null ? string.Empty : "?" + query);

    protected static string Detail(Guid projectId, Guid executionId, string? query = null) =>
        $"/api/projects/{projectId}/verification-history/{executionId}" + (query is null ? string.Empty : "?" + query);

    protected static string Window(Guid projectId, Guid executionId, string stream, string? query = null) =>
        $"/api/projects/{projectId}/verification-history/{executionId}/artifact-window/{stream}" + (query is null ? string.Empty : "?" + query);

    protected static string[] Names(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    protected static string Normalize(string text)
    {
        var body = JsonNode.Parse(text)!.AsObject();
        body.Remove("traceId");
        body.Remove("instance");
        return body.ToJsonString();
    }

    protected static string Time(DateTimeOffset value) => value.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    protected static JsonObject PropertiesOf(JsonNode document, string name)
    {
        var schema = document["components"]!["schemas"]![name]!;
        return (schema["properties"] ?? schema["allOf"]!.AsArray().First(node => node!["properties"] is not null)!["properties"])!.AsObject();
    }

    protected async Task<string> SnapshotAsync() => await WithDbAsync(async db =>
    {
        var outputs = await db.VerificationOutputArtifacts.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => row.Id + "|" + row.RelativeStoragePath + "|" + row.ContentHash).ToListAsync();
        var executions = await db.VerificationExecutions.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => row.Id + "|" + row.Status + "|" + row.ExitCode + "|" + row.CompletionFingerprintSha256).ToListAsync();
        return string.Join(";", outputs) + "#" + string.Join(";", executions) + "#" + await db.Projects.CountAsync() + "#"
            + await db.Events.CountAsync() + "#" + await db.Artifacts.CountAsync() + "#" + await db.Attempts.CountAsync();
    });
}
