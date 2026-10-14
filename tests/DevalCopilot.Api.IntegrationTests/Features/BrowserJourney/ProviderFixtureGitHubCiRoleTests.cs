using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.BrowserJourney;

public sealed partial class ProviderFixtureContractTests
{
    private static readonly string[] GhCi =
    [
        "api", "graphql", "--hostname", "github.com", "--method", "POST",
        "-f", "query=query($owner:String!,$name:String!,$ref:String!){repository(owner:$owner,name:$name){id name nameWithOwner "
            + "ref(qualifiedName:$ref){name prefix target{__typename ... on Commit{oid statusCheckRollup{state}}}}}}",
        "-f", "owner=octo-cat", "-f", "name=hello.world", "-f", "ref=refs/heads/devalcopilot/workspace/0123456789abcdef0123456789abcdef/3",
    ];

    [Fact]
    public void The_github_double_answers_the_fixed_check_contract_with_the_echoed_branch_the_fixed_commit_and_a_fixed_success_rollup_and_logs_only_its_kind()
    {
        var result = Run("gh", GhCi);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            "{\"data\":{\"repository\":{\"id\":\"R_kgDOJourneyFx1\",\"name\":\"hello.world\",\"nameWithOwner\":\"octo-cat/hello.world\","
            + "\"ref\":{\"name\":\"devalcopilot/workspace/0123456789abcdef0123456789abcdef/3\",\"prefix\":\"refs/heads/\","
            + "\"target\":{\"__typename\":\"Commit\",\"oid\":\"1234567890abcdef1234567890abcdef12345678\","
            + "\"statusCheckRollup\":{\"state\":\"SUCCESS\"}}}}}}\n",
            result.StandardOutput);
        var log = Directory.EnumerateFiles(_root, "*.jsonl", SearchOption.AllDirectories).Select(File.ReadAllText).Single();
        Assert.Contains("\"role\":\"gh\"", log);
        Assert.Contains("\"kind\":\"ci-observation\"", log);
        Assert.DoesNotContain("octo-cat", log);
        Assert.DoesNotContain("hello.world", log);
        Assert.DoesNotContain("devalcopilot/workspace", log);
        Assert.DoesNotContain("SUCCESS", log);
    }

    [Fact]
    public void The_check_contract_does_not_change_the_identity_or_branch_contracts_or_their_log_kinds()
    {
        var identity = Run("gh", GhIdentity);
        var branch = Run("gh", GhBranch);

        Assert.Equal(0, identity.ExitCode);
        Assert.DoesNotContain("statusCheckRollup", identity.StandardOutput);
        Assert.Equal(0, branch.ExitCode);
        Assert.DoesNotContain("statusCheckRollup", branch.StandardOutput);
        var log = string.Join('\n', Directory.EnumerateFiles(_root, "*.jsonl", SearchOption.AllDirectories).Select(File.ReadAllText));
        Assert.Contains("\"kind\":\"repository-identity\"", log);
        Assert.Contains("\"kind\":\"branch-observation\"", log);
        Assert.DoesNotContain("ci-observation", log);
    }

    [Theory]
    [InlineData("hostname", "evil.example")]
    [InlineData("method", "GET")]
    [InlineData("query", "query{viewer{login}}")]
    [InlineData("query", "query($owner:String!,$name:String!,$ref:String!){repository(owner:$owner,name:$name){id name nameWithOwner ref(qualifiedName:$ref){name prefix target{__typename ... on Commit{oid statusCheckRollup{state contexts{totalCount}}}}}}}")]
    [InlineData("owner", "bad owner")]
    [InlineData("name", ".git")]
    [InlineData("ref", "refs/tags/v1")]
    [InlineData("ref", "refs/heads/main")]
    [InlineData("ref", "refs/heads/devalcopilot/workspace/0123456789abcdef0123456789abcdef/0")]
    [InlineData("ref", "refs/heads/devalcopilot/workspace/0123456789ABCDEF0123456789abcdef/3")]
    [InlineData("ref", "devalcopilot/workspace/0123456789abcdef0123456789abcdef/3")]
    public void The_github_double_refuses_a_changed_check_contract(string part, string value)
    {
        var arguments = GhCi.ToArray();
        switch (part)
        {
            case "hostname": arguments[3] = value; break;
            case "method": arguments[5] = value; break;
            case "query": arguments[7] = "query=" + value; break;
            case "owner": arguments[9] = "owner=" + value; break;
            case "name": arguments[11] = "name=" + value; break;
            default: arguments[13] = "ref=" + value; break;
        }

        Assert.Equal(Unsupported, Run("gh", arguments).ExitCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void The_github_double_refuses_an_extra_or_missing_check_argument(int change)
    {
        var arguments = change == 1 ? [.. GhCi, "--jq", ".data"] : GhCi.Take(GhCi.Length - 2).ToArray();

        Assert.Equal(Unsupported, Run("gh", arguments).ExitCode);
    }
}
