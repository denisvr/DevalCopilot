using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.BrowserJourney;

public sealed partial class ProviderFixtureContractTests
{
    private static readonly string[] GhStatus =
        ["auth", "status", "--hostname", "github.com", "--active", "--json", "hosts", "--jq", "{entries: []}"];

    [Fact]
    public void The_github_double_answers_the_version_probe_without_logging_it()
    {
        var result = Run("gh", ["--version"]);

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("gh version ", result.StandardOutput);
        Assert.False(File.Exists(Path.Combine(_root, "fixture", "invocations.jsonl")));
    }

    [Fact]
    public void The_github_double_answers_only_the_fixed_status_contract_with_a_filtered_authenticated_answer_and_logs_the_call()
    {
        var result = Run("gh", GhStatus);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("{\"entries\":[{\"active\":true,\"state\":\"success\"}]}\n", result.StandardOutput);
        var log = Directory.EnumerateFiles(_root, "*.jsonl", SearchOption.AllDirectories).Select(File.ReadAllText).Single();
        Assert.Contains("\"role\":\"gh\"", log);
        Assert.Contains("\"kind\":\"auth-status\"", log);
    }

    [Theory]
    [InlineData("auth", "status")]
    [InlineData("auth", "token")]
    [InlineData("auth", "status", "--hostname", "evil.example", "--active", "--json", "hosts", "--jq", "x")]
    [InlineData("auth", "status", "--hostname", "github.com", "--json", "hosts", "--jq", "x", "--show-token")]
    [InlineData("auth", "status", "--hostname", "github.com", "--active", "--json", "hosts", "--jq", "")]
    [InlineData("repo", "list")]
    public void The_github_double_refuses_every_other_invocation(params string[] arguments)
    {
        Assert.Equal(Unsupported, Run("gh", arguments).ExitCode);
    }
}
