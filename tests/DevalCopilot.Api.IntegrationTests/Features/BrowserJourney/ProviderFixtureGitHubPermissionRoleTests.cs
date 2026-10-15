using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.BrowserJourney;

public sealed partial class ProviderFixtureContractTests
{
    private static readonly string[] GhPermission =
    [
        "api", "graphql", "--hostname", "github.com", "--method", "POST",
        "-f", "query=query($owner:String!,$name:String!){repository(owner:$owner,name:$name){id name nameWithOwner viewerPermission}}",
        "-f", "owner=octo-cat", "-f", "name=hello.world",
    ];

    [Fact]
    public void The_github_double_answers_the_fixed_permission_contract_with_the_echoed_names_the_fixed_identifier_and_a_fixed_role_and_logs_only_its_kind()
    {
        var result = Run("gh", GhPermission);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            "{\"data\":{\"repository\":{\"id\":\"R_kgDOJourneyFx1\",\"name\":\"hello.world\",\"nameWithOwner\":\"octo-cat/hello.world\","
            + "\"viewerPermission\":\"MAINTAIN\"}}}\n",
            result.StandardOutput);
        var log = Directory.EnumerateFiles(_root, "*.jsonl", SearchOption.AllDirectories).Select(File.ReadAllText).Single();
        Assert.Contains("\"role\":\"gh\"", log);
        Assert.Contains("\"kind\":\"repository-permission-observation\"", log);
        Assert.DoesNotContain("octo-cat", log);
        Assert.DoesNotContain("hello.world", log);
        Assert.DoesNotContain("MAINTAIN", log);
        Assert.DoesNotContain("R_kgDOJourneyFx1", log);
    }

    [Fact]
    public void The_permission_contract_does_not_change_the_identity_or_branch_contracts_or_their_log_kinds()
    {
        var identity = Run("gh", GhIdentity);
        var branch = Run("gh", GhBranch);

        Assert.Equal(0, identity.ExitCode);
        Assert.DoesNotContain("viewerPermission", identity.StandardOutput);
        Assert.Equal(0, branch.ExitCode);
        Assert.DoesNotContain("viewerPermission", branch.StandardOutput);
        var log = string.Join('\n', Directory.EnumerateFiles(_root, "*.jsonl", SearchOption.AllDirectories).Select(File.ReadAllText));
        Assert.Contains("\"kind\":\"repository-identity\"", log);
        Assert.Contains("\"kind\":\"branch-observation\"", log);
        Assert.DoesNotContain("repository-permission-observation", log);
    }

    [Theory]
    [InlineData("hostname", "evil.example")]
    [InlineData("method", "GET")]
    [InlineData("query", "query{viewer{login}}")]
    [InlineData("query", "query($owner:String!,$name:String!){repository(owner:$owner,name:$name){id name nameWithOwner viewerPermission viewerCanAdminister}}")]
    [InlineData("query", "query($owner:String!,$name:String!){repository(owner:$owner,name:$name){id name nameWithOwner}}")]
    [InlineData("owner", "bad owner")]
    [InlineData("name", ".git")]
    public void The_github_double_refuses_a_changed_permission_contract(string part, string value)
    {
        var arguments = GhPermission.ToArray();
        switch (part)
        {
            case "hostname": arguments[3] = value; break;
            case "method": arguments[5] = value; break;
            case "query": arguments[7] = "query=" + value; break;
            case "owner": arguments[9] = "owner=" + value; break;
            default: arguments[11] = "name=" + value; break;
        }

        // The plain identity query is its own contract and is answered by it, never by this one.
        var expected = part == "query" && value.EndsWith("nameWithOwner}}", StringComparison.Ordinal) ? 0 : Unsupported;
        var result = Run("gh", arguments);
        Assert.Equal(expected, result.ExitCode);
        Assert.DoesNotContain("viewerPermission", result.StandardOutput);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void The_github_double_refuses_an_extra_or_missing_permission_argument(int change)
    {
        var arguments = change == 1 ? [.. GhPermission, "--jq", ".data"] : GhPermission.Take(GhPermission.Length - 2).ToArray();

        Assert.Equal(Unsupported, Run("gh", arguments).ExitCode);
    }
}
