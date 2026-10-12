namespace DevalCopilot.Api.IntegrationTests.BrowserJourney.ProviderFixture;

/// <summary>
/// The GitHub CLI double: the fixed version probe and the one fixed stored-authentication status contract, which answers an
/// already-filtered, active, successful account. It is only a process-boundary double: it reads no credential, performs no network
/// call and does not evaluate the selector, so it proves nothing about how an installed GitHub CLI formats or filters its output.
/// Only the status call is logged, so version probes never change the journeys' invocation evidence.
/// </summary>
public static class GhRole
{
    private static readonly string[] StatusPrefix = ["auth", "status", "--hostname", "github.com", "--active", "--json", "hosts", "--jq"];

    public const string FilteredAuthenticatedAnswer = "{\"entries\":[{\"active\":true,\"state\":\"success\"}]}\n";

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

        throw new FixtureRefusal(FixtureRefusal.UnsupportedInvocation, "The GitHub CLI double accepts only the version probe and the fixed stored-authentication status contract.");
    }
}
