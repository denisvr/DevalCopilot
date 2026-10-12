using System.ComponentModel;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Projects.Queries.GetProjectGitHubOrigin;
using DevalCopilot.Infrastructure.Features.Projects;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Projects;

/// <summary>
/// The repository-local GitHub origin observer (ADR-0039) against a recording process double: the exact argv, environment,
/// approved root and bounds, the closed handling of every output shape, the fixed 15 second budget, launch failures,
/// cancellation and fault propagation. The real Git boundary (scope, hostile values, ignored configuration) is proved in
/// <c>GitHubOriginAdapterRealGitTests</c>.
/// </summary>
public sealed class GitHubOriginAdapterTests
{
    private const string GitPath = @"C:\safe\git.exe";
    private const string Root = @"C:\repos\origin-fixture";
    private const string OwnShape = "false\n.git\n.git\nC:/repos/origin-fixture\n";

    private sealed class RecordingProcessAdapter(Func<int, ProcessExecutionRequest, CancellationToken, Task<ProcessExecutionResult>> run)
        : IProcessExecutionAdapter
    {
        public List<ProcessExecutionRequest> Requests { get; } = [];

        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return run(Requests.Count, request, cancellationToken);
        }
    }

    private sealed class FakeScope(Func<bool>? intact = null) : IGitHubOriginScope
    {
        public bool Disposed { get; private set; }

        public bool IsIntact() => intact?.Invoke() ?? true;

        public void Dispose() => Disposed = true;
    }

    private sealed class SteppingTimeProvider : TimeProvider
    {
        public long Ticks { get; set; }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Ticks;
    }

    private static ProcessExecutionResult Result(
        string stdout, int exitCode = 0, bool stdoutTruncated = false, bool stderrTruncated = false,
        ProcessExecutionOutcome outcome = ProcessExecutionOutcome.Exited) => new()
        {
            Outcome = outcome,
            ExitCode = outcome == ProcessExecutionOutcome.Exited ? exitCode : null,
            StandardOutput = stdout,
            StandardOutputTruncated = stdoutTruncated,
            StandardError = string.Empty,
            StandardErrorTruncated = stderrTruncated,
            Duration = TimeSpan.FromMilliseconds(5),
        };

    private static Task<ProcessExecutionResult> Done(ProcessExecutionResult result) => Task.FromResult(result);

    private static Task<GitHubOriginObservation> ObserveAsync(
        RecordingProcessAdapter process, TimeProvider? timeProvider = null, CancellationToken cancellationToken = default,
        FakeScope? scope = null) =>
        new GitHubOriginAdapter(process, timeProvider ?? TimeProvider.System, _ => scope ?? new FakeScope())
            .ObserveAsync(GitPath, new RepositoryRootCandidate(Root), cancellationToken);

    private static RecordingProcessAdapter Script(string shape, ProcessExecutionResult config) =>
        new((call, _, _) => Done(call == 1 ? Result(shape) : config));

    private static Task<GitHubOriginObservation> ObserveConfigAsync(ProcessExecutionResult config) =>
        ObserveAsync(Script(OwnShape, config));

    [Fact]
    public async Task Exactly_two_fixed_invocations_with_the_literal_argv_isolated_environment_root_and_bounds()
    {
        var process = Script(OwnShape, Result("https://github.com/acme/widgets.git\0"));

        var observation = await ObserveAsync(process, new SteppingTimeProvider());

        Assert.Equal(GitHubOriginStatus.Available, observation.Status);
        Assert.Equal(2, process.Requests.Count);

        string[] hardening = ["--no-pager", "-c", "core.fsmonitor=false", "-c", "core.untrackedCache=false", "-c", "protocol.allow=never"];
        Assert.Equal(
            [.. hardening, "rev-parse", "--is-bare-repository", "--git-dir", "--git-common-dir", "--show-toplevel"],
            process.Requests[0].Arguments);
        Assert.Equal(
            [.. hardening, "config", "--file", Root + @"\.git\config", "--no-includes", "--null", "--get-all", "remote.origin.url"],
            process.Requests[1].Arguments);

        var expectedEnvironment = new Dictionary<string, string>
        {
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GIT_OPTIONAL_LOCKS"] = "0",
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_CONFIG_GLOBAL"] = "NUL",
        };
        foreach (var request in process.Requests)
        {
            Assert.Equal(GitPath, request.ExecutablePath);
            Assert.Equal(Root, request.WorkingDirectory);
            Assert.Equal(Root, request.ApprovedRoot);
            Assert.Equal(8 * 1024, request.MaxBytesPerStream);
            Assert.Equal(16 * 1024, request.MaxTotalCapturedBytes);
            Assert.Null(request.StandardOutputSinkPath);
            Assert.Null(request.StandardErrorSinkPath);
            Assert.Null(request.StandardInput);
            Assert.Equal(expectedEnvironment, request.EnvironmentVariables.ToDictionary(pair => pair.Key, pair => pair.Value));
        }

        Assert.Equal(TimeSpan.FromSeconds(15), process.Requests[0].Timeout);
    }

    [Fact]
    public async Task An_unplain_administrative_scope_is_unavailable_without_starting_any_process()
    {
        var process = Script(OwnShape, Result("https://github.com/acme/widgets\0"));

        var observation = await new GitHubOriginAdapter(process, TimeProvider.System, _ => null)
            .ObserveAsync(GitPath, new RepositoryRootCandidate(Root), CancellationToken.None);

        Assert.Equal(GitHubOriginObservation.Unavailable, observation);
        Assert.Empty(process.Requests);
    }

    [Fact]
    public async Task A_scope_that_is_no_longer_intact_after_the_configuration_read_discards_the_answer()
    {
        var process = Script(OwnShape, Result("https://github.com/acme/widgets\0"));

        var observation = await ObserveAsync(process, scope: new FakeScope(() => false));

        Assert.Equal(GitHubOriginObservation.Unavailable, observation);
        Assert.Equal(2, process.Requests.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task The_scope_is_held_for_both_invocations_and_released_after_every_outcome(int faultAt)
    {
        var scope = new FakeScope();
        var disposedAtCall = new List<bool>();
        var process = new RecordingProcessAdapter((call, _, _) =>
        {
            disposedAtCall.Add(scope.Disposed);
            return faultAt == call ? throw new InvalidOperationException("boom") : Done(call == 1 ? Result(OwnShape) : Result("", 1));
        });

        try
        {
            await ObserveAsync(process, scope: scope);
        }
        catch (InvalidOperationException)
        {
            Assert.NotEqual(0, faultAt);
        }

        Assert.All(disposedAtCall, disposed => Assert.False(disposed));
        Assert.True(scope.Disposed);
    }

    [Fact]
    public async Task The_second_invocation_receives_only_the_remaining_part_of_the_one_fifteen_second_budget()
    {
        var clock = new SteppingTimeProvider();
        var process = new RecordingProcessAdapter((call, _, _) =>
        {
            clock.Ticks += TimeSpan.FromSeconds(4).Ticks;
            return Done(call == 1 ? Result(OwnShape) : Result("", 1));
        });

        var observation = await ObserveAsync(process, clock);

        Assert.Equal(GitHubOriginStatus.NotConfigured, observation.Status);
        Assert.Equal(TimeSpan.FromSeconds(15), process.Requests[0].Timeout);
        Assert.Equal(TimeSpan.FromSeconds(11), process.Requests[1].Timeout);
    }

    [Fact]
    public async Task An_exhausted_budget_never_starts_the_configuration_read()
    {
        var clock = new SteppingTimeProvider();
        var process = new RecordingProcessAdapter((call, _, _) =>
        {
            clock.Ticks += TimeSpan.FromSeconds(15).Ticks;
            return Done(Result(OwnShape));
        });

        var observation = await ObserveAsync(process, clock);

        Assert.Equal(GitHubOriginStatus.Unavailable, observation.Status);
        Assert.Single(process.Requests);
    }

    [Theory]
    [InlineData("true\n.\n.\n\n")]
    [InlineData("true\n.git\n.git\nC:/repos/origin-fixture\n")]
    [InlineData("false\n.git\n.git\nC:/repos/elsewhere\n")]
    [InlineData("false\n.git\n.git\nC:/repos\n")]
    [InlineData("false\n.git\n.git\nC:/repos/origin-fixture/sub\n")]
    [InlineData("false\n.git/worktrees/w\n.git\nC:/repos/origin-fixture\n")]
    [InlineData("false\n.git\n../shared/.git\nC:/repos/origin-fixture\n")]
    [InlineData("false\nC:/elsewhere/.git\nC:/elsewhere/.git\nC:/repos/origin-fixture\n")]
    [InlineData("false\n.git\n.git\n")]
    [InlineData("false\n.git\nC:/repos/origin-fixture\n")]
    [InlineData("false\n.git\n.git\nC:/repos/origin-fixture\nextra\n")]
    [InlineData("false\n.git\n.git\nC:/repos/origin-fixture")]
    [InlineData("false\r\n.git\r\n.git\r\nC:/repos/origin-fixture\r\n")]
    [InlineData("")]
    public async Task A_shape_that_is_not_the_roots_own_main_repository_is_refused_before_any_configuration_read(string shape)
    {
        var process = Script(shape, Result("https://github.com/acme/widgets\0"));

        var observation = await ObserveAsync(process);

        Assert.Equal(GitHubOriginStatus.Unavailable, observation.Status);
        Assert.Null(observation.Target);
        Assert.Single(process.Requests);
    }

    [Fact]
    public async Task The_roots_own_dot_git_directory_is_accepted_in_any_case_and_slash_spelling()
    {
        var process = Script("false\n.git\nC:/REPOS/Origin-Fixture/.git\nc:/repos/origin-fixture\n", Result("", 1));

        var observation = await ObserveAsync(process);

        Assert.Equal(GitHubOriginStatus.NotConfigured, observation.Status);
    }

    [Theory]
    [InlineData(128, false, false)]
    [InlineData(2, false, false)]
    [InlineData(1, true, false)]
    public async Task A_failed_shape_check_is_unavailable_without_a_configuration_read(int exitCode, bool stdoutTruncated, bool stderrTruncated)
    {
        var process = new RecordingProcessAdapter((_, _, _) => Done(Result(OwnShape, exitCode, stdoutTruncated, stderrTruncated)));

        var observation = await ObserveAsync(process);

        Assert.Equal(GitHubOriginStatus.Unavailable, observation.Status);
        Assert.Single(process.Requests);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task A_truncated_shape_answer_is_never_trusted(bool stdoutTruncated, bool stderrTruncated)
    {
        var process = new RecordingProcessAdapter((_, _, _) => Done(Result(OwnShape, 0, stdoutTruncated, stderrTruncated)));

        Assert.Equal(GitHubOriginStatus.Unavailable, (await ObserveAsync(process)).Status);
        Assert.Single(process.Requests);
    }

    [Fact]
    public async Task Exit_one_with_empty_untruncated_output_is_not_configured()
    {
        var observation = await ObserveConfigAsync(Result("", 1));

        Assert.Equal(GitHubOriginStatus.NotConfigured, observation.Status);
        Assert.Null(observation.Target);
    }

    [Theory]
    [InlineData("x", 1, false, false)]
    [InlineData("", 1, true, false)]
    [InlineData("", 1, false, true)]
    public async Task Exit_one_is_not_configured_only_for_empty_untruncated_output(
        string stdout, int exitCode, bool stdoutTruncated, bool stderrTruncated)
    {
        var observation = await ObserveConfigAsync(Result(stdout, exitCode, stdoutTruncated, stderrTruncated));

        Assert.Equal(GitHubOriginStatus.Unavailable, observation.Status);
    }

    [Theory]
    [InlineData(128)]
    [InlineData(5)]
    [InlineData(3)]
    [InlineData(2)]
    [InlineData(-1)]
    public async Task Any_other_exit_is_unavailable_even_with_a_supported_value(int exitCode)
    {
        var observation = await ObserveConfigAsync(Result("https://github.com/acme/widgets\0", exitCode));

        Assert.Equal(GitHubOriginStatus.Unavailable, observation.Status);
    }

    [Theory]
    [InlineData("https://github.com/acme/widgets\0https://github.com/acme/widgets\0")]
    [InlineData("https://github.com/acme/widgets\0git@github.com:other/repo\0")]
    [InlineData("https://github.com/acme/widgets\n")]
    [InlineData("https://github.com/acme/widgets")]
    [InlineData("\0")]
    [InlineData("\0\0")]
    [InlineData("")]
    [InlineData("https://github.com/acme/widgets\0\0")]
    [InlineData("https://github.com/acme/wid\0gets\0")]
    [InlineData("\0https://github.com/acme/widgets\0")]
    public async Task Exit_zero_requires_exactly_one_non_empty_nul_terminated_value(string stdout)
    {
        var observation = await ObserveConfigAsync(Result(stdout));

        Assert.Equal(GitHubOriginStatus.Unavailable, observation.Status);
        Assert.Null(observation.Target);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task A_truncated_configuration_answer_is_never_trusted(bool stdoutTruncated, bool stderrTruncated)
    {
        var observation = await ObserveConfigAsync(Result("https://github.com/acme/widgets\0", 0, stdoutTruncated, stderrTruncated));

        Assert.Equal(GitHubOriginStatus.Unavailable, observation.Status);
    }

    [Fact]
    public async Task A_timeout_of_either_invocation_is_unavailable()
    {
        var firstTimesOut = new RecordingProcessAdapter((_, _, _) => Done(Result("", outcome: ProcessExecutionOutcome.TimedOut)));
        Assert.Equal(GitHubOriginStatus.Unavailable, (await ObserveAsync(firstTimesOut)).Status);
        Assert.Single(firstTimesOut.Requests);

        var secondTimesOut = Script(OwnShape, Result("", outcome: ProcessExecutionOutcome.TimedOut));
        Assert.Equal(GitHubOriginStatus.Unavailable, (await ObserveAsync(secondTimesOut)).Status);
        Assert.Equal(2, secondTimesOut.Requests.Count);
    }

    [Fact]
    public async Task An_os_launch_failure_is_unavailable_and_carries_no_exception_text()
    {
        var process = new RecordingProcessAdapter((_, _, _) => throw new Win32Exception(2, "C:\\secret\\path"));

        var observation = await ObserveAsync(process);

        Assert.Equal(GitHubOriginObservation.Unavailable, observation);
    }

    [Fact]
    public async Task A_vanished_vetted_executable_is_unavailable()
    {
        var process = new RecordingProcessAdapter((_, _, _) =>
            throw new ArgumentException("missing", nameof(ProcessExecutionRequest.ExecutablePath)));

        Assert.Equal(GitHubOriginStatus.Unavailable, (await ObserveAsync(process)).Status);
    }

    [Fact]
    public async Task Any_other_argument_fault_is_a_programming_fault_and_propagates()
    {
        var process = new RecordingProcessAdapter((_, _, _) =>
            throw new ArgumentException("bad", nameof(ProcessExecutionRequest.WorkingDirectory)));

        await Assert.ThrowsAsync<ArgumentException>(() => ObserveAsync(process));
    }

    [Fact]
    public async Task An_unexpected_fault_propagates_from_either_invocation()
    {
        var first = new RecordingProcessAdapter((_, _, _) => throw new InvalidOperationException("boom"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ObserveAsync(first));

        var second = new RecordingProcessAdapter((call, _, _) =>
            call == 1 ? Done(Result(OwnShape)) : throw new InvalidOperationException("boom"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ObserveAsync(second));
    }

    [Fact]
    public async Task Cancellation_propagates_whether_thrown_or_reported_by_the_executor()
    {
        var thrown = new RecordingProcessAdapter((_, _, _) => throw new OperationCanceledException());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ObserveAsync(thrown));

        var reported = new RecordingProcessAdapter((_, _, _) => Done(Result("", outcome: ProcessExecutionOutcome.Cancelled)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ObserveAsync(reported));

        var reportedSecond = new RecordingProcessAdapter((call, _, _) =>
            Done(call == 1 ? Result(OwnShape) : Result("", outcome: ProcessExecutionOutcome.Cancelled)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ObserveAsync(reportedSecond));
    }

    [Fact]
    public async Task The_callers_cancellation_token_reaches_the_executor()
    {
        using var source = new CancellationTokenSource();
        CancellationToken seen = default;
        var process = new RecordingProcessAdapter((_, _, token) =>
        {
            seen = token;
            return Done(Result("", 1, outcome: ProcessExecutionOutcome.Exited));
        });

        await ObserveAsync(process, cancellationToken: source.Token);

        Assert.Equal(source.Token, seen);
    }

    [Theory]
    [InlineData("https://github.com/acme/widgets", "acme", "widgets")]
    [InlineData("https://github.com/acme/widgets.git", "acme", "widgets")]
    [InlineData("git@github.com:acme/widgets", "acme", "widgets")]
    [InlineData("git@github.com:acme/widgets.git", "acme", "widgets")]
    [InlineData("ssh://git@github.com/acme/widgets", "acme", "widgets")]
    [InlineData("ssh://git@github.com/acme/widgets.git", "acme", "widgets")]
    [InlineData("https://github.com/Acme-Corp/Widgets_v2.x-y", "Acme-Corp", "Widgets_v2.x-y")]
    [InlineData("https://github.com/a/b", "a", "b")]
    [InlineData("https://github.com/0/9", "0", "9")]
    [InlineData("https://github.com/acme/widgets.git.git", "acme", "widgets.git")]
    [InlineData("https://github.com/acme/widgets.gitx", "acme", "widgets.gitx")]
    [InlineData("https://github.com/acme/a.b", "acme", "a.b")]
    public void The_three_supported_spellings_normalize_to_owner_and_repository(string raw, string owner, string repository)
    {
        var target = GitHubOriginUrlParser.TryParse(raw);

        Assert.Equal(new GitHubOriginTarget(owner, repository), target);
        Assert.Equal("github.com", target!.Host);
    }

    [Fact]
    public void Owner_and_repository_length_boundaries_are_exact()
    {
        Assert.NotNull(GitHubOriginUrlParser.TryParse($"https://github.com/{new string('a', 39)}/r"));
        Assert.Null(GitHubOriginUrlParser.TryParse($"https://github.com/{new string('a', 40)}/r"));
        Assert.NotNull(GitHubOriginUrlParser.TryParse($"https://github.com/o/{new string('r', 100)}"));
        Assert.Null(GitHubOriginUrlParser.TryParse($"https://github.com/o/{new string('r', 101)}"));
        Assert.NotNull(GitHubOriginUrlParser.TryParse($"https://github.com/o/{new string('r', 100)}.git"));
        Assert.Null(GitHubOriginUrlParser.TryParse($"https://github.com/o/{new string('r', 101)}.git"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" https://github.com/acme/widgets")]
    [InlineData("https://github.com/acme/widgets ")]
    [InlineData("https://github.com/acme/widgets\n")]
    [InlineData("https://github.com/acme/widgets\t")]
    [InlineData("https://github.com/acme/wid gets")]
    [InlineData("https://github.com/acme/widgets/")]
    [InlineData("https://github.com/acme/widgets//")]
    [InlineData("https://github.com/acme//widgets")]
    [InlineData("https://github.com//widgets")]
    [InlineData("https://github.com/acme")]
    [InlineData("https://github.com/acme/")]
    [InlineData("https://github.com/acme/widgets/extra")]
    [InlineData("https://github.com/acme/widgets/tree/main")]
    [InlineData("https://user@github.com/acme/widgets")]
    [InlineData("https://user:pass@github.com/acme/widgets")]
    [InlineData("https://token@github.com/acme/widgets.git")]
    [InlineData("https://github.com:443/acme/widgets")]
    [InlineData("https://github.com/acme/widgets?x=1")]
    [InlineData("https://github.com/acme/widgets#frag")]
    [InlineData("https://github.com/acme/wid%67ets")]
    [InlineData("https://github.com/ac%6de/widgets")]
    [InlineData("https://github.com/acme/widgets%2F")]
    [InlineData("https://github.com/acme/widgets\\x")]
    [InlineData("http://github.com/acme/widgets")]
    [InlineData("HTTPS://github.com/acme/widgets")]
    [InlineData("https://GitHub.com/acme/widgets")]
    [InlineData("https://www.github.com/acme/widgets")]
    [InlineData("https://github.com.evil.example/acme/widgets")]
    [InlineData("https://evil.example/github.com/acme/widgets")]
    [InlineData("https://gitlab.com/acme/widgets")]
    [InlineData("git://github.com/acme/widgets")]
    [InlineData("ssh://github.com/acme/widgets")]
    [InlineData("ssh://other@github.com/acme/widgets")]
    [InlineData("ssh://git@github.com:22/acme/widgets")]
    [InlineData("ssh://git@github.com:acme/widgets")]
    [InlineData("other@github.com:acme/widgets")]
    [InlineData("git@gitlab.com:acme/widgets")]
    [InlineData("git@github.com:/acme/widgets")]
    [InlineData("git@github.com:acme/widgets/")]
    [InlineData("git@github.com/acme/widgets")]
    [InlineData("github.com/acme/widgets")]
    [InlineData("github.com:acme/widgets")]
    [InlineData("C:/repos/widgets")]
    [InlineData("../widgets")]
    [InlineData("/srv/git/widgets")]
    [InlineData("file:///srv/git/widgets")]
    [InlineData("https://github.com/-acme/widgets")]
    [InlineData("https://github.com/acme-/widgets")]
    [InlineData("https://github.com/ac_me/widgets")]
    [InlineData("https://github.com/ac.me/widgets")]
    [InlineData("https://github.com/acm\u00e9/widgets")]
    [InlineData("https://github.com/acme/widg\u00e9ts")]
    [InlineData("https://github.com/acme/.widgets")]
    [InlineData("https://github.com/acme/_widgets")]
    [InlineData("https://github.com/acme/-widgets")]
    [InlineData("https://github.com/acme/.")]
    [InlineData("https://github.com/acme/..")]
    [InlineData("https://github.com/acme/.git")]
    [InlineData("https://github.com/acme/..git")]
    [InlineData("https://github.com/./widgets")]
    [InlineData("https://github.com/../widgets")]
    [InlineData("https://github.com/acme/widgets@x")]
    [InlineData("https://github.com/acme/widgets:x")]
    [InlineData("https://github.com/acme/widgets\0")]
    [InlineData("https://github.com/acme/wid\u0001gets")]
    [InlineData("https://github.com/acme/widgets\u200b")]
    [InlineData("git@github.com:acme/widgets.git/")]
    [InlineData("git@github.com:acme/widgets.git ")]
    public void Every_unsupported_spelling_is_refused_without_being_normalized_into_a_target(string raw) =>
        Assert.Null(GitHubOriginUrlParser.TryParse(raw));
}
