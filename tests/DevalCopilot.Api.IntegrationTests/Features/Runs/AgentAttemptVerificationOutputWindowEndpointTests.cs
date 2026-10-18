using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSwag.Generation;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// ADR-0052: the protected, bodyless sealed window of one stream of one recorded verification member, against the real host, file-backed
/// SQLite and the real artifact store over a class-owned disposable root, observed by a recording wrapper. Authentication, the fixed 404,
/// strict 400 validation without clamping, every status, byte caps and cursors, UTF-8 text, the OpenAPI document and the absence of any
/// artifact read for a refused member, stream or request are proved at the wire.
/// </summary>
public sealed class AgentAttemptVerificationOutputWindowEndpointTests : AgentAttemptVerificationApiTestBase, IClassFixture<ApiWebApplicationFactory>, IDisposable
{
    private static readonly string[] RootMembers =
        ["artifact", "attemptId", "executionId", "nextOffset", "runId", "status", "stream", "text", "totalLength"];

    private static readonly string[] StreamMembers = ["artifactId", "byteLength", "captureOutcome", "status", "truncated"];

    private readonly string _root;
    private readonly CountingStore _store;
    private readonly WebApplicationFactory<Program> _derived;

    public AgentAttemptVerificationOutputWindowEndpointTests(ApiWebApplicationFactory factory)
        : this(factory, Path.Combine(Path.GetTempPath(), $"devalcopilot-verification-window-api-{Guid.NewGuid():N}"))
    {
    }

    private AgentAttemptVerificationOutputWindowEndpointTests(ApiWebApplicationFactory factory, string root)
        : base(Derive(factory, root, out var store))
    {
        _root = root;
        _store = store;
        _derived = Host;
        Directory.CreateDirectory(_root);
    }

    private sealed class CountingStore(IArtifactStore inner) : IArtifactStore
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

    private static WebApplicationFactory<Program> Derive(ApiWebApplicationFactory factory, string root, out CountingStore store)
    {
        var counting = new CountingStore(new FilesystemArtifactStore(root));
        store = counting;
        return factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IArtifactStore>();
            services.AddSingleton<IArtifactStore>(counting);
        }));
    }

    public void Dispose()
    {
        _derived.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static string Route(Guid runId, Guid attemptId, Guid executionId, string stream, string? query = null) =>
        $"/api/runs/{runId}/agent-attempts/{attemptId}/verification-evidence/{executionId}/artifact-window/{stream}"
        + (query is null ? string.Empty : "?" + query);

    private async Task<string> WriteAsync(Guid executionId, string stream, byte[] bytes)
    {
        var relative = Path.Combine("verification", executionId.ToString("N"), stream + ".sealed");
        var full = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, bytes);
        return relative;
    }

    private async Task<(string Path, VerificationOutputArtifact Row)> RecordAsync(
        Guid executionId, VerificationOutputPurpose purpose, byte[] bytes, bool? truncated = false,
        VerificationOutputCaptureOutcome capture = VerificationOutputCaptureOutcome.CapturedWithKnownTruncation)
    {
        var relative = await WriteAsync(executionId, purpose == VerificationOutputPurpose.StandardOutput ? "stdout" : "stderr", bytes);
        return (relative, await AddOutputAsync(executionId, purpose, relative, ContentHash(bytes), bytes.Length, truncated, capture));
    }

    private int StoreCalls
    {
        get
        {
            lock (_store.Paths)
            {
                return _store.Paths.Count;
            }
        }
    }

    [Fact]
    public async Task The_route_requires_authentication_and_accepts_only_a_bodyless_get()
    {
        var lineage = await SeedAsync(false, false);
        var execution = lineage.Members[0].ExecutionId;
        await RecordAsync(execution, VerificationOutputPurpose.StandardOutput, "abc"u8.ToArray());
        var route = Route(lineage.RunId, lineage.AttemptId, execution, "stdout");

        using var anonymous = Host.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);

        using var wrong = Host.CreateClient();
        wrong.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-launch-secret");
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync(route)).StatusCode);

        using var queryOnly = Host.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await queryOnly.GetAsync($"{route}?access_token={ApiWebApplicationFactory.ValidSecret}")).StatusCode);

        using var client = AuthenticatedClient();
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync(route, null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsync(route, null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(route)).StatusCode);
        Assert.Equal(0, StoreCalls);
    }

    [Fact]
    public async Task Ok_carries_only_the_documented_members_the_recorded_descriptor_and_the_canonical_stream()
    {
        var lineage = await SeedAsync(true, true, false);
        var execution = lineage.Members[0].ExecutionId;
        var bytes = Encoding.UTF8.GetBytes("first\nsecond\n");
        var stdout = await RecordAsync(execution, VerificationOutputPurpose.StandardOutput, bytes, truncated: true);

        var response = await GetAsync(Route(lineage.RunId, lineage.AttemptId, execution, "stdout", "fromOffset=6&maxBytes=64"));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(RootMembers, Names(response.Body));
        Assert.Equal("Ok", response.Body.GetProperty("status").GetString());
        Assert.Equal("stdout", response.Body.GetProperty("stream").GetString());
        Assert.Equal(lineage.RunId, response.Body.GetProperty("runId").GetGuid());
        Assert.Equal(lineage.AttemptId, response.Body.GetProperty("attemptId").GetGuid());
        Assert.Equal(execution, response.Body.GetProperty("executionId").GetGuid());
        Assert.Equal("second\n", response.Body.GetProperty("text").GetString());
        Assert.Equal(bytes.Length, response.Body.GetProperty("nextOffset").GetInt64());
        Assert.Equal(bytes.Length, response.Body.GetProperty("totalLength").GetInt64());
        var artifact = response.Body.GetProperty("artifact");
        Assert.Equal(StreamMembers, Names(artifact));
        Assert.Equal("Recorded", artifact.GetProperty("status").GetString());
        Assert.Equal(stdout.Row.Id, artifact.GetProperty("artifactId").GetGuid());
        Assert.Equal(bytes.Length, artifact.GetProperty("byteLength").GetInt64());
        Assert.Equal("CapturedWithKnownTruncation", artifact.GetProperty("captureOutcome").GetString());
        Assert.Equal(JsonValueKind.True, artifact.GetProperty("truncated").ValueKind);
        foreach (var forbidden in new[] { "sha256", "hash", "\\\\", ".sealed", "verification/" })
        {
            Assert.DoesNotContain(forbidden, response.Text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-int.MaxValue)]
    [InlineData(-int.MaxValue + 1)]
    [InlineData(int.MaxValue)]
    public async Task The_stream_of_a_failed_member_with_an_int32_boundary_exit_code_is_served_on_the_wire(int exitCode)
    {
        var lineage = await SeedAsync(true, true, false);
        var execution = lineage.Members[0].ExecutionId;
        await ExecuteAsync($"UPDATE verification_executions SET ExitCode = {exitCode} WHERE Id = {execution}");
        var bytes = Encoding.UTF8.GetBytes("failure detail\n");
        var stdout = await RecordAsync(execution, VerificationOutputPurpose.StandardOutput, bytes);
        var before = StoreCalls;

        var response = await GetAsync(Route(lineage.RunId, lineage.AttemptId, execution, "stdout"));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("Ok", response.Body.GetProperty("status").GetString());
        Assert.Equal("failure detail\n", response.Body.GetProperty("text").GetString());
        Assert.Equal(bytes.Length, response.Body.GetProperty("nextOffset").GetInt64());
        Assert.Equal(bytes.Length, response.Body.GetProperty("totalLength").GetInt64());
        Assert.Equal(stdout.Row.Id, response.Body.GetProperty("artifact").GetProperty("artifactId").GetGuid());
        Assert.Equal(before + 1, StoreCalls);
    }

    [Fact]
    public async Task Without_parameters_the_window_is_sixteen_kibibytes_and_explicit_caps_are_honoured()
    {
        var lineage = await SeedAsync(false, false);
        var execution = lineage.Members[0].ExecutionId;
        await RecordAsync(execution, VerificationOutputPurpose.StandardError, Encoding.UTF8.GetBytes(new string('z', 70_000)));
        string RouteFor(string? query) => Route(lineage.RunId, lineage.AttemptId, execution, "stderr", query);

        var defaults = await GetAsync(RouteFor(null));
        Assert.Equal("stderr", defaults.Body.GetProperty("stream").GetString());
        Assert.Equal(16384, defaults.Body.GetProperty("nextOffset").GetInt64());
        Assert.Equal(16384, defaults.Body.GetProperty("text").GetString()!.Length);
        Assert.Equal(70_000, defaults.Body.GetProperty("totalLength").GetInt64());

        Assert.Equal(64, (await GetAsync(RouteFor("maxBytes=64"))).Body.GetProperty("nextOffset").GetInt64());
        Assert.Equal(65536, (await GetAsync(RouteFor("maxBytes=65536"))).Body.GetProperty("nextOffset").GetInt64());
        Assert.Equal(70_000, (await GetAsync(RouteFor("fromOffset=70000"))).Body.GetProperty("nextOffset").GetInt64());
        Assert.Equal(16384, (await GetAsync(RouteFor("fromOffset=&maxBytes="))).Body.GetProperty("nextOffset").GetInt64());
    }

    [Fact]
    public async Task Multibyte_text_survives_multiple_windows_through_the_wire()
    {
        var lineage = await SeedAsync(false, false);
        var execution = lineage.Members[0].ExecutionId;
        var original = string.Concat(Enumerable.Repeat("é ✓ 𝄞 日本語 – ", 30));
        await RecordAsync(execution, VerificationOutputPurpose.StandardOutput, Encoding.UTF8.GetBytes(original));

        var builder = new StringBuilder();
        long offset = 0;
        while (true)
        {
            var response = await GetAsync(Route(lineage.RunId, lineage.AttemptId, execution, "stdout", $"fromOffset={offset}&maxBytes=64"));
            Assert.Equal("Ok", response.Body.GetProperty("status").GetString());
            builder.Append(response.Body.GetProperty("text").GetString());
            var next = response.Body.GetProperty("nextOffset").GetInt64();
            if (next == response.Body.GetProperty("totalLength").GetInt64())
            {
                break;
            }

            Assert.True(next > offset);
            offset = next;
        }

        Assert.Equal(original, builder.ToString());
    }

    [Fact]
    public async Task Every_non_ok_status_is_an_explicit_200_with_no_artifact_empty_text_the_offset_unchanged_and_no_length()
    {
        var lineage = await SeedAsync(true, true, true, false);
        var missing = lineage.Members[0].ExecutionId;
        var tampered = lineage.Members[1].ExecutionId;
        var broken = lineage.Members[2].ExecutionId;
        var recordedMissing = await RecordAsync(missing, VerificationOutputPurpose.StandardOutput, "gone soon"u8.ToArray());
        File.Delete(Path.Combine(_root, recordedMissing.Path));
        var recordedTampered = await RecordAsync(tampered, VerificationOutputPurpose.StandardOutput, "original text"u8.ToArray());
        await File.WriteAllBytesAsync(Path.Combine(_root, recordedTampered.Path), Encoding.UTF8.GetBytes(Secret + " tampered with extra length"));
        var recordedBroken = await RecordAsync(broken, VerificationOutputPurpose.StandardOutput, "metadata will break"u8.ToArray());
        await ExecuteAsync($"UPDATE verification_output_artifacts SET CaptureOutcome = {"Weird" + Secret} WHERE Id = {recordedBroken.Row.Id}");

        var cases = new[]
        {
            (missing, "stdout", "Missing"),
            (tampered, "stdout", "IntegrityMismatch"),
            (broken, "stdout", "Unavailable"),
            (missing, "stderr", "NotRecorded"),
        };
        foreach (var (execution, stream, status) in cases)
        {
            var response = await GetAsync(Route(lineage.RunId, lineage.AttemptId, execution, stream, "fromOffset=3"));

            Assert.Equal(HttpStatusCode.OK, response.Status);
            Assert.Equal(RootMembers, Names(response.Body));
            Assert.Equal(status, response.Body.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, response.Body.GetProperty("artifact").ValueKind);
            Assert.Equal(string.Empty, response.Body.GetProperty("text").GetString());
            Assert.Equal(3, response.Body.GetProperty("nextOffset").GetInt64());
            Assert.Equal(0, response.Body.GetProperty("totalLength").GetInt64());
            Assert.DoesNotContain(Secret, response.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_verified_empty_stream_is_ok_and_an_offset_past_the_recorded_length_is_a_validation_refusal_without_a_read()
    {
        var lineage = await SeedAsync(false, false);
        var execution = lineage.Members[0].ExecutionId;
        await RecordAsync(execution, VerificationOutputPurpose.StandardOutput, []);
        await RecordAsync(execution, VerificationOutputPurpose.StandardError, "12345"u8.ToArray());

        var empty = await GetAsync(Route(lineage.RunId, lineage.AttemptId, execution, "stdout"));
        Assert.Equal("Ok", empty.Body.GetProperty("status").GetString());
        Assert.Equal(string.Empty, empty.Body.GetProperty("text").GetString());
        Assert.Equal(0, empty.Body.GetProperty("totalLength").GetInt64());

        var callsBefore = StoreCalls;
        var past = await GetAsync(Route(lineage.RunId, lineage.AttemptId, execution, "stderr", "fromOffset=6"));
        Assert.Equal(HttpStatusCode.BadRequest, past.Status);
        Assert.Equal("urn:devalente:problem:validation", past.Body.GetProperty("type").GetString());
        var error = Assert.Single(past.Body.GetProperty("errors").EnumerateArray());
        Assert.Equal("validation.invalid_offset", error.GetProperty("code").GetString());
        Assert.Equal("#/fromOffset", error.GetProperty("pointer").GetString());
        Assert.Equal(callsBefore, StoreCalls);

        var atEnd = await GetAsync(Route(lineage.RunId, lineage.AttemptId, execution, "stderr", "fromOffset=5"));
        Assert.Equal("Ok", atEnd.Body.GetProperty("status").GetString());
        Assert.Equal(string.Empty, atEnd.Body.GetProperty("text").GetString());
        Assert.Equal(5, atEnd.Body.GetProperty("nextOffset").GetInt64());
    }

    [Fact]
    public async Task An_unknown_run_foreign_attempt_nonmember_execution_and_unsupported_stream_are_the_same_fixed_404_and_read_no_artifact()
    {
        var lineage = await SeedAsync(false, false);
        var other = await SeedAsync(false, false);
        var execution = lineage.Members[0].ExecutionId;
        await RecordAsync(execution, VerificationOutputPurpose.StandardOutput, "abc"u8.ToArray());
        await RecordAsync(other.Members[0].ExecutionId, VerificationOutputPurpose.StandardOutput, "foreign"u8.ToArray());
        var unknownId = Guid.NewGuid();
        var callsBefore = StoreCalls;

        var routes = new[]
        {
            Route(Guid.NewGuid(), lineage.AttemptId, execution, "stdout"),
            Route(lineage.RunId, unknownId, execution, "stdout"),
            Route(lineage.RunId, other.AttemptId, execution, "stdout"),
            Route(lineage.RunId, lineage.AttemptId, other.Members[0].ExecutionId, "stdout"),
            Route(lineage.RunId, lineage.AttemptId, unknownId, "stdout"),
            Route(lineage.RunId, lineage.AttemptId, execution, "stdin"),
            Route(lineage.RunId, lineage.AttemptId, execution, "STDOUT"),
            Route(lineage.RunId, lineage.AttemptId, execution, "context-manifest"),
            Route(lineage.RunId, lineage.AttemptId, execution, $"x-{Secret}"),
        };
        var responses = new List<(HttpStatusCode Status, JsonElement Body, string Text, string? MediaType)>();
        foreach (var route in routes)
        {
            responses.Add(await GetAsync(route));
        }

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.Status);
            Assert.Equal("urn:devalente:problem:not-found", response.Body.GetProperty("type").GetString());
            var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
            Assert.Equal("agent_attempt_verification.not_found", error.GetProperty("code").GetString());
            var body = Normalize(response.Text);
            foreach (var value in new[] { unknownId.ToString(), lineage.AttemptId.ToString(), other.AttemptId.ToString(), execution.ToString(), Secret, "stdin" })
            {
                Assert.DoesNotContain(value, body, StringComparison.OrdinalIgnoreCase);
            }
        }

        Assert.Single(responses.Select(response => Normalize(response.Text)).Distinct());
        Assert.Equal(callsBefore, StoreCalls);
    }

    [Fact]
    public async Task A_member_or_stream_substitution_reads_only_the_requested_row_and_a_refused_one_reads_nothing()
    {
        var lineage = await SeedAsync(true, true, false);
        var first = lineage.Members[0].ExecutionId;
        var second = lineage.Members[1].ExecutionId;
        var firstOut = await RecordAsync(first, VerificationOutputPurpose.StandardOutput, "first stdout"u8.ToArray());
        var firstErr = await RecordAsync(first, VerificationOutputPurpose.StandardError, "first stderr"u8.ToArray());
        var secondOut = await RecordAsync(second, VerificationOutputPurpose.StandardOutput, "second stdout"u8.ToArray());

        string Text(JsonElement body) => body.GetProperty("text").GetString()!;
        Assert.Equal("first stdout", Text((await GetAsync(Route(lineage.RunId, lineage.AttemptId, first, "stdout"))).Body));
        Assert.Equal("first stderr", Text((await GetAsync(Route(lineage.RunId, lineage.AttemptId, first, "stderr"))).Body));
        Assert.Equal("second stdout", Text((await GetAsync(Route(lineage.RunId, lineage.AttemptId, second, "stdout"))).Body));

        lock (_store.Paths)
        {
            Assert.Equal([firstOut.Path, firstErr.Path, secondOut.Path], _store.Paths.TakeLast(3));
        }

        // A refused request (second member's stderr was never recorded; a foreign attempt) opens no file at all.
        var before = StoreCalls;
        Assert.Equal("NotRecorded", (await GetAsync(Route(lineage.RunId, lineage.AttemptId, second, "stderr"))).Body.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(Route(lineage.RunId, Guid.NewGuid(), first, "stdout"))).Status);
        Assert.Equal(before, StoreCalls);
    }

    [Fact]
    public async Task A_defect_in_any_member_makes_the_window_unavailable_without_a_read()
    {
        var lineage = await SeedAsync(true, true, false);
        var first = lineage.Members[0].ExecutionId;
        await RecordAsync(first, VerificationOutputPurpose.StandardOutput, "abc"u8.ToArray());
        await ExecuteAsync($"UPDATE verification_executions SET Status = 'TimedOut' WHERE Id = {lineage.Members[1].ExecutionId}");
        var before = StoreCalls;

        var response = await GetAsync(Route(lineage.RunId, lineage.AttemptId, first, "stdout", "fromOffset=1"));

        Assert.Equal("Unavailable", response.Body.GetProperty("status").GetString());
        Assert.Equal(1, response.Body.GetProperty("nextOffset").GetInt64());
        Assert.Equal(before, StoreCalls);
    }

    public static TheoryData<string, string, string> RejectedValues() => new()
    {
        { "fromOffset=-1", "validation.invalid_offset", "#/fromOffset" },
        { "fromOffset=abc", "validation.invalid_offset", "#/fromOffset" },
        { "fromOffset=1.5", "validation.invalid_offset", "#/fromOffset" },
        { "fromOffset=1e1", "validation.invalid_offset", "#/fromOffset" },
        { "fromOffset=0x10", "validation.invalid_offset", "#/fromOffset" },
        { "fromOffset=%2B5", "validation.invalid_offset", "#/fromOffset" },
        { "fromOffset=05", "validation.invalid_offset", "#/fromOffset" },
        { "fromOffset=%205", "validation.invalid_offset", "#/fromOffset" },
        { "fromOffset=5%20", "validation.invalid_offset", "#/fromOffset" },
        { "fromOffset=%EF%BC%95", "validation.invalid_offset", "#/fromOffset" },
        { "fromOffset=1&fromOffset=2", "validation.invalid_offset", "#/fromOffset" },
        { "fromOffset=9007199254740992", "validation.invalid_offset", "#/fromOffset" },
        { "fromOffset=9223372036854775807", "validation.invalid_offset", "#/fromOffset" },
        { "fromOffset=99999999999999999999", "validation.invalid_offset", "#/fromOffset" },
        { "maxBytes=63", "validation.invalid_max_bytes", "#/maxBytes" },
        { "maxBytes=65537", "validation.invalid_max_bytes", "#/maxBytes" },
        { "maxBytes=0", "validation.invalid_max_bytes", "#/maxBytes" },
        { "maxBytes=-1", "validation.invalid_max_bytes", "#/maxBytes" },
        { "maxBytes=1.5", "validation.invalid_max_bytes", "#/maxBytes" },
        { "maxBytes=abc", "validation.invalid_max_bytes", "#/maxBytes" },
        { "maxBytes=064", "validation.invalid_max_bytes", "#/maxBytes" },
        { "maxBytes=%2B64", "validation.invalid_max_bytes", "#/maxBytes" },
        { "maxBytes=64&maxBytes=128", "validation.invalid_max_bytes", "#/maxBytes" },
        { "maxBytes=2147483648", "validation.invalid_max_bytes", "#/maxBytes" },
        { "maxBytes=99999999999999999999", "validation.invalid_max_bytes", "#/maxBytes" },
    };

    [Theory]
    [MemberData(nameof(RejectedValues))]
    public async Task An_invalid_offset_or_cap_is_refused_with_the_shared_validation_problem_never_clamped_and_never_echoed(
        string query, string code, string pointer)
    {
        var lineage = await SeedAsync(false, false);
        var execution = lineage.Members[0].ExecutionId;
        await RecordAsync(execution, VerificationOutputPurpose.StandardOutput, "abcdef"u8.ToArray());
        var before = StoreCalls;

        var response = await GetAsync(Route(lineage.RunId, lineage.AttemptId, execution, "stdout", query));

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("application/problem+json", response.MediaType);
        Assert.Equal("urn:devalente:problem:validation", response.Body.GetProperty("type").GetString());
        Assert.Equal(400, response.Body.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(response.Body.GetProperty("traceId").GetString()));
        var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
        Assert.Equal(code, error.GetProperty("code").GetString());
        Assert.Equal(pointer, error.GetProperty("pointer").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("detail").GetString()));
        Assert.DoesNotContain("ValidationProblemDetails", response.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("is not valid", response.Text, StringComparison.Ordinal);
        Assert.Equal(before, StoreCalls);
    }

    [Fact]
    public async Task Both_an_invalid_offset_and_an_invalid_cap_report_both_pointers()
    {
        var lineage = await SeedAsync(false, false);

        var response = await GetAsync(Route(lineage.RunId, lineage.AttemptId, lineage.Members[0].ExecutionId, "stdout", "fromOffset=-1&maxBytes=1"));

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal(
            ["#/fromOffset", "#/maxBytes"],
            response.Body.GetProperty("errors").EnumerateArray().Select(error => error.GetProperty("pointer").GetString()!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Reading_a_window_writes_nothing()
    {
        var lineage = await SeedAsync(true, true, false);
        var execution = lineage.Members[0].ExecutionId;
        await RecordAsync(execution, VerificationOutputPurpose.StandardOutput, "abc"u8.ToArray());
        var files = Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();

        async Task<string> SnapshotAsync() => await WithDbAsync(async db =>
        {
            var outputs = await db.VerificationOutputArtifacts.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => row.Id + "|" + row.RelativeStoragePath + "|" + row.ContentHash).ToListAsync();
            return string.Join(";", outputs) + "#" + await db.VerificationExecutions.CountAsync() + "#" + await db.Events.CountAsync()
                + "#" + await db.Artifacts.CountAsync();
        });

        var before = await SnapshotAsync();
        await GetAsync(Route(lineage.RunId, lineage.AttemptId, execution, "stdout"));
        await GetAsync(Route(lineage.RunId, lineage.AttemptId, execution, "stderr"));

        Assert.Equal(before, await SnapshotAsync());
        Assert.Equal(files, Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_actual_openapi_document_declares_the_bodyless_route_its_bounded_query_and_the_closed_response()
    {
        var generator = Host.Services.GetRequiredService<IOpenApiDocumentGenerator>();
        var document = JsonNode.Parse((await generator.GenerateAsync("v1")).ToJson())!;

        var operation = document["paths"]![
            "/api/runs/{runId}/agent-attempts/{attemptId}/verification-evidence/{executionId}/artifact-window/{stream}"]!["get"]!;
        var parameters = operation["parameters"]!.AsArray().Select(p => (p!["name"]!.GetValue<string>(), p["in"]!.GetValue<string>()));
        Assert.Equal(
            [("attemptId", "path"), ("executionId", "path"), ("fromOffset", "query"), ("maxBytes", "query"), ("runId", "path"), ("stream", "path")],
            parameters.Order());
        Assert.Null(operation["requestBody"]);
        var responses = operation["responses"]!.AsObject();
        Assert.Equal(["200", "400", "404"], responses.Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.Contains("GetAgentAttemptVerificationOutputWindowResponse", responses["200"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("ApiProblemDetails", responses["400"]!["content"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("ApiProblemDetails", responses["404"]!["content"]!.ToJsonString(), StringComparison.Ordinal);

        var offset = operation["parameters"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "fromOffset")!;
        Assert.Contains("int64", offset.ToJsonString(), StringComparison.Ordinal);
        var window = PropertiesOf(document, "GetAgentAttemptVerificationOutputWindowResponse");
        Assert.Equal(RootMembers, window.Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.Contains("integer", window["nextOffset"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("integer", window["totalLength"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("string", window["text"]!.ToJsonString(), StringComparison.Ordinal);
    }
}
