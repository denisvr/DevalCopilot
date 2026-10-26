using System.Net;
using System.Text;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Projects;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Projects.ProjectVerificationInspection;

/// <summary>
/// ADR-0069: the protected, bodyless sealed window of one stream of one recorded project verification, against the real host, file-backed
/// SQLite and the real artifact store over a class-owned disposable root observed by a counting wrapper: every status, byte caps and
/// cursors, UTF-8 text through the wire, an offset past the recorded length, refusal before any artifact read and the absence of any
/// write. No Agent attempt or local execution sequence exists for any execution read here.
/// </summary>
public sealed class ProjectVerificationInspectionWindowEndpointTests(ApiWebApplicationFactory factory)
    : ProjectVerificationInspectionApiTestBase(factory), IClassFixture<ApiWebApplicationFactory>
{
    private static readonly string[] RootMembers =
        ["artifact", "executionId", "nextOffset", "projectId", "status", "stream", "text", "totalLength"];

    private static readonly string[] StreamMembers = ["artifactId", "byteLength", "captureOutcome", "status", "truncated"];

    [Fact]
    public async Task Ok_carries_only_the_documented_members_the_recorded_descriptor_and_the_canonical_stream()
    {
        var (projectId, executions) = await SeedAsync(true);
        var execution = executions[0];
        var stdout = await RecordAsync(execution.Id, VerificationOutputPurpose.StandardOutput, "standard output"u8.ToArray(), true);
        await RecordAsync(execution.Id, VerificationOutputPurpose.StandardError, "standard error"u8.ToArray());

        var response = await GetAsync(Window(projectId, execution.Id, "stdout", "fromOffset=9&maxBytes=64"));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("application/json", response.MediaType);
        Assert.Equal(RootMembers, Names(response.Body));
        Assert.Equal((projectId, execution.Id, "stdout", "Ok"), (
            response.Body.GetProperty("projectId").GetGuid(), response.Body.GetProperty("executionId").GetGuid(),
            response.Body.GetProperty("stream").GetString(), response.Body.GetProperty("status").GetString()));
        Assert.Equal("output", response.Body.GetProperty("text").GetString());
        Assert.Equal((15L, 15L), (response.Body.GetProperty("nextOffset").GetInt64(), response.Body.GetProperty("totalLength").GetInt64()));
        var artifact = response.Body.GetProperty("artifact");
        Assert.Equal(StreamMembers, Names(artifact));
        Assert.Equal(("Recorded", stdout.Row.Id, 15L, "CapturedWithKnownTruncation", true), (
            artifact.GetProperty("status").GetString(), artifact.GetProperty("artifactId").GetGuid(), artifact.GetProperty("byteLength").GetInt64(),
            artifact.GetProperty("captureOutcome").GetString(), artifact.GetProperty("truncated").GetBoolean()));
        Assert.DoesNotContain(".sealed", response.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("sha256:", response.Text, StringComparison.Ordinal);
        Assert.Equal(1, StoreCalls);

        var stderr = await GetAsync(Window(projectId, execution.Id, "stderr"));
        Assert.Equal(("stderr", "standard error"), (stderr.Body.GetProperty("stream").GetString(), stderr.Body.GetProperty("text").GetString()));
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-int.MaxValue)]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(int.MaxValue - 1)]
    [InlineData(int.MaxValue)]
    public async Task The_stream_of_an_execution_with_an_int32_boundary_exit_code_is_served_on_the_wire(int exitCode)
    {
        var (projectId, executions) = await SeedAsync(true);
        await RecordAsync(executions[0].Id, VerificationOutputPurpose.StandardOutput, "boundary"u8.ToArray());
        await ExecuteAsync($"UPDATE verification_executions SET ExitCode = {exitCode} WHERE Id = {executions[0].Id}");

        var response = await GetAsync(Window(projectId, executions[0].Id, "stdout"));

        Assert.Equal("Ok", response.Body.GetProperty("status").GetString());
        Assert.Equal("boundary", response.Body.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Without_parameters_the_window_is_sixteen_kibibytes_and_explicit_caps_are_honoured()
    {
        var (projectId, executions) = await SeedAsync(false);
        var bytes = Encoding.UTF8.GetBytes(new string('x', 40_000));
        await RecordAsync(executions[0].Id, VerificationOutputPurpose.StandardOutput, bytes);

        var byDefault = await GetAsync(Window(projectId, executions[0].Id, "stdout"));
        var small = await GetAsync(Window(projectId, executions[0].Id, "stdout", "maxBytes=64"));
        var large = await GetAsync(Window(projectId, executions[0].Id, "stdout", "maxBytes=65536"));

        Assert.Equal((16384, 16384L), (byDefault.Body.GetProperty("text").GetString()!.Length, byDefault.Body.GetProperty("nextOffset").GetInt64()));
        Assert.Equal((64, 64L), (small.Body.GetProperty("text").GetString()!.Length, small.Body.GetProperty("nextOffset").GetInt64()));
        Assert.Equal((40_000, 40_000L), (large.Body.GetProperty("text").GetString()!.Length, large.Body.GetProperty("nextOffset").GetInt64()));
    }

    [Fact]
    public async Task Multibyte_text_survives_multiple_windows_through_the_wire()
    {
        var (projectId, executions) = await SeedAsync(false);
        var original = string.Concat(Enumerable.Repeat("é ✓ 𝄞 日本語 – ", 30)) + "end";
        var bytes = Encoding.UTF8.GetBytes(original);
        await RecordAsync(executions[0].Id, VerificationOutputPurpose.StandardError, bytes);

        var builder = new StringBuilder();
        long offset = 0;
        while (true)
        {
            var window = await GetAsync(Window(projectId, executions[0].Id, "stderr", $"fromOffset={offset}&maxBytes=64"));
            Assert.Equal("Ok", window.Body.GetProperty("status").GetString());
            builder.Append(window.Body.GetProperty("text").GetString());
            var next = window.Body.GetProperty("nextOffset").GetInt64();
            if (next == window.Body.GetProperty("totalLength").GetInt64())
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
        var (projectId, executions) = await SeedAsync(false, false, false, false);
        var missing = await RecordAsync(executions[0].Id, VerificationOutputPurpose.StandardOutput, "will be removed"u8.ToArray());
        File.Delete(Path.Combine(Root, missing.Path));
        var tampered = await RecordAsync(executions[1].Id, VerificationOutputPurpose.StandardOutput, "original content"u8.ToArray());
        await File.WriteAllBytesAsync(Path.Combine(Root, tampered.Path), "Original content"u8.ToArray());
        var damaged = await RecordAsync(executions[2].Id, VerificationOutputPurpose.StandardOutput, "content"u8.ToArray());
        await ExecuteAsync($"UPDATE verification_output_artifacts SET ContentHash = 'sha256:bad' WHERE Id = {damaged.Row.Id}");

        var cases = new[]
        {
            (Window(projectId, executions[0].Id, "stdout", "fromOffset=4"), "Missing", 4L),
            (Window(projectId, executions[1].Id, "stdout", "fromOffset=2"), "IntegrityMismatch", 2L),
            (Window(projectId, executions[2].Id, "stdout", "fromOffset=1"), "Unavailable", 1L),
            (Window(projectId, executions[3].Id, "stdout", "fromOffset=7"), "NotRecorded", 7L),
        };
        foreach (var (route, status, offset) in cases)
        {
            var response = await GetAsync(route);

            Assert.Equal(HttpStatusCode.OK, response.Status);
            Assert.Equal(RootMembers, Names(response.Body));
            Assert.Equal(status, response.Body.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, response.Body.GetProperty("artifact").ValueKind);
            Assert.Equal(string.Empty, response.Body.GetProperty("text").GetString());
            Assert.Equal((offset, 0L), (response.Body.GetProperty("nextOffset").GetInt64(), response.Body.GetProperty("totalLength").GetInt64()));
            Assert.DoesNotContain("original content", response.Text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_verified_empty_stream_is_ok_and_an_offset_past_the_recorded_length_is_a_validation_refusal_without_a_read()
    {
        var (projectId, executions) = await SeedAsync(false);
        await RecordAsync(executions[0].Id, VerificationOutputPurpose.StandardOutput, []);
        await RecordAsync(executions[0].Id, VerificationOutputPurpose.StandardError, "abc"u8.ToArray());

        var empty = await GetAsync(Window(projectId, executions[0].Id, "stdout"));
        var before = StoreCalls;
        var past = await GetAsync(Window(projectId, executions[0].Id, "stderr", "fromOffset=4"));
        var afterPast = StoreCalls;
        var atEnd = await GetAsync(Window(projectId, executions[0].Id, "stderr", "fromOffset=3"));

        Assert.Equal(("Ok", string.Empty, 0L), (empty.Body.GetProperty("status").GetString(), empty.Body.GetProperty("text").GetString(), empty.Body.GetProperty("totalLength").GetInt64()));
        Assert.Equal(HttpStatusCode.BadRequest, past.Status);
        Assert.Equal("validation.invalid_offset", Assert.Single(past.Body.GetProperty("errors").EnumerateArray()).GetProperty("code").GetString());
        Assert.Equal(before, afterPast);
        Assert.Equal(("Ok", 3L), (atEnd.Body.GetProperty("status").GetString(), atEnd.Body.GetProperty("nextOffset").GetInt64()));
    }

    [Fact]
    public async Task An_execution_with_incoherent_recorded_facts_is_unavailable_before_any_artifact_is_read()
    {
        var (projectId, executions) = await SeedAsync(false);
        await RecordAsync(executions[0].Id, VerificationOutputPurpose.StandardOutput, "content"u8.ToArray());
        await ExecuteAsync($"UPDATE verification_executions SET Status = 'Weird' WHERE Id = {executions[0].Id}");

        var response = await GetAsync(Window(projectId, executions[0].Id, "stdout", "fromOffset=2"));

        Assert.Equal("Unavailable", response.Body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, response.Body.GetProperty("artifact").ValueKind);
        Assert.Equal(2L, response.Body.GetProperty("nextOffset").GetInt64());
        Assert.Equal(0, StoreCalls);
    }

    [Fact]
    public async Task A_window_reads_only_the_requested_stream_file_and_writes_nothing()
    {
        var (projectId, executions) = await SeedAsync(true, false);
        await RecordAsync(executions[0].Id, VerificationOutputPurpose.StandardOutput, "abc"u8.ToArray());
        await RecordAsync(executions[0].Id, VerificationOutputPurpose.StandardError, "def"u8.ToArray());
        var files = Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
        var before = await SnapshotAsync();

        await GetAsync(Window(projectId, executions[0].Id, "stdout"));
        var afterFirst = StoreCalls;
        await GetAsync(Window(projectId, executions[0].Id, "stderr"));

        Assert.Equal((1, 2), (afterFirst, StoreCalls));
        Assert.Equal(before, await SnapshotAsync());
        Assert.Equal(files, Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
    }
}
