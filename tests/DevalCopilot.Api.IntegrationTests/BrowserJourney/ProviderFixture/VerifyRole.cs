namespace DevalCopilot.Api.IntegrationTests.BrowserJourney.ProviderFixture;

/// <summary>
/// The verification executable: one fixed argument, reads the real candidate file in the owned worktree and exits zero only
/// when that content holds the corrected statement. There is no call counter and no stored state, so passing depends on the
/// correction edit alone.
/// </summary>
public static class VerifyRole
{
    public const string Argument = "check";

    public static int Run(IReadOnlyList<string> args, OwnedLocation location, string currentDirectory)
    {
        if (args.Count != 1 || args[0] != Argument)
        {
            throw new FixtureRefusal(FixtureRefusal.UnsupportedInvocation, "The verification executable accepts only the check argument.");
        }

        var worktree = location.RequireWorktree(currentDirectory);
        var content = File.ReadAllText(location.RequireCandidateFile(worktree));
        var passed = CandidateEdit.IsCorrect(content);
        InvocationLog.Append(
            location,
            new Dictionary<string, object?> { ["role"] = "verify", ["kind"] = "check", ["outcome"] = passed ? "passed" : "failed" });
        if (passed)
        {
            Console.Out.Write("PASS: Total adds its operands.\n");
            return 0;
        }

        Console.Out.Write("verification started\n");
        Console.Error.Write("FAIL " + ManifestInfo.VerificationFailureMarker + ": Total does not add left and right.\n");
        return 1;
    }
}
