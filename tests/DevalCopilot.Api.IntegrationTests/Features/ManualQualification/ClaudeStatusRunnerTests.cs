using DevalCopilot.Api.IntegrationTests.ManualQualification;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Domain.Features.Processes;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>The two serial status observations, their stop rules and the plan's decision table, over an owned process double that
/// never starts a process.</summary>
public sealed class ClaudeStatusRunnerTests : QualificationTestBase
{
    private const string Claude = @"C:\fixture\claude.exe";
    private const string Work = @"C:\fixture\work";

    private static readonly Dictionary<string, string> Ordinary = new() { ["ORDINARY_ONLY"] = "value-a", ["USERPROFILE"] = "home" };
    private static readonly Dictionary<string, string> Filtered = new() { ["USERPROFILE"] = "home", ["SystemRoot"] = "root" };

    private (ClaudeStatusRunner Runner, ProcessAdmission Admission) Compose(ScriptedStatusAdapter adapter)
    {
        var context = new StatusContext { WorkingDirectory = Work };
        var counters = new AdmissionLedger(LedgerDirectory, ClaudeStatusSummary.Authorization, TimeProvider.System);
        var admission = new ProcessAdmission(StatusInventory.Create(Claude, context, Execution), StatusInventory.Total, counters);
        return (new ClaudeStatusRunner(new AdmittingProcessExecutionAdapter(adapter, admission), context), admission);
    }

    private static readonly TimeSpan Execution = TimeSpan.FromSeconds(30);

    private static Task<ClaudeStatusRun> Run(ClaudeStatusRunner runner, Dictionary<string, string>? filtered = null, bool nullFiltered = false) =>
        runner.RunAsync(
            Claude, Work, Ordinary, nullFiltered ? null : filtered ?? Filtered, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10), CancellationToken.None);

    [Fact]
    public async Task Both_authenticated_starts_exactly_two_typed_status_children_serially_with_their_own_environments()
    {
        var adapter = new ScriptedStatusAdapter(ScriptedStatusAdapter.Authenticated(), ScriptedStatusAdapter.Authenticated());
        var (runner, admission) = Compose(adapter);

        var run = await Run(runner);

        Assert.Equal(ClaudeStatusDecision.BothAuthenticated, run.Decision);
        Assert.Null(run.StopCode);
        Assert.Equal(2, adapter.Requests.Count);
        Assert.All(adapter.Requests, request =>
        {
            Assert.Equal(Claude, request.ExecutablePath);
            Assert.Equal(["auth", "status"], request.Arguments);
            Assert.Equal(Work, request.WorkingDirectory);
            Assert.Equal(Work, request.ApprovedRoot);
            Assert.Null(request.StandardInput);
            Assert.Null(request.StandardOutputSinkPath);
            Assert.Null(request.StandardErrorSinkPath);
            Assert.Equal(TimeSpan.FromSeconds(30), request.Timeout);
            Assert.Equal(ClaudeStatusProjector.MaximumStreamBytes, request.MaxBytesPerStream);
            Assert.Equal(2 * ClaudeStatusProjector.MaximumStreamBytes, request.MaxTotalCapturedBytes);
        });
        Assert.Equal(Ordinary, adapter.Requests[0].EnvironmentVariables);
        Assert.Equal(Filtered, adapter.Requests[1].EnvironmentVariables);
        Assert.Equal(1, admission.Facts.Admitted["claude.status.ordinary"]);
        Assert.Equal(1, admission.Facts.Admitted["claude.status.filtered"]);
        Assert.Equal(0, admission.Facts.TotalRefused);
    }

    [Fact]
    public async Task The_second_child_never_starts_before_the_first_has_finished()
    {
        var firstFinished = false;
        var adapter = new ScriptedStatusAdapter(
            async token =>
            {
                await Task.Delay(50, token);
                firstFinished = true;
                return ScriptedStatusAdapter.Result(ProcessExecutionOutcome.Exited, 0, "{\"loggedIn\":true}");
            },
            _ =>
            {
                Assert.True(firstFinished);
                return Task.FromResult(ScriptedStatusAdapter.Result(ProcessExecutionOutcome.Exited, 0, "{\"loggedIn\":true}"));
            });
        var (runner, _) = Compose(adapter);

        var run = await Run(runner);

        Assert.Equal(ClaudeStatusDecision.BothAuthenticated, run.Decision);
    }

    [Theory]
    [InlineData(true, true, ClaudeStatusDecision.BothAuthenticated)]
    [InlineData(true, false, ClaudeStatusDecision.OrdinaryAuthenticatedFilteredLoggedOut)]
    [InlineData(false, false, ClaudeStatusDecision.BothLoggedOut)]
    [InlineData(false, true, ClaudeStatusDecision.ReverseDiscrepancy)]
    public async Task The_decision_is_exactly_the_plans_table_and_claims_nothing_more(bool ordinaryIn, bool filteredIn, ClaudeStatusDecision expected)
    {
        var adapter = new ScriptedStatusAdapter(
            ordinaryIn ? ScriptedStatusAdapter.Authenticated() : ScriptedStatusAdapter.LoggedOut(),
            filteredIn ? ScriptedStatusAdapter.Authenticated() : ScriptedStatusAdapter.LoggedOut());
        var (runner, _) = Compose(adapter);

        var run = await Run(runner);

        Assert.Equal(expected, run.Decision);
        Assert.Null(run.StopCode);
        Assert.Equal(ordinaryIn ? ClaudeStatusState.ReportedAuthenticated : ClaudeStatusState.ReportedLoggedOut, run.Ordinary.State);
        Assert.Equal(filteredIn ? ClaudeStatusState.ReportedAuthenticated : ClaudeStatusState.ReportedLoggedOut, run.Filtered.State);
    }

    [Fact]
    public async Task The_configuration_directory_comparison_is_derived_in_memory_and_unknown_when_a_field_is_missing()
    {
        var equal = await Run(Compose(new ScriptedStatusAdapter(
            ScriptedStatusAdapter.Authenticated(@"C:\Users\A\.claude"), ScriptedStatusAdapter.Authenticated(@"c:\users\a\.claude"))).Runner);
        Assert.Equal(ConfigDirectoryEquality.Equal, equal.ConfigDirectory);
    }

    [Fact]
    public async Task Different_configuration_directories_are_reported_as_different()
    {
        var different = await Run(Compose(new ScriptedStatusAdapter(
            ScriptedStatusAdapter.Authenticated(@"C:\Users\A\.claude"), ScriptedStatusAdapter.LoggedOut(@"C:\Users\A\other"))).Runner);

        Assert.Equal(ConfigDirectoryEquality.Different, different.ConfigDirectory);
    }

    [Fact]
    public async Task A_missing_configuration_directory_in_either_observation_is_unknown()
    {
        var missing = await Run(Compose(new ScriptedStatusAdapter(
            ScriptedStatusAdapter.Authenticated(@"C:\Users\A\.claude"), ScriptedStatusAdapter.Authenticated())).Runner);

        Assert.Equal(ConfigDirectoryEquality.Unknown, missing.ConfigDirectory);
    }

    public static TheoryData<string, Func<CancellationToken, Task<ProcessExecutionResult>>> IncoherentFirst => new()
    {
        { "timeout", _ => Task.FromResult(ScriptedStatusAdapter.Result(ProcessExecutionOutcome.TimedOut, null, string.Empty)) },
        { "cancelled", _ => Task.FromResult(ScriptedStatusAdapter.Result(ProcessExecutionOutcome.Cancelled, null, string.Empty)) },
        { "malformed", ScriptedStatusAdapter.Exited(0, "not json") },
        { "contradiction", ScriptedStatusAdapter.Exited(1, "{\"loggedIn\":true}") },
        {
            "incomplete io",
            _ => Task.FromResult(ScriptedStatusAdapter.Result(
                ProcessExecutionOutcome.Exited,
                0,
                "{\"loggedIn\":true}",
                new ProcessIoCompletion(OutputCaptureCompletion.DeadlineExceeded, OutputCaptureCompletion.Complete, InputDeliveryCompletion.NotRedirected)))
        },
        { "truncated", _ => Task.FromResult(ScriptedStatusAdapter.Result(ProcessExecutionOutcome.Exited, 0, "{\"loggedIn\":true}", null, true)) },
        { "start failure", _ => throw new InvalidOperationException("C:/Users/someone/secret could not be started") },
        { "mistyped authMethod", ScriptedStatusAdapter.Exited(0, "{\"loggedIn\":true,\"authMethod\":123}") },
        { "mistyped configDirectory", ScriptedStatusAdapter.Exited(0, "{\"loggedIn\":true,\"configDirectory\":123}") },
        { "duplicated configDirectory", ScriptedStatusAdapter.Exited(0, "{\"loggedIn\":true,\"configDirectory\":\"a\",\"configDirectory\":\"a\"}") },
    };

    [Theory]
    [MemberData(nameof(IncoherentFirst))]
    public async Task An_incoherent_first_observation_stops_the_sequence_and_the_second_context_never_starts(
        string label, Func<CancellationToken, Task<ProcessExecutionResult>> first)
    {
        Assert.False(string.IsNullOrEmpty(label));
        var adapter = new ScriptedStatusAdapter(first, ScriptedStatusAdapter.Authenticated());
        var (runner, admission) = Compose(adapter);

        var run = await Run(runner);

        Assert.Equal(ClaudeStatusDecision.Stopped, run.Decision);
        Assert.Equal("OrdinaryObservationNotCoherent", run.StopCode);
        Assert.Single(adapter.Requests);
        Assert.Equal(ClaudeStatusPhysical.NotStarted, run.Filtered.Physical);
        Assert.False(admission.Facts.Admitted.ContainsKey("claude.status.filtered"));
        Assert.DoesNotContain("someone", run.StopCode, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_incoherent_second_observation_stops_with_no_decision_and_no_second_comparison()
    {
        var adapter = new ScriptedStatusAdapter(ScriptedStatusAdapter.Authenticated(), ScriptedStatusAdapter.Exited(0, "garbage"), ScriptedStatusAdapter.Authenticated());
        var (runner, _) = Compose(adapter);

        var run = await Run(runner);

        Assert.Equal(ClaudeStatusDecision.Stopped, run.Decision);
        Assert.Equal("FilteredObservationNotCoherent", run.StopCode);
        Assert.Equal(2, adapter.Requests.Count);
        Assert.Equal(ClaudeStatusState.ReportedAuthenticated, run.Ordinary.State);
    }

    [Fact]
    public async Task When_the_production_builder_refuses_no_process_starts_and_the_smaller_map_is_never_tried()
    {
        var adapter = new ScriptedStatusAdapter(ScriptedStatusAdapter.Authenticated(), ScriptedStatusAdapter.Authenticated());
        var (runner, admission) = Compose(adapter);

        var run = await Run(runner, nullFiltered: true);

        Assert.Equal(ClaudeStatusDecision.Stopped, run.Decision);
        Assert.Equal("FilteredEnvironmentUnavailable", run.StopCode);
        Assert.Empty(adapter.Requests);
        Assert.Equal(0, admission.Facts.TotalAdmitted);
    }

    [Fact]
    public async Task The_sequence_bound_cancels_a_running_child_and_stops_without_starting_the_second()
    {
        var adapter = new ScriptedStatusAdapter(
            async token =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return ScriptedStatusAdapter.Result(ProcessExecutionOutcome.Exited, 0, string.Empty);
            },
            ScriptedStatusAdapter.Authenticated());
        var (runner, _) = Compose(adapter);

        var run = await runner.RunAsync(Claude, Work, Ordinary, Filtered, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(100), CancellationToken.None);

        Assert.Equal(ClaudeStatusDecision.Stopped, run.Decision);
        Assert.Equal(ClaudeStatusPhysical.Cancelled, run.Ordinary.Physical);
        Assert.Single(adapter.Requests);
    }

    [Fact]
    public async Task A_second_start_of_either_context_or_any_other_command_is_refused_by_the_inventory_before_the_adapter()
    {
        var adapter = new ScriptedStatusAdapter(ScriptedStatusAdapter.Authenticated(), ScriptedStatusAdapter.Authenticated());
        var context = new StatusContext { WorkingDirectory = Work };
        var counters = new AdmissionLedger(LedgerDirectory, ClaudeStatusSummary.Authorization, TimeProvider.System);
        var admission = new ProcessAdmission(StatusInventory.Create(Claude, context, Execution), StatusInventory.Total, counters);
        var boundary = new AdmittingProcessExecutionAdapter(adapter, admission);
        var runner = new ClaudeStatusRunner(boundary, context);
        await Run(runner);

        ProcessExecutionRequest Request(params string[] arguments) => new()
        {
            ExecutablePath = Claude, Arguments = arguments, WorkingDirectory = Work, ApprovedRoot = Work, Timeout = TimeSpan.FromSeconds(1),
        };

        context.Current = ClaudeStatusObservation.Ordinary;
        await Assert.ThrowsAsync<ProcessAdmissionRefusedException>(() => boundary.ExecuteAsync(Request("auth", "status"), CancellationToken.None));
        context.Current = ClaudeStatusObservation.Filtered;
        await Assert.ThrowsAsync<ProcessAdmissionRefusedException>(() => boundary.ExecuteAsync(Request("auth", "status"), CancellationToken.None));
        foreach (var other in new[] { new[] { "--version" }, ["--help"], ["auth", "login"], ["auth", "logout"], ["setup-token"], ["--print"], ["auth", "status", "--text"], [] })
        {
            await Assert.ThrowsAsync<ProcessAdmissionRefusedException>(() => boundary.ExecuteAsync(Request(other), CancellationToken.None));
        }

        Assert.Equal(2, adapter.Requests.Count);
    }

    [Fact]
    public async Task A_status_request_of_another_executable_is_refused_even_with_the_exact_arguments()
    {
        var adapter = new ScriptedStatusAdapter();
        var context = new StatusContext { Current = ClaudeStatusObservation.Ordinary, WorkingDirectory = Work };
        var counters = new AdmissionLedger(LedgerDirectory, ClaudeStatusSummary.Authorization, TimeProvider.System);
        var boundary = new AdmittingProcessExecutionAdapter(
            adapter, new ProcessAdmission(StatusInventory.Create(Claude, context, Execution), StatusInventory.Total, counters));

        await Assert.ThrowsAsync<ProcessAdmissionRefusedException>(() => boundary.ExecuteAsync(
            new ProcessExecutionRequest
            {
                ExecutablePath = @"C:\elsewhere\claude.exe",
                Arguments = ["auth", "status"],
                WorkingDirectory = Work,
                ApprovedRoot = Work,
                Timeout = TimeSpan.FromSeconds(1),
            },
            CancellationToken.None));

        Assert.Empty(adapter.Requests);
    }

    [Fact]
    public void The_environment_facts_are_closed_booleans_and_a_count_with_no_names_or_values()
    {
        var ordinary = ClaudeStatusEnvironmentFacts.Describe(new Dictionary<string, string>
        {
            ["PATH"] = "x", ["ANTHROPIC_API_KEY"] = "sk-secret", ["CLAUDE_CONFIG_DIR"] = "C:\\cfg", ["USERPROFILE"] = "C:\\u",
        });

        Assert.Equal(4, ordinary.KeyCount);
        Assert.True(ordinary.HasUserProfile);
        Assert.True(ordinary.HasClaudeConfigDirectory);
        Assert.True(ordinary.HasAnthropicVariable);
        Assert.True(ordinary.HasClaudeVariable);
        Assert.Equal(new ClaudeStatusEnvironmentFacts(0, false, false, false, false), ClaudeStatusEnvironmentFacts.Describe(null));
        Assert.DoesNotContain("sk-secret", ordinary.ToString(), StringComparison.Ordinal);
    }
}
