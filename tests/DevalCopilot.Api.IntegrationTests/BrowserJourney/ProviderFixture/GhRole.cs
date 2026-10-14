using System.Text.RegularExpressions;

namespace DevalCopilot.Api.IntegrationTests.BrowserJourney.ProviderFixture;

/// <summary>
/// The GitHub CLI double: the fixed version probe, the one fixed stored-authentication status contract, which answers an
/// already-filtered, active, successful account, the one fixed read-only repository identity contract, which echoes the requested
/// owner and name back with a fixed opaque identifier, and the one fixed read-only branch contract, which additionally echoes the
/// requested owned branch with a fixed commit that is never the recorded one, and the one fixed read-only check contract, which answers the
/// same ref and fixed commit with a fixed SUCCESS rollup. It is only a process-boundary double: it reads no
/// credential, performs no network
/// call and does not evaluate the selector or the GraphQL query, so it proves nothing about how an installed GitHub CLI formats or
/// filters its output or how GitHub answers. Only the status, identity, branch and check calls are logged (kind only, never an argument), so version
/// probes never change the journeys' invocation evidence.
/// </summary>
public static partial class GhRole
{
    private static readonly string[] StatusPrefix = ["auth", "status", "--hostname", "github.com", "--active", "--json", "hosts", "--jq"];

    private static readonly string[] IdentityPrefix = ["api", "graphql", "--hostname", "github.com", "--method", "POST", "-f"];

    private const string IdentityQuery =
        "query=query($owner:String!,$name:String!){repository(owner:$owner,name:$name){id name nameWithOwner}}";

    private const string BranchQuery =
        "query=query($owner:String!,$name:String!,$ref:String!){repository(owner:$owner,name:$name){id name nameWithOwner "
        + "ref(qualifiedName:$ref){name prefix target{__typename oid}}}}";

    private const string CiQuery =
        "query=query($owner:String!,$name:String!,$ref:String!){repository(owner:$owner,name:$name){id name nameWithOwner "
        + "ref(qualifiedName:$ref){name prefix target{__typename ... on Commit{oid statusCheckRollup{state}}}}}}";

    public const string RemoteBranchCommit = "1234567890abcdef1234567890abcdef12345678";

    public const string RemoteRollupState = "SUCCESS";

    public const string FilteredAuthenticatedAnswer = "{\"entries\":[{\"active\":true,\"state\":\"success\"}]}\n";

    public const string RepositoryNodeId = "R_kgDOJourneyFx1";

    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?$")]
    private static partial Regex OwnerName();

    [GeneratedRegex("^devalcopilot/workspace/[0-9a-f]{32}/[1-9][0-9]{0,9}$")]
    private static partial Regex OwnedBranchName();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$")]
    private static partial Regex RepositoryName();

    public static int Run(IReadOnlyList<string> args, OwnedLocation location)
    {
        if (args.Count == 1 && args[0] == "--version")
        {
            Console.Out.Write("gh version 2.50.0 (browser journey fixture)\n");
            return 0;
        }

        if (args.Count == StatusPrefix.Length + 1 && args.Take(StatusPrefix.Length).SequenceEqual(StatusPrefix)
            && args[StatusPrefix.Length].Length is > 0 and <= 1024)
        {
            InvocationLog.Append(location, new Dictionary<string, object?> { ["role"] = "gh", ["kind"] = "auth-status" });
            Console.Out.Write(FilteredAuthenticatedAnswer);
            return 0;
        }

        if (args.Count == 12 && args.Take(IdentityPrefix.Length).SequenceEqual(IdentityPrefix) && args[7] == IdentityQuery
            && args[8] == "-f" && args[9].StartsWith("owner=", StringComparison.Ordinal) && args[10] == "-f"
            && args[11].StartsWith("name=", StringComparison.Ordinal))
        {
            var owner = args[9]["owner=".Length..];
            var name = args[11]["name=".Length..];
            if (OwnerName().IsMatch(owner) && RepositoryName().IsMatch(name))
            {
                InvocationLog.Append(location, new Dictionary<string, object?> { ["role"] = "gh", ["kind"] = "repository-identity" });
                Console.Out.Write(
                    "{\"data\":{\"repository\":{\"id\":\"" + RepositoryNodeId + "\",\"name\":\"" + name + "\",\"nameWithOwner\":\""
                    + owner + "/" + name + "\"}}}\n");
                return 0;
            }
        }

        if (args.Count == 14 && args.Take(IdentityPrefix.Length).SequenceEqual(IdentityPrefix) && args[7] == BranchQuery
            && args[8] == "-f" && args[9].StartsWith("owner=", StringComparison.Ordinal) && args[10] == "-f"
            && args[11].StartsWith("name=", StringComparison.Ordinal) && args[12] == "-f"
            && args[13].StartsWith("ref=refs/heads/", StringComparison.Ordinal))
        {
            var owner = args[9]["owner=".Length..];
            var name = args[11]["name=".Length..];
            var branch = args[13]["ref=refs/heads/".Length..];
            if (OwnerName().IsMatch(owner) && RepositoryName().IsMatch(name) && OwnedBranchName().IsMatch(branch))
            {
                InvocationLog.Append(location, new Dictionary<string, object?> { ["role"] = "gh", ["kind"] = "branch-observation" });
                Console.Out.Write(
                    "{\"data\":{\"repository\":{\"id\":\"" + RepositoryNodeId + "\",\"name\":\"" + name + "\",\"nameWithOwner\":\""
                    + owner + "/" + name + "\",\"ref\":{\"name\":\"" + branch + "\",\"prefix\":\"refs/heads/\",\"target\":"
                    + "{\"__typename\":\"Commit\",\"oid\":\"" + RemoteBranchCommit + "\"}}}}}\n");
                return 0;
            }
        }

        if (args.Count == 14 && args.Take(IdentityPrefix.Length).SequenceEqual(IdentityPrefix) && args[7] == CiQuery
            && args[8] == "-f" && args[9].StartsWith("owner=", StringComparison.Ordinal) && args[10] == "-f"
            && args[11].StartsWith("name=", StringComparison.Ordinal) && args[12] == "-f"
            && args[13].StartsWith("ref=refs/heads/", StringComparison.Ordinal))
        {
            var owner = args[9]["owner=".Length..];
            var name = args[11]["name=".Length..];
            var branch = args[13]["ref=refs/heads/".Length..];
            if (OwnerName().IsMatch(owner) && RepositoryName().IsMatch(name) && OwnedBranchName().IsMatch(branch))
            {
                InvocationLog.Append(location, new Dictionary<string, object?> { ["role"] = "gh", ["kind"] = "ci-observation" });
                Console.Out.Write(
                    "{\"data\":{\"repository\":{\"id\":\"" + RepositoryNodeId + "\",\"name\":\"" + name + "\",\"nameWithOwner\":\""
                    + owner + "/" + name + "\",\"ref\":{\"name\":\"" + branch + "\",\"prefix\":\"refs/heads/\",\"target\":"
                    + "{\"__typename\":\"Commit\",\"oid\":\"" + RemoteBranchCommit + "\",\"statusCheckRollup\":{\"state\":\""
                    + RemoteRollupState + "\"}}}}}}\n");
                return 0;
            }
        }

        throw new FixtureRefusal(
            FixtureRefusal.UnsupportedInvocation,
            "The GitHub CLI double accepts only the version probe, the fixed stored-authentication status contract and the fixed repository identity, branch and check contracts.");
    }
}
