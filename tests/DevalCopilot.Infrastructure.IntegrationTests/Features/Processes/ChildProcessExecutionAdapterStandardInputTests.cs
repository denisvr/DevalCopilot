using System.Text;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Infrastructure.Features.Processes;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Processes;

/// <summary>
/// Exercises the bounded standard-input support added to <see cref="ChildProcessExecutionAdapter"/>
/// against the same deterministic <c>ProcessExecutionFixture</c> test double used by
/// <see cref="ChildProcessExecutionAdapterTests"/> — a real child process, never a shell, never a
/// real Codex/npm/network binary. The fixture's <c>echo-stdin</c> and <c>sleep-then-echo-stdin</c>
/// modes exist solely to support this file.
/// </summary>
public sealed class ChildProcessExecutionAdapterStandardInputTests : IDisposable
{
    private static readonly string FixtureExecutablePath = Path.Combine(AppContext.BaseDirectory, "ProcessExecutionFixture.exe");

    private readonly ChildProcessExecutionAdapter _adapter = new();
    private readonly string _approvedRoot;

    public ChildProcessExecutionAdapterStandardInputTests()
    {
        _approvedRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-process-stdin-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_approvedRoot);

        Assert.True(File.Exists(FixtureExecutablePath), $"Expected the fixture executable at '{FixtureExecutablePath}'.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_approvedRoot))
        {
            Directory.Delete(_approvedRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Exact_stdin_bytes_including_non_ascii_utf8_content_are_transmitted_to_the_child_process_unchanged()
    {
        var payload = Encoding.UTF8.GetBytes("plain ascii, then non-ascii: café,日本語, emoji 🎉, and a final line.");
        var request = CreateRequest(["echo-stdin"], payload);

        var result = await _adapter.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(ProcessExecutionOutcome.Exited, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(Encoding.UTF8.GetString(payload), result.StandardOutput);
    }

    [Fact]
    public async Task No_stdin_behaves_identically_to_every_caller_before_stdin_support_existed()
    {
        // "exit-code" never touches standard input at all: this proves the adapter's whole
        // pipeline (including the now-added, no-op stdin-write task folded into Task.WhenAll)
        // completes exactly as it did before StandardInput existed, for a caller that never sets it.
        var request = CreateRequest(["exit-code", "0"], standardInput: null);

        var result = await _adapter.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(ProcessExecutionOutcome.Exited, result.Outcome);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task Empty_but_non_null_stdin_is_a_distinct_real_redirected_stream_from_null_stdin()
    {
        var request = CreateRequest(["echo-stdin"], standardInput: []);

        var result = await _adapter.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(ProcessExecutionOutcome.Exited, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardOutput);
    }

    [Fact]
    public async Task Oversized_stdin_is_rejected_before_any_process_starts()
    {
        var markerPath = Path.Combine(_approvedRoot, "marker.txt");
        var oversizedPayload = new byte[ProcessExecutionRequest.MaxStandardInputBytes + 1];
        var request = CreateRequest(["append-marker", markerPath], oversizedPayload);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _adapter.ExecuteAsync(request, CancellationToken.None));

        Assert.False(File.Exists(markerPath), "The child process must never launch for oversized stdin.");
    }

    [Fact]
    public async Task Stdin_exactly_at_the_maximum_allowed_size_is_accepted()
    {
        var payload = new byte[ProcessExecutionRequest.MaxStandardInputBytes];
        Array.Fill(payload, (byte)'a');
        var request = CreateRequest(["echo-stdin"], payload);

        var result = await _adapter.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(ProcessExecutionOutcome.Exited, result.Outcome);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task Shell_metacharacters_in_stdin_reach_the_child_as_literal_bytes_never_interpreted_by_a_shell()
    {
        var payload = Encoding.UTF8.GetBytes("; rm -rf / && echo pwned; $(whoami); `id`; | cat /etc/passwd");
        var request = CreateRequest(["echo-stdin"], payload);

        var result = await _adapter.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(ProcessExecutionOutcome.Exited, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(Encoding.UTF8.GetString(payload), result.StandardOutput);
    }

    [Fact]
    public async Task Cancelling_after_a_small_stdin_write_completes_still_kills_the_process_and_reports_cancelled()
    {
        var request = CreateRequest(
            ["sleep-ms", "20000"], Encoding.UTF8.GetBytes("small payload"), timeout: TimeSpan.FromSeconds(30));
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var result = await _adapter.ExecuteAsync(request, cancellationSource.Token);

        Assert.Equal(ProcessExecutionOutcome.Cancelled, result.Outcome);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public async Task Cancelling_while_a_large_stdin_write_is_still_in_flight_kills_the_process_and_completes_without_hanging()
    {
        // Sized well beyond the OS pipe's own kernel buffer and comfortably under
        // MaxStandardInputBytes, so the adapter's write genuinely blocks waiting for the fixture
        // to drain it — the fixture instead sleeps first, so the write is still in flight when
        // cancellation fires.
        var payload = new byte[512 * 1024];
        Array.Fill(payload, (byte)'a');
        var request = CreateRequest(["sleep-then-echo-stdin", "20000"], payload, timeout: TimeSpan.FromSeconds(30));
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var result = await _adapter.ExecuteAsync(request, cancellationSource.Token);

        Assert.Equal(ProcessExecutionOutcome.Cancelled, result.Outcome);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public async Task A_timeout_while_a_large_stdin_write_is_still_in_flight_kills_the_process_and_completes_without_hanging()
    {
        var payload = new byte[512 * 1024];
        Array.Fill(payload, (byte)'a');
        var request = CreateRequest(["sleep-then-echo-stdin", "20000"], payload, timeout: TimeSpan.FromMilliseconds(300));

        var result = await _adapter.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(ProcessExecutionOutcome.TimedOut, result.Outcome);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public void Stdin_content_never_appears_in_the_requests_ToString_representation()
    {
        const string Sentinel = "SENTINEL-STDIN-CONTENT-MUST-NEVER-LEAK-9f2c8b";
        var request = CreateRequest(["echo-stdin"], Encoding.UTF8.GetBytes(Sentinel));

        var text = request.ToString();

        Assert.DoesNotContain(Sentinel, text);
        Assert.Contains("HasStandardInput = True", text);
    }

    [Fact]
    public async Task Stdin_content_never_appears_in_an_exception_message_from_an_unrelated_failed_validation()
    {
        const string Sentinel = "SENTINEL-STDIN-CONTENT-MUST-NEVER-LEAK-EXC-4d1a90";
        var oversizedArguments = Enumerable.Range(0, ProcessExecutionRequest.MaxArgumentCount + 1).Select(i => i.ToString()).ToArray();
        var request = CreateRequest(["echo-args", .. oversizedArguments], Encoding.UTF8.GetBytes(Sentinel));

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _adapter.ExecuteAsync(request, CancellationToken.None));

        Assert.DoesNotContain(Sentinel, exception.Message);
        Assert.DoesNotContain(Sentinel, exception.ToString());
    }

    [Fact]
    public async Task Stdin_content_never_appears_in_the_exception_message_from_the_oversized_stdin_validation_itself()
    {
        const string Sentinel = "SENTINEL-STDIN-OVERSIZED-4b7e21";
        var oversizedPayload = new byte[ProcessExecutionRequest.MaxStandardInputBytes + 1];
        Encoding.UTF8.GetBytes(Sentinel).CopyTo(oversizedPayload, 0);
        var request = CreateRequest(["exit-code", "0"], oversizedPayload);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _adapter.ExecuteAsync(request, CancellationToken.None));

        Assert.DoesNotContain(Sentinel, exception.Message);
        Assert.DoesNotContain(Sentinel, exception.ToString());
    }

    private ProcessExecutionRequest CreateRequest(IReadOnlyList<string> arguments, byte[]? standardInput, TimeSpan? timeout = null) =>
        new()
        {
            ExecutablePath = FixtureExecutablePath,
            Arguments = arguments,
            WorkingDirectory = _approvedRoot,
            ApprovedRoot = _approvedRoot,
            Timeout = timeout ?? TimeSpan.FromSeconds(10),
            StandardInput = standardInput,
        };
}
