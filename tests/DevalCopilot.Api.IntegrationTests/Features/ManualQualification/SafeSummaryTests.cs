using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

public sealed class SafeSummaryTests : QualificationTestBase
{
    private static readonly string[] NoForbiddenValues = [];

    [Fact]
    public async Task A_qualified_session_summary_carries_the_allowlisted_facts_and_no_path_or_credential()
    {
        var environment = new ScriptedEnvironment
        {
            Targets = new TargetJudgement(true,
            [
                new TargetReport("codex", true, "Real", "NodeScript", "0.43.0"),
                new TargetReport("claude", true, "Real", "DirectExecutable", "2.1.0"),
            ]),
        };
        var report = await RunAsync(environment);

        var text = SafeSummary.Render(report, NoForbiddenValues);

        using var summary = JsonDocument.Parse(text);
        var root = summary.RootElement;
        Assert.Equal("Qualified", root.GetProperty("result").GetString());
        Assert.Equal("Challenge", root.GetProperty("verdict").GetString());
        Assert.Equal("0.43.0", root.GetProperty("targets")[0].GetProperty("observedVersion").GetString());
        Assert.True(root.GetProperty("allowances").GetProperty("planner").GetProperty("consumedBeforePost").GetBoolean());
        Assert.Equal(0, root.GetProperty("allowances").GetProperty("unused").GetArrayLength());
        Assert.True(root.GetProperty("reviewer").GetProperty("lineage").GetProperty("repliesToThePersistedProposal").GetBoolean());
        Assert.True(root.GetProperty("reviewer").GetProperty("lineage").GetProperty("inputIsExactlyThatProposal").GetBoolean());
        Assert.True(root.GetProperty("source").GetProperty("unchanged").GetBoolean());
        Assert.True(root.GetProperty("cleanup").GetProperty("ownedRootRemoved").GetBoolean());
        Assert.DoesNotMatch("[A-Za-z]:[/\\\\]", text);
        Assert.DoesNotContain("Bearer", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_blocked_session_lists_both_allowances_as_unused()
    {
        var environment = new ScriptedEnvironment
        {
            Targets = new TargetJudgement(false, [new TargetReport("codex", false, "NotObservedExecutableNotFound", null, null)]),
        };
        var report = await RunAsync(environment);

        using var summary = JsonDocument.Parse(SafeSummary.Render(report, NoForbiddenValues));
        var allowances = summary.RootElement.GetProperty("allowances");

        Assert.Equal(["planner", "reviewer"], allowances.GetProperty("unused").EnumerateArray().Select(item => item.GetString()));
        Assert.False(allowances.GetProperty("planner").GetProperty("postAttempted").GetBoolean());
        Assert.Equal("Blocked", summary.RootElement.GetProperty("result").GetString());
        Assert.Equal(JsonValueKind.Null, summary.RootElement.GetProperty("planner").ValueKind);
    }

    [Fact]
    public async Task A_failure_reports_actual_observed_invocations_apart_from_the_spent_allowances()
    {
        var environment = new ScriptedEnvironment { OnSubmit = (_, _) => throw new TimeoutException() };
        var report = await RunAsync(environment);

        using var summary = JsonDocument.Parse(SafeSummary.Render(report, NoForbiddenValues));
        var planner = summary.RootElement.GetProperty("allowances").GetProperty("planner");

        Assert.True(planner.GetProperty("consumedBeforePost").GetBoolean());
        Assert.True(planner.GetProperty("postAttempted").GetBoolean());
        Assert.False(planner.GetProperty("executionObserved").GetBoolean());
        Assert.Equal("Unknown", planner.GetProperty("invocationState").GetString());
        Assert.Equal("PlannerSubmissionAmbiguous", summary.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_forbidden_value_anywhere_replaces_the_whole_summary_with_a_fixed_verdict()
    {
        var environment = new ScriptedEnvironment
        {
            OnSubmit = (_, _) => Task.FromResult(SubmissionResult.Refused("hunter2")),
        };
        var report = await RunAsync(environment);

        var text = SafeSummary.Render(report, ["hunter2"]);

        Assert.Equal("{\"schemaVersion\":1,\"result\":\"Failed\",\"code\":\"SummaryRefused\"}", text);
    }

    [Theory]
    [InlineData("C:/Users/someone/secret-project")]
    [InlineData("a b")]
    [InlineData("line\nbreak")]
    [InlineData("a\"quote")]
    public async Task A_free_text_code_never_reaches_the_summary(string hostile)
    {
        var environment = new ScriptedEnvironment
        {
            OnSubmit = (_, _) => Task.FromResult(SubmissionResult.Refused(hostile)),
        };
        var report = await RunAsync(environment);

        var text = SafeSummary.Render(report, NoForbiddenValues);

        Assert.DoesNotContain("someone", text, StringComparison.Ordinal);
        Assert.DoesNotContain("break", text, StringComparison.Ordinal);
        Assert.DoesNotContain("quote", text, StringComparison.Ordinal);
        using var _ = JsonDocument.Parse(text);
    }
}
