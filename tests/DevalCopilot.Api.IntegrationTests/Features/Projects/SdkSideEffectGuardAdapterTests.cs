using DevalCopilot.Application.Features.Processes.Ports;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Projects;

/// <summary>
/// ADR-0074: the pre-launch guard refuses a <c>dotnet.exe</c> request whose environment lacks either side-effect flag, or carries it with
/// anything but the literal <c>false</c>, before the real adapter is ever reached. The inner adapter here only records that it was called,
/// so nothing in this class starts a process, reads the user PATH or touches a certificate store.
/// </summary>
public sealed class SdkSideEffectGuardAdapterTests
{
    private const string AddToPath = "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH";
    private const string Certificate = "DOTNET_GENERATE_ASPNET_CERTIFICATE";

    [Fact]
    public async Task A_dotnet_request_with_both_flags_false_reaches_the_inner_adapter()
    {
        var inner = new RecordingAdapter();
        var guard = new SdkSideEffectGuardAdapter(inner);

        await Assert.ThrowsAsync<RecordingAdapter.Reached>(() => guard.ExecuteAsync(Request("dotnet.exe", AddToPath, "false", Certificate, "false"), default));

        Assert.Equal(1, inner.Calls);
        Assert.Equal(1, guard.Launches);
        Assert.Equal(0, guard.Refusals);
    }

    [Theory]
    [InlineData(null, null, "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH,DOTNET_GENERATE_ASPNET_CERTIFICATE")]
    [InlineData(null, "false", "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH")]
    [InlineData("false", null, "DOTNET_GENERATE_ASPNET_CERTIFICATE")]
    [InlineData("true", "false", "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH")]
    [InlineData("false", "true", "DOTNET_GENERATE_ASPNET_CERTIFICATE")]
    [InlineData("0", "false", "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH")]
    [InlineData("false", "False", "DOTNET_GENERATE_ASPNET_CERTIFICATE")]
    [InlineData("false", "", "DOTNET_GENERATE_ASPNET_CERTIFICATE")]
    public async Task A_dotnet_request_missing_or_misvaluing_a_flag_is_refused_before_the_inner_adapter(string? addToPath, string? certificate, string expected)
    {
        var inner = new RecordingAdapter();
        var guard = new SdkSideEffectGuardAdapter(inner);
        var environment = new List<string>();
        if (addToPath is not null)
        {
            environment.AddRange([AddToPath, addToPath]);
        }

        if (certificate is not null)
        {
            environment.AddRange([Certificate, certificate]);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => guard.ExecuteAsync(Request("dotnet.exe", [.. environment]), default));

        Assert.Equal(0, inner.Calls);
        Assert.Equal(0, guard.Launches);
        Assert.Equal(1, guard.Refusals);
        Assert.Equal(expected, guard.LastRefusal);
    }

    [Fact]
    public void The_flag_names_match_case_insensitively_like_the_process_environment_and_the_leaf_check_ignores_case()
    {
        Assert.Empty(SdkSideEffectGuardAdapter.Violations(Request("DOTNET.EXE", "dotnet_add_global_tools_to_path", "false", "Dotnet_Generate_Aspnet_Certificate", "false")));
        Assert.Equal(2, SdkSideEffectGuardAdapter.Violations(Request("DOTNET.EXE")).Count);
    }

    [Fact]
    public void Another_executable_is_never_inspected()
    {
        Assert.Empty(SdkSideEffectGuardAdapter.Violations(Request("node.exe")));
        Assert.Empty(SdkSideEffectGuardAdapter.Violations(Request("tool.exe")));
    }

    private static ProcessExecutionRequest Request(string leaf, params string[] environmentPairs)
    {
        var environment = new Dictionary<string, string>();
        for (var index = 0; index < environmentPairs.Length; index += 2)
        {
            environment[environmentPairs[index]] = environmentPairs[index + 1];
        }

        return new ProcessExecutionRequest
        {
            ExecutablePath = Path.Combine(Path.GetTempPath(), "guard-fixture", leaf),
            WorkingDirectory = Path.GetTempPath(),
            ApprovedRoot = Path.GetTempPath(),
            Timeout = TimeSpan.FromSeconds(1),
            EnvironmentVariables = environment,
        };
    }

    private sealed class RecordingAdapter : IProcessExecutionAdapter
    {
        public int Calls { get; private set; }

        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromException<ProcessExecutionResult>(new Reached());
        }

        public sealed class Reached : Exception;
    }
}
