using System.Text.Json.Nodes;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.BrowserJourney;

/// <summary>The Codex double's closed App Server mode as a REAL child process (ADR-0025): exactly the handshake and the one rate-limit
/// read the strict account-usage observation sends, answered from the script the journey writes into the owned root, one scripted read
/// per launch, and a fixed refusal for anything else. It runs from a plain directory (the host launches it from its scratch directory,
/// never a worktree).</summary>
public sealed partial class ProviderFixtureContractTests
{
    private const string InitializeLine =
        """{"method":"initialize","id":0,"params":{"clientInfo":{"name":"DevalCopilot","title":"DevalCopilot","version":"1.0.0"},"capabilities":{}}}""";

    private const string InitializedLine = """{"method":"initialized","params":{}}""";
    private const string ReadLine = """{"method":"account/rateLimits/read","id":1}""";

    private string UsageScript => Path.Combine(_root, "fixture", "account-usage.json");

    private string UsageCounter => Path.Combine(_root, "fixture", "account-usage.reads");

    private void WriteUsageScript(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(UsageScript)!);
        File.WriteAllText(UsageScript, json);
    }

    private Result RunAppServer(string standardInput) =>
        Run("codex", ["app-server", "--stdio"], workingDirectory: _outside, standardInput: standardInput);

    private static string Handshake(string read = ReadLine) => InitializeLine + "\n" + InitializedLine + "\n" + read + "\n";

    [Fact]
    public void The_codex_double_answers_the_exact_app_server_handshake_with_the_scripted_read_and_logs_only_its_index()
    {
        WriteUsageScript("""{"reads":[{"primary":42,"secondary":7}]}""");

        var result = RunAppServer(Handshake());

        Assert.Equal(0, result.ExitCode);
        var lines = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("""{"id":0,"result":{}}""", lines[0]);
        var reply = JsonNode.Parse(lines[1])!;
        Assert.Equal(1, reply["id"]!.GetValue<int>());
        var bucket = reply["result"]!["rateLimitsByLimitId"]!["codex"]!;
        Assert.Equal(42, bucket["primary"]!["usedPercent"]!.GetValue<int>());
        Assert.Equal(7, bucket["secondary"]!["usedPercent"]!.GetValue<int>());
        Assert.Equal(42, reply["result"]!["rateLimits"]!["primary"]!["usedPercent"]!.GetValue<int>());
        Assert.Null(bucket["rateLimitReachedType"]);
        Assert.Equal("1", File.ReadAllText(UsageCounter));
        var log = File.ReadAllLines(Path.Combine(_root, "fixture", "invocations.jsonl")).Last();
        Assert.Equal("""{"role":"codex","kind":"account_usage","usageReadIndex":0}""", log);
    }

    [Fact]
    public void Each_launch_answers_its_own_scripted_read_and_the_last_one_repeats()
    {
        WriteUsageScript("""{"reads":[{"primary":10},{"primary":85},{"unavailable":true}]}""");

        var first = JsonNode.Parse(RunAppServer(Handshake()).StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)[1])!;
        var second = JsonNode.Parse(RunAppServer(Handshake()).StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)[1])!;
        var third = JsonNode.Parse(RunAppServer(Handshake()).StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)[1])!;
        var fourth = JsonNode.Parse(RunAppServer(Handshake()).StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)[1])!;

        Assert.Equal(10, first["result"]!["rateLimits"]!["primary"]!["usedPercent"]!.GetValue<int>());
        Assert.Equal(85, second["result"]!["rateLimits"]!["primary"]!["usedPercent"]!.GetValue<int>());
        Assert.NotNull(third["error"]);
        Assert.NotNull(fourth["error"]);
        Assert.Equal("4", File.ReadAllText(UsageCounter));
    }

    [Fact]
    public void A_scripted_reached_state_is_carried_and_an_absent_window_is_null()
    {
        WriteUsageScript("""{"reads":[{"primary":1,"reachedType":"rate_limit_reached"}]}""");

        var reply = JsonNode.Parse(RunAppServer(Handshake()).StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)[1])!;

        Assert.Equal("rate_limit_reached", reply["result"]!["rateLimits"]!["rateLimitReachedType"]!.GetValue<string>());
        Assert.Null(reply["result"]!["rateLimits"]!["secondary"]);
    }

    [Theory]
    [InlineData("""{"method":"initialize","id":0,"params":{"capabilities":{}}}""")]
    [InlineData("""{"method":"thread/start","id":0,"params":{}}""")]
    [InlineData("")]
    public void The_codex_double_refuses_a_handshake_that_is_not_the_closed_contract(string firstLine)
    {
        WriteUsageScript("""{"reads":[{"primary":1}]}""");

        var result = RunAppServer(firstLine + "\n" + InitializedLine + "\n" + ReadLine + "\n");

        Assert.Equal(Unsupported, result.ExitCode);
        Assert.False(File.Exists(UsageCounter));
    }

    [Theory]
    [InlineData("""{"method":"account/rateLimits/read","id":2}""")]
    [InlineData("""{"method":"model/list","id":1}""")]
    [InlineData("""{"method":"account/rateLimits/reset","id":1}""")]
    [InlineData("""{"method":"turn/start","id":1,"params":{}}""")]
    public void The_codex_double_refuses_any_request_other_than_the_one_rate_limit_read(string request)
    {
        WriteUsageScript("""{"reads":[{"primary":1}]}""");

        var result = RunAppServer(Handshake(request));

        Assert.Equal(Unsupported, result.ExitCode);
        Assert.False(File.Exists(UsageCounter));
    }

    [Fact]
    public void The_codex_double_refuses_extra_arguments_a_missing_script_and_an_invalid_script()
    {
        Assert.Equal(Unsupported, Run("codex", ["app-server", "--stdio", "--listen"], workingDirectory: _outside, standardInput: Handshake()).ExitCode);
        Assert.Equal(Unsupported, Run("codex", ["app-server"], workingDirectory: _outside, standardInput: Handshake()).ExitCode);

        Assert.Equal(OwnershipRefused, RunAppServer(Handshake()).ExitCode);

        WriteUsageScript("not json");
        Assert.Equal(Unsupported, RunAppServer(Handshake()).ExitCode);
        WriteUsageScript("""{"reads":[]}""");
        Assert.Equal(Unsupported, RunAppServer(Handshake()).ExitCode);
        Assert.False(File.Exists(UsageCounter));
    }
}
