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

    private static readonly string[] GhIdentity =
    [
        "api", "graphql", "--hostname", "github.com", "--method", "POST",
        "-f", "query=query($owner:String!,$name:String!){repository(owner:$owner,name:$name){id name nameWithOwner}}",
        "-f", "owner=octo-cat", "-f", "name=hello.world",
    ];

    [Fact]
    public void The_github_double_answers_the_fixed_identity_contract_by_echoing_the_requested_names_and_logs_only_its_kind()
    {
        var result = Run("gh", GhIdentity);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            "{\"data\":{\"repository\":{\"id\":\"R_kgDOJourneyFx1\",\"name\":\"hello.world\",\"nameWithOwner\":\"octo-cat/hello.world\"}}}\n",
            result.StandardOutput);
        var log = Directory.EnumerateFiles(_root, "*.jsonl", SearchOption.AllDirectories).Select(File.ReadAllText).Single();
        Assert.Contains("\"kind\":\"repository-identity\"", log);
        Assert.DoesNotContain("octo-cat", log);
        Assert.DoesNotContain("hello.world", log);
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
    [InlineData("api", "graphql")]
    [InlineData("api", "user")]
    public void The_github_double_refuses_every_other_invocation(params string[] arguments)
    {
        Assert.Equal(Unsupported, Run("gh", arguments).ExitCode);
    }

    [Theory]
    [InlineData("hostname", "evil.example")]
    [InlineData("method", "GET")]
    [InlineData("query", "query{viewer{login}}")]
    [InlineData("owner", "bad owner")]
    [InlineData("owner", "-octo")]
    [InlineData("name", "")]
    [InlineData("name", ".git")]
    [InlineData("name", "re/po")]
    public void The_github_double_refuses_a_changed_identity_contract(string part, string value)
    {
        var arguments = GhIdentity.ToArray();
        switch (part)
        {
            case "hostname": arguments[3] = value; break;
            case "method": arguments[5] = value; break;
            case "query": arguments[7] = "query=" + value; break;
            case "owner": arguments[9] = "owner=" + value; break;
            default: arguments[11] = "name=" + value; break;
        }

        Assert.Equal(Unsupported, Run("gh", arguments).ExitCode);
    }
}
