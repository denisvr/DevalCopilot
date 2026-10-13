using System.Text.RegularExpressions;

namespace DevalCopilot.Api.IntegrationTests.BrowserJourney.ProviderFixture;

/// <summary>
/// The GitHub CLI double: the fixed version probe, the one fixed stored-authentication status contract, which answers an
/// already-filtered, active, successful account, and the one fixed read-only repository identity contract, which echoes the requested
/// owner and name back with a fixed opaque identifier. It is only a process-boundary double: it reads no credential, performs no network
/// call and does not evaluate the selector or the GraphQL query, so it proves nothing about how an installed GitHub CLI formats or
/// filters its output or how GitHub answers. Only the status and identity calls are logged (kind only, never an argument), so version
/// probes never change the journeys' invocation evidence.
/// </summary>
public static partial class GhRole
{
    private static readonly string[] StatusPrefix = ["auth", "status", "--hostname", "github.com", "--active", "--json", "hosts", "--jq"];

    private static readonly string[] IdentityPrefix = ["api", "graphql", "--hostname", "github.com", "--method", "POST", "-f"];

    private const string IdentityQuery =
        "query=query($owner:String!,$name:String!){repository(owner:$owner,name:$name){id name nameWithOwner}}";

    public const string FilteredAuthenticatedAnswer = "{\"entries\":[{\"active\":true,\"state\":\"success\"}]}\n";

    public const string RepositoryNodeId = "R_kgDOJourneyFx1";

    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?$")]
    private static partial Regex OwnerName();

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

        throw new FixtureRefusal(
            FixtureRefusal.UnsupportedInvocation,
            "The GitHub CLI double accepts only the version probe, the fixed stored-authentication status contract and the fixed repository identity contract.");
    }
}
