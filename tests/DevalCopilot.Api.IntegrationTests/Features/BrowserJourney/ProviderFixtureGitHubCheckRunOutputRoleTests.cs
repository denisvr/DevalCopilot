using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.BrowserJourney;

public sealed partial class ProviderFixtureContractTests
{
    private const string CheckRunMetadataDocument =
        "query($owner:String!,$name:String!,$ref:String!,$id:ID!){repository(owner:$owner,name:$name){id name nameWithOwner "
        + "ref(qualifiedName:$ref){name prefix target{__typename ... on Commit{oid}}}} node(id:$id){__typename ... on CheckRun{id "
        + "repository{id} checkSuite{id repository{id} commit{oid}}}}}";

    private const string CheckRunOutputDocument =
        "query($owner:String!,$name:String!,$ref:String!,$id:ID!){repository(owner:$owner,name:$name){id name nameWithOwner "
        + "ref(qualifiedName:$ref){name prefix target{__typename ... on Commit{oid}}}} node(id:$id){__typename ... on CheckRun{id "
        + "repository{id} checkSuite{id repository{id} commit{oid}} name status conclusion title summary text}}}";

    private static string[] GhCheckRun(string document) =>
    [
        "api", "graphql", "--hostname", "github.com", "--method", "POST",
        "-f", "query=" + document,
        "-f", "owner=octo-cat", "-f", "name=hello.world", "-f", "ref=refs/heads/devalcopilot/workspace/0123456789abcdef0123456789abcdef/3",
        "-f", "id=FX-CHECK-ID-001",
    ];

    private static readonly string[] GhCheckRunMetadata = GhCheckRun(CheckRunMetadataDocument);

    private static readonly string[] GhCheckRunOutput = GhCheckRun(CheckRunOutputDocument);

    private const string CheckRunRoot =
        "{\"data\":{\"repository\":{\"id\":\"R_kgDOJourneyFx1\",\"name\":\"hello.world\",\"nameWithOwner\":\"octo-cat/hello.world\","
        + "\"ref\":{\"name\":\"devalcopilot/workspace/0123456789abcdef0123456789abcdef/3\",\"prefix\":\"refs/heads/\","
        + "\"target\":{\"__typename\":\"Commit\",\"oid\":\"1234567890abcdef1234567890abcdef12345678\"}}},"
        + "\"node\":{\"__typename\":\"CheckRun\",\"id\":\"FX-CHECK-ID-001\",\"repository\":{\"id\":\"R_kgDOJourneyFx1\"},"
        + "\"checkSuite\":{\"id\":\"FX-SUITE-001\",\"repository\":{\"id\":\"R_kgDOJourneyFx1\"},"
        + "\"commit\":{\"oid\":\"1234567890abcdef1234567890abcdef12345678\"}}";

    [Fact]
    public void The_github_double_answers_the_fixed_check_run_metadata_contract_with_the_association_only_and_logs_only_its_kind()
    {
        var result = Run("gh", GhCheckRunMetadata);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(CheckRunRoot + "}}}\n", result.StandardOutput);
        Assert.DoesNotContain("FX-OUTPUT", result.StandardOutput);
        var log = Directory.EnumerateFiles(_root, "*.jsonl", SearchOption.AllDirectories).Select(File.ReadAllText).Single();
        Assert.Contains("\"role\":\"gh\"", log);
        Assert.Contains("\"kind\":\"check-run-metadata-observation\"", log);
        Assert.DoesNotContain("octo-cat", log);
        Assert.DoesNotContain("hello.world", log);
        Assert.DoesNotContain("devalcopilot/workspace", log);
        Assert.DoesNotContain("FX-CHECK", log);
        Assert.DoesNotContain("FX-SUITE", log);
    }

    [Fact]
    public void The_github_double_answers_the_fixed_check_run_output_contract_with_three_fictitious_strings_and_logs_only_its_kind()
    {
        var result = Run("gh", GhCheckRunOutput);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            CheckRunRoot + ",\"name\":\"Journey fixture build / unit\",\"status\":\"COMPLETED\",\"conclusion\":\"FAILURE\","
            + "\"title\":\"FX-OUTPUT-TITLE-001\",\"summary\":\"FX-OUTPUT-SUMMARY-002\",\"text\":\"FX-OUTPUT-TEXT-003\"}}}\n",
            result.StandardOutput);
        var log = Directory.EnumerateFiles(_root, "*.jsonl", SearchOption.AllDirectories).Select(File.ReadAllText).Single();
        Assert.Contains("\"kind\":\"check-run-output-observation\"", log);
        Assert.DoesNotContain("FX-OUTPUT", log);
        Assert.DoesNotContain("FX-CHECK", log);
        Assert.DoesNotContain("octo-cat", log);
        Assert.DoesNotContain("Journey fixture", log);
    }

    [Fact]
    public void The_check_run_contracts_do_not_change_the_earlier_contracts_or_their_log_kinds()
    {
        var contexts = Run("gh", GhContexts);
        var identity = Run("gh", GhIdentity);

        Assert.Equal(0, contexts.ExitCode);
        Assert.DoesNotContain("\"node\":", contexts.StandardOutput);
        Assert.Equal(0, identity.ExitCode);
        Assert.DoesNotContain("\"node\":", identity.StandardOutput);
        var log = string.Join('\n', Directory.EnumerateFiles(_root, "*.jsonl", SearchOption.AllDirectories).Select(File.ReadAllText));
        Assert.Contains("\"kind\":\"check-contexts-observation\"", log);
        Assert.Contains("\"kind\":\"repository-identity\"", log);
        Assert.DoesNotContain("check-run", log);
    }

    [Theory]
    [InlineData("hostname", "evil.example")]
    [InlineData("method", "GET")]
    [InlineData("query", "query{viewer{login}}")]
    [InlineData("query", "query($owner:String!,$name:String!,$ref:String!,$id:ID!){repository(owner:$owner,name:$name){id name nameWithOwner ref(qualifiedName:$ref){name prefix target{__typename ... on Commit{oid}}}} node(id:$id){__typename ... on CheckRun{id name status conclusion title summary text annotations{totalCount}}}}")]
    [InlineData("owner", "bad owner")]
    [InlineData("name", ".git")]
    [InlineData("ref", "refs/tags/v1")]
    [InlineData("ref", "refs/heads/main")]
    [InlineData("ref", "refs/heads/devalcopilot/workspace/0123456789abcdef0123456789abcdef/0")]
    [InlineData("ref", "devalcopilot/workspace/0123456789abcdef0123456789abcdef/3")]
    [InlineData("id", "")]
    [InlineData("id", "bad id")]
    [InlineData("id", "a.b")]
    public void The_github_double_refuses_a_changed_check_run_contract_for_either_phase(string part, string value)
    {
        foreach (var contract in new[] { GhCheckRunMetadata, GhCheckRunOutput })
        {
            var arguments = contract.ToArray();
            switch (part)
            {
                case "hostname": arguments[3] = value; break;
                case "method": arguments[5] = value; break;
                case "query": arguments[7] = "query=" + value; break;
                case "owner": arguments[9] = "owner=" + value; break;
                case "name": arguments[11] = "name=" + value; break;
                case "ref": arguments[13] = "ref=" + value; break;
                default: arguments[15] = "id=" + value; break;
            }

            Assert.Equal(Unsupported, Run("gh", arguments).ExitCode);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void The_github_double_refuses_an_extra_or_missing_check_run_argument_and_a_document_of_the_other_contract(int change)
    {
        var arguments = change switch
        {
            1 => [.. GhCheckRunMetadata, "--jq", ".data"],
            2 => GhCheckRunOutput.Take(GhCheckRunOutput.Length - 2).ToArray(),
            _ => [.. GhContexts, "-f", "id=FX-CHECK-ID-001"],
        };

        Assert.Equal(Unsupported, Run("gh", arguments).ExitCode);
    }
}
