using DevalCopilot.Api.IntegrationTests.ManualQualification;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Domain.Features.Processes;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>The closed projector of one <c>claude auth status</c> result, over fabricated outputs only.</summary>
public sealed class ClaudeStatusProjectorTests
{
    private static ClaudeStatusObservation Project(ProcessExecutionResult result) =>
        ClaudeStatusProjector.Project(ClaudeStatusObservation.Ordinary, result);

    private static ProcessExecutionResult Exit(int code, string output, ProcessIoCompletion? io = null, bool truncated = false) =>
        ScriptedStatusAdapter.Result(ProcessExecutionOutcome.Exited, code, output, io, truncated);

    [Fact]
    public void An_authenticated_report_with_exit_zero_is_a_coherent_authenticated_observation()
    {
        var observation = Project(Exit(0, "{\"loggedIn\":true,\"authMethod\":\"claude.ai\",\"email\":\"someone@example.invalid\"}"));

        Assert.Equal(ClaudeStatusState.ReportedAuthenticated, observation.State);
        Assert.Equal("claudeAi", observation.AuthMethod);
        Assert.Equal(ClaudeStatusPhysical.Exited, observation.Physical);
        Assert.Equal(0, observation.ExitCode);
        Assert.True(observation.IsCoherent);
    }

    [Fact]
    public void A_logged_out_report_with_the_documented_exit_one_is_a_completed_observation_not_a_physical_failure()
    {
        var observation = Project(Exit(1, "{\"loggedIn\":false,\"authMethod\":\"none\"}"));

        Assert.Equal(ClaudeStatusState.ReportedLoggedOut, observation.State);
        Assert.Equal("none", observation.AuthMethod);
        Assert.Equal(ClaudeStatusPhysical.Exited, observation.Physical);
        Assert.True(observation.IsCoherent);
    }

    public static TheoryData<string, int, string> Incoherent => new()
    {
        { "authenticated report with exit one", 1, "{\"loggedIn\":true}" },
        { "logged out report with exit zero", 0, "{\"loggedIn\":false}" },
        { "exit two", 2, "{\"loggedIn\":true}" },
        { "exit minus one", -1, "{\"loggedIn\":false}" },
        { "empty output", 0, "" },
        { "not json", 0, "logged in as someone" },
        { "json array", 0, "[true]" },
        { "missing loggedIn", 0, "{\"authMethod\":\"claude.ai\"}" },
        { "loggedIn as text", 0, "{\"loggedIn\":\"true\"}" },
        { "loggedIn as number", 1, "{\"loggedIn\":0}" },
        { "loggedIn null", 0, "{\"loggedIn\":null}" },
        { "duplicated loggedIn", 0, "{\"loggedIn\":true,\"loggedIn\":true}" },
        { "duplicated authMethod", 0, "{\"loggedIn\":true,\"authMethod\":\"a\",\"authMethod\":\"b\"}" },
        { "trailing garbage", 0, "{\"loggedIn\":true} extra" },
        { "unterminated", 0, "{\"loggedIn\":true" },
    };

    [Theory]
    [MemberData(nameof(Incoherent))]
    public void A_malformed_contradicting_or_unrecognized_report_is_unknown_and_never_guessed(string label, int exitCode, string output)
    {
        Assert.False(string.IsNullOrEmpty(label));

        var observation = Project(Exit(exitCode, output));

        Assert.Equal(ClaudeStatusState.Unknown, observation.State);
        Assert.False(observation.IsCoherent);
        Assert.Equal("unknown", observation.AuthMethod);
        Assert.Null(observation.ConfigDirectoryFingerprint);
    }

    [Fact]
    public void Output_nested_beyond_the_bound_is_unknown_rather_than_parsed()
    {
        var deep = string.Concat(Enumerable.Repeat("[", 64)) + string.Concat(Enumerable.Repeat("]", 64));

        Assert.Equal(ClaudeStatusState.Unknown, Project(Exit(0, "{\"loggedIn\":true,\"x\":" + deep + "}")).State);
    }

    public static TheoryData<string, ProcessIoCompletion> IncompleteIo => new()
    {
        { "stdout deadline", new(OutputCaptureCompletion.DeadlineExceeded, OutputCaptureCompletion.Complete, InputDeliveryCompletion.NotRedirected) },
        { "stderr deadline", new(OutputCaptureCompletion.Complete, OutputCaptureCompletion.DeadlineExceeded, InputDeliveryCompletion.NotRedirected) },
        { "stdout faulted", new(OutputCaptureCompletion.Faulted, OutputCaptureCompletion.Complete, InputDeliveryCompletion.NotRedirected) },
        { "stderr unknown", new(OutputCaptureCompletion.Complete, OutputCaptureCompletion.Unknown, InputDeliveryCompletion.NotRedirected) },
        { "everything unknown", ProcessIoCompletion.Unknown },
    };

    [Theory]
    [MemberData(nameof(IncompleteIo))]
    public void Incomplete_io_is_never_parsed_even_when_the_partial_output_looks_perfect(string label, ProcessIoCompletion io)
    {
        Assert.False(string.IsNullOrEmpty(label));

        var observation = Project(Exit(0, "{\"loggedIn\":true,\"authMethod\":\"claude.ai\"}", io));

        Assert.Equal(ClaudeStatusState.Unknown, observation.State);
        Assert.False(observation.IoComplete);
        Assert.False(observation.IsCoherent);
        Assert.Equal(ClaudeStatusPhysical.Exited, observation.Physical);
    }

    [Fact]
    public void Truncated_output_is_never_parsed()
    {
        var observation = Project(Exit(0, "{\"loggedIn\":true}", truncated: true));

        Assert.Equal(ClaudeStatusState.Unknown, observation.State);
        Assert.True(observation.StandardOutputTruncated);
        Assert.False(observation.IsCoherent);
    }

    [Theory]
    [InlineData(ProcessExecutionOutcome.TimedOut, ClaudeStatusPhysical.TimedOut)]
    [InlineData(ProcessExecutionOutcome.Cancelled, ClaudeStatusPhysical.Cancelled)]
    public void A_timeout_or_cancellation_is_a_physical_outcome_with_no_exit_code_and_no_report(
        ProcessExecutionOutcome outcome, ClaudeStatusPhysical expected)
    {
        var observation = Project(ScriptedStatusAdapter.Result(outcome, null, "{\"loggedIn\":true}"));

        Assert.Equal(expected, observation.Physical);
        Assert.Null(observation.ExitCode);
        Assert.Equal(ClaudeStatusState.Unknown, observation.State);
    }

    [Theory]
    [InlineData("none", "none")]
    [InlineData("claude.ai", "claudeAi")]
    [InlineData("console", "console")]
    [InlineData("someone@example.invalid", "other")]
    [InlineData("C:/Users/someone/secret", "other")]
    public void The_method_is_reduced_to_a_closed_token_and_free_text_never_survives(string method, string expected)
    {
        var observation = Project(Exit(0, $"{{\"loggedIn\":true,\"authMethod\":\"{method}\"}}"));

        Assert.Equal(expected, observation.AuthMethod);
    }

    [Fact]
    public void A_missing_method_is_unreported()
    {
        Assert.Equal("unreported", Project(Exit(0, "{\"loggedIn\":true}")).AuthMethod);
    }

    [Fact]
    public void The_configuration_directory_is_only_a_one_way_fingerprint_that_ignores_case_and_a_trailing_separator()
    {
        var first = Project(Exit(0, "{\"loggedIn\":true,\"configDirectory\":\"C:\\\\Users\\\\Someone\\\\.claude\"}"));
        var same = Project(Exit(0, "{\"loggedIn\":true,\"configDirectory\":\"c:\\\\users\\\\someone\\\\.claude\\\\\"}"));
        var other = Project(Exit(0, "{\"loggedIn\":true,\"configDirectory\":\"C:\\\\Users\\\\Someone\\\\other\"}"));

        Assert.NotNull(first.ConfigDirectoryFingerprint);
        Assert.Equal(first.ConfigDirectoryFingerprint, same.ConfigDirectoryFingerprint);
        Assert.NotEqual(first.ConfigDirectoryFingerprint, other.ConfigDirectoryFingerprint);
        Assert.DoesNotContain("someone", first.ConfigDirectoryFingerprint, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(64, first.ConfigDirectoryFingerprint!.Length);
    }

    [Fact]
    public void An_oversized_configuration_directory_is_not_fingerprinted_but_is_not_fatal()
    {
        var oversized = Project(Exit(0, "{\"loggedIn\":true,\"configDirectory\":\"" + new string('a', 2000) + "\"}"));

        Assert.Null(oversized.ConfigDirectoryFingerprint);
        Assert.Equal(ClaudeStatusState.ReportedAuthenticated, oversized.State);
    }

    [Fact]
    public void An_absent_optional_member_stays_distinct_from_a_present_mistyped_one()
    {
        var absent = Project(Exit(0, "{\"loggedIn\":true}"));
        var mistyped = Project(Exit(0, "{\"loggedIn\":true,\"configDirectory\":42}"));

        Assert.Equal(ClaudeStatusState.ReportedAuthenticated, absent.State);
        Assert.Equal("unreported", absent.AuthMethod);
        Assert.Equal(ClaudeStatusState.Unknown, mistyped.State);
    }

    public static TheoryData<string, int, string> MistypedKnownMembers => new()
    {
        { "authMethod number with authenticated", 0, "{\"loggedIn\":true,\"authMethod\":123}" },
        { "configDirectory number with authenticated", 0, "{\"loggedIn\":true,\"configDirectory\":123}" },
        { "authMethod number with logged out", 1, "{\"loggedIn\":false,\"authMethod\":123}" },
        { "configDirectory number with logged out", 1, "{\"loggedIn\":false,\"configDirectory\":123}" },
        { "authMethod null", 0, "{\"loggedIn\":true,\"authMethod\":null}" },
        { "configDirectory null", 0, "{\"loggedIn\":true,\"configDirectory\":null}" },
        { "authMethod boolean", 0, "{\"loggedIn\":true,\"authMethod\":true}" },
        { "configDirectory object", 0, "{\"loggedIn\":true,\"configDirectory\":{}}" },
        { "authMethod array", 0, "{\"loggedIn\":true,\"authMethod\":[\"claude.ai\"]}" },
        { "duplicated configDirectory", 0, "{\"loggedIn\":true,\"configDirectory\":\"a\",\"configDirectory\":\"b\"}" },
        { "duplicated with differing types", 0, "{\"loggedIn\":true,\"authMethod\":\"claude.ai\",\"authMethod\":1}" },
    };

    [Theory]
    [MemberData(nameof(MistypedKnownMembers))]
    public void A_present_known_member_of_the_wrong_type_or_duplicated_is_never_a_reported_state(string label, int exitCode, string output)
    {
        Assert.False(string.IsNullOrEmpty(label));

        var observation = Project(Exit(exitCode, output));

        Assert.Equal(ClaudeStatusState.Unknown, observation.State);
        Assert.False(observation.IsCoherent);
        Assert.Equal("unknown", observation.AuthMethod);
        Assert.Null(observation.ConfigDirectoryFingerprint);
    }

    [Fact]
    public void No_member_of_the_observation_carries_the_account_or_any_other_output_field()
    {
        var observation = Project(Exit(0, "{\"loggedIn\":true,\"email\":\"someone@example.invalid\",\"orgId\":\"org-secret\",\"authMethod\":\"claude.ai\"}"));

        var text = string.Join('|', typeof(ClaudeStatusObservation).GetProperties().Select(property => property.GetValue(observation)?.ToString()));

        Assert.DoesNotContain("someone", text, StringComparison.Ordinal);
        Assert.DoesNotContain("org-secret", text, StringComparison.Ordinal);
    }
}
